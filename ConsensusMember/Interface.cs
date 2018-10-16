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
	public class Interface : Node
	{
		public ShardID MyID { get; private set; }

		private Thread gecThread;

		public Interface(Tuple<Configuration,int, ShardID> cfg, int peerPort, bool updateAddress) : base(cfg.Item1, cfg.Item2)
		{
			MyID = cfg.Item3;
			int consensusPort = base.Address().Port;

			if (updateAddress)
			{
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
			gecThread = new Thread(new ThreadStart(GECThreadMain));
			gecThread.Start();
		}

		public Interface(ShardID myID, int peerPort, bool updateAddress):this(ConstructConfig(myID), peerPort, updateAddress)
		{
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
		

		/// <summary>
		/// Queries messages received during the specified generation.
		/// The generation and all predecessors are removed if the returned message pack is found to be complete
		/// </summary>
		/// <param name="generation"></param>
		/// <returns></returns>
		public ExtMessagePack QueryMessages(int generation)
		{
			IncomingMessages current = incomingMessages;
			if (generation > current.Generation)
				return new ExtMessagePack(new MessagePack());
			if (generation == current.Generation)
				return new ExtMessagePack(current.Export(false));
			if (generations.TryGetValue(generation, out current))
				return new ExtMessagePack(current.Export(true));
			return new ExtMessagePack(true);
		}

		private class IncomingMessages
		{
			private readonly ConcurrentBag<ClientMessage> bag = new ConcurrentBag<ClientMessage>();
			public readonly int Generation;

			public IncomingMessages(int generation)
			{
				Generation = generation;
			}

			public MessagePack Export(bool complete)
			{
				Dictionary<Guid, List<ClientMessage>> temp = new Dictionary<Guid, List<ClientMessage>>();
				// The key equals the targeted entity Guid or Guid.Empty if the message should be broadcast to all entities.
				foreach (var m in bag)
					temp.GetOrCreate(m.ID.To).Add(m);
				Dictionary<Guid, ClientMessage[]> dict = new Dictionary<Guid, ClientMessage[]>();
				foreach (var p in temp)
					dict.Add(p.Key, p.Value.ToArray());
				return new MessagePack(dict, complete);
			}

			internal void Add(ClientMessage msg)
			{
				bag.Add(msg);
			}
		}

		private IncomingMessages incomingMessages  = new IncomingMessages(0);
		private readonly ConcurrentDictionary<int, IncomingMessages> generations = new ConcurrentDictionary<int, IncomingMessages>();
		
		private static class Timing
		{
			public class SampleSet
			{
				public readonly int Generation;
				public readonly ConcurrentBag<TimeSpan> Samples = new ConcurrentBag<TimeSpan>();
				public SampleSet(int gen)
				{
					Generation = gen;
				}

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
			public static ConcurrentQueue<SampleSet> generations = new ConcurrentQueue<SampleSet>();
			public static int minGeneration = 0;
			public static double currentSeconds = 0.5;
			public const double Alpha = 1.0 / 3.0;
			public static void TrimOut(int generationOrOlder)
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

			public static TimeSpan DeliveryEstimation
			{
				get
				{
					lock (generations)
					{
						double current = currentSeconds;
						foreach (var b in generations)
						{
							current = b.Max * Alpha + current * (1.0 - Alpha);
						}
						return TimeSpan.FromSeconds(current);
					}
				}
			}

			public static void Include(int generation, TimeSpan delta)
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
						var set = new SampleSet(generation);
						set.Samples.Add(delta);
						generations.Enqueue(set);
					}
				}
			}
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
				Timing.Include(EndedGeneration, Delay);
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
				var current = parent.incomingMessages;
				if (current.Generation > EndedGeneration)
					return;
				if (!parent.generations.TryAdd(current.Generation, current))
					return;
				parent.incomingMessages = new IncomingMessages(EndedGeneration + 1);
				parent.Commit(new TimeWindowReport(EndedGeneration,Clock.Now - TimeStamp));
			}
		}

		private void GECThreadMain()
		{
			while (true)
			{
				var current = incomingMessages;
				Clock.SleepUntil(GetDeadline(current.Generation) - Timing.DeliveryEstimation);
				Log.Message("Progressing ");
				Commit(new GEC(current.Generation));
			}
		}

		[Serializable]
		private class CommitMessage : ICommitable
		{
			private ClientMessage msg;

			public CommitMessage(ClientMessage msg)
			{
				this.msg = msg;
			}

			public void Commit(Node node)
			{
				var parent = node as Interface;
				parent.incomingMessages.Add(msg);
			}
		}

		public void Dispatch(ClientMessage msg)
		{
			Commit(new CommitMessage(msg));
		}
	}
}
