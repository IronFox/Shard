using Base;
using Shard;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Consensus
{
	public interface INotifiable
	{
		void OnMessageCommit(Address clientAddress, ClientMessage message);
		void OnGenerationEnd(int generation);
	}
	public class Interface : Node
	{
		public ShardID MyID { get; private set; }

		private Thread gecThread;

		public readonly INotifiable Notify;

		public Interface(Configuration cfg, int myIndex, ShardID myID, INotifiable notify) : base(cfg, myIndex)
		{
			MyID = myID;
			Notify = notify;
			gecThread = new Thread(new ThreadStart(GECThreadMain));
			gecThread.Start();
		}

		public Interface(Tuple<Configuration,int, ShardID> cfg, INotifiable notify) : this(cfg.Item1, cfg.Item2, cfg.Item3,notify)
		{}

		public Interface(ShardID myID, int peerPort, bool updateAddress, INotifiable notify) :this(ConstructConfig(myID), notify)
		{
			if (updateAddress)
			{
				int consensusPort = base.Address().Port;
				Log.Message("Detecting address...");
				//https://stackoverflow.com/questions/6803073/get-local-ip-address - Mr.Wang from Next Door
				string localIP;
				using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
				{
					socket.Connect("8.8.8.8", 65530);
					IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
					localIP = endPoint.Address.ToString();
				}
				var address = new FullShardAddress(MyID, localIP, peerPort, consensusPort);
				Log.Message("Publishing address: " + address);
				BaseDB.PutNow(address);
			}
		}

		private static Tuple<Configuration, int, ShardID> ConstructConfig(ShardID myID)
		{
			Func<Address>[] addresses = new Func<Address>[3]; //0, -1, -2
			int at = -myID.ReplicaLevel;
			for (int i = 0; i < 3; i++)
				if (i != at)
					addresses[at] = () => BaseDB.TryGetConsensusAddress(new ShardID(myID.XYZ, i));
			//else
			//	addresses[at] = null;	//default


			Configuration cfg = new Configuration(addresses);

			return new Tuple<Configuration, int, ShardID>(cfg, at, myID);
		}
		
		
		
		


		
		private class Timing
		{
			public class SampleSet
			{
				public readonly int Generation;
				public readonly int ConsensusSize;
				public readonly ConcurrentBag<TimeSpan> Samples = new ConcurrentBag<TimeSpan>();
				public SampleSet(int gen, int consensusSize)
				{
					Generation = gen;
					ConsensusSize = consensusSize;
				}

				public bool IsComplete => Samples.Count >= ConsensusSize;

				public double Max
				{
					get
					{
						double max = 0;
						foreach (var s in Samples)
							max = Math.Max(max, s.TotalSeconds);
						return max;
					}
				}
			}
			public ConcurrentQueue<SampleSet> generations = new ConcurrentQueue<SampleSet>();
			public int minGeneration = 0;
			public double currentSeconds = 0.5;
			public const double Alpha = 1.0 / 3.0;
			public void TrimOut(int generationOrOlder)
			{
				lock(generations)
				{
					if (generationOrOlder >= minGeneration)
					{
						SampleSet bag;
						while (generations.TryPeek(out bag) && bag.Generation <= generationOrOlder)
						{
							generations.TryDequeue(out bag);
							currentSeconds = bag.Max * Alpha + currentSeconds * (1.0 - Alpha);
						}
						minGeneration = generationOrOlder + 1;
					}
				}
			}

			public TimeSpan DeliveryEstimation
			{
				get
				{
					lock (generations)
					{
						double current = currentSeconds;
						foreach (var b in generations)
						{
							if (b.IsComplete)
								current = b.Max * Alpha + current * (1.0 - Alpha);
						}
						return TimeSpan.FromSeconds(current);
					}
				}
			}

			public void Include(int generation, int consensusSize, TimeSpan delta)
			{
				lock (generations)
				{ 
					if (generation < minGeneration)
						return;
					int max = 0;
					foreach (var b in generations)
						if (b.Generation == generation)
						{
							b.Samples.Add(delta);
							return;
						}
						else
							max = b.Generation;
					if (generations.IsEmpty)
					{
						minGeneration = generation;
						var set = new SampleSet(generation, consensusSize);
						set.Samples.Add(delta);
						generations.Enqueue(set);
					}
				}
			}
		}


		private readonly Timing timing = new Timing();
		public void TrimOut(int generationOrOlder)
		{
			timing.TrimOut(generationOrOlder);
		}


		private static DateTime GetDeadline(int generation)
		{
			var c = TimingInfo.Current;

			return c.GetGenerationStart(generation+1) - c.CSApplicationTimeWindow;
		}

		[Serializable]
		private class TimeWindowReport : ICommitable
		{
			public readonly int EndedGeneration;
			public readonly TimeSpan Delay;

			public TimeWindowReport(int endedGeneration, TimeSpan delay)
			{
				EndedGeneration = endedGeneration;
				Delay = delay;
			}

			public void Commit(Node node)
			{
				var parent = node as Interface;
				parent.timing.Include(EndedGeneration, parent.Configuration.Size, Delay);
			}
		}

		[Serializable]
		private class GEC : ICommitable
		{
			public readonly int EndedGeneration;
			public readonly DateTime TimeStamp;
			public GEC(int generation)
			{
				EndedGeneration = generation;
				TimeStamp = Clock.Now;
			}
			public void Commit(Node node)
			{
				var parent = node as Interface;
				if (EndedGeneration <= parent.generation)
					return;
				parent.generation = EndedGeneration + 1;
				parent.Notify.OnGenerationEnd(EndedGeneration);
				parent.Commit(new TimeWindowReport(EndedGeneration,Clock.Now - TimeStamp));
			}
		}

		public void ForwardMessageGeneration(int gen)
		{
			generation = gen;
		}

		private int generation = 0;
		private void GECThreadMain()
		{
			while (true)
			{
				var current = generation;
				Clock.SleepUntil(GetDeadline(current) - timing.DeliveryEstimation);
				Log.Message("Progressing ");
				Commit(new GEC(current));
			}
		}

		[Serializable]
		private class CommitMessage : ICommitable
		{
			private ClientMessage msg;
			private Address confirmTo;

			public CommitMessage(ClientMessage msg, Address confirmTo)
			{
				this.msg = msg;
				this.confirmTo = confirmTo;
			}

			public void Commit(Node node)
			{
				var parent = node as Interface;
				parent.Notify.OnMessageCommit(confirmTo, msg);
			}
		}

		public void Dispatch(ClientMessage msg, Address confirmTo)
		{
			Commit(new CommitMessage(msg, confirmTo));
		}
	}
}
