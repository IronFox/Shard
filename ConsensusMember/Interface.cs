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
using VectorMath;

namespace Consensus
{
	public interface INotifiable
	{
		void OnMessageCommit(Address clientAddress, ClientMessage message);
		void OnGenerationEnd(int generation);
		void OnAddressMismatchConsensusLoss(Address locallyBound, Address globallyRegistered);
		void OnOutOfConfig(Configuration newConfig, Configuration.Member memberID);
		void OnConsensusChange(Consensus.Status newState, Consensus.Identity newLeader);
	}
	public class Interface : Node
	{
		public ShardID MyID { get; private set; }

		private Thread gecThread;
		private int actualPort;

		public readonly ThreadOperations ThreadOps;

		public void AwaitClosure()
		{
			gecThread.Join();
		}

		public readonly INotifiable Notify;

		public override Address GetAddress(int replicaIndex)
		{
			return BaseDB.TryGetAddress(new ShardID(MyID.XYZ, replicaIndex)).ConsensusAddress;
		}
		
		public static Configuration BuildConfig()
		{
			BaseDB.SDConfigContainer cfg = BaseDB.SD;
			while (cfg == null)
			{
				Log.Message("Requerying SD configuration...");
				Thread.Sleep(100);
				cfg = BaseDB.SD;
			}
			return new Configuration(EnumerateMembers(-cfg.witnessCount, cfg.replicaCount - 1));
		}

		private static IEnumerable<string> CombineRevs(string rev, string[] revisions)
		{
			yield return rev;
			foreach (var s in revisions)
				yield return s;
		}

		private static IEnumerable<Configuration.Member> EnumerateMembers(int first, int last)
		{
			for (int i = first; i <= last; i++)
				yield return Member(i);
		}

		private static Configuration.Member Member(int i)
		{
			return new Configuration.Member(i, i == -1 || i == 0);
		}

		public Interface(Configuration.Member self, Address selfAddress, Int3 myCoords, ThreadOperations threadOps, INotifiable notify, Action<Address> onAddressBound=null):base(self)
		{
			Notify = notify;
			ThreadOps = threadOps;
			var cfg = BuildConfig();
			if (!cfg.ContainsIdentifier(self))
				throw new ArgumentOutOfRangeException("Given self address is not contained by current SD configuration");
			Log.Message("Starting consensus with configuration "+cfg);
			actualPort = Start(cfg, selfAddress, onAddressBound);
			MyID = new ShardID(myCoords,self.Identifier);
			if (threadOps != ThreadOperations.Nothing)
			{
				gecThread = new Thread(new ThreadStart(GECThreadMain));
				gecThread.Start();
			}

		}

		private static void PublishAddress(FullShardAddress address)
		{
			Log.Message("Publishing address: " + address);
			BaseDB.PutNow(address);

		}

		[Flags]
		public enum ThreadOperations
		{
			Nothing = 0,
			CheckConfiguration = 1,
			ScheduleGECs = 2,

			Everything = int.MaxValue
		}


		public Interface(ShardID myID, int peerPort, int consensusPort, bool updateAddress, ThreadOperations threadOps, INotifiable notify) :
			this(Member(myID.ReplicaLevel), GetMyAddress(consensusPort),myID.XYZ, threadOps, notify,
				updateAddress ? addr => PublishAddress(new FullShardAddress(myID, addr.Host, peerPort, addr.Port)) : (Action<Address>)null
			)
		{}

		private static object ipLock = new object();
		private static string localIP = null;
		private static Address GetMyAddress(int consensusPort)
		{
			lock (ipLock)
			{
				if (localIP == null)
				{
					Log.Message("Detecting address...");
					//https://stackoverflow.com/questions/6803073/get-local-ip-address - Mr.Wang from Next Door
					using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
					{
						socket.Connect("8.8.8.8", 65530);
						IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
						localIP = endPoint.Address.ToString();
					}
				}
				var rs= new Address(localIP, consensusPort);
				Log.Message("Done. Address is "+rs);
				return rs;

			}

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
							Log.Message("Included g"+bag.Generation+". DTW now at "+currentSeconds);
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

			public void Commit(Node node, CommitID myID)
			{
				var parent = node as Interface;
				parent.timing.Include(EndedGeneration, parent.Configuration.Size, Delay);
			}

			public override string ToString()
			{
				return "TimeReport g" + EndedGeneration + ", d=" + Delay;
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

			public override string ToString() => "GEC g" + EndedGeneration+", dispatched "+TimeStamp;

			public void Commit(Node node, CommitID myID)
			{
				var parent = node as Interface;
				if (EndedGeneration < parent.generation)
					return;

				parent.gecCommits.Enqueue(new Tuple<CommitID, int>(myID, EndedGeneration));
				CommitID flush = CommitID.None;
				Tuple<CommitID, int> check;
				while (parent.gecCommits.TryPeek(out check) && check.Item2 +2 < EndedGeneration)
				{
					parent.gecCommits.TryDequeue(out check);
					if (check.Item2 + 2 < EndedGeneration)
						flush = check.Item1;
				}
				if (flush != CommitID.None)
				{
					Log.Message("Collecting fossils from " + flush);
					parent.RemoveFossils(flush, true);
				}

				parent.generation = EndedGeneration + 1;
				parent.Notify.OnGenerationEnd(EndedGeneration);
				parent.Schedule(new TimeWindowReport(EndedGeneration,Clock.Now - TimeStamp));

			}
		}

		public void ForwardMessageGeneration(int gen)
		{
			generation = gen;
		}

		private int generation = 0;
		private ConcurrentQueue<Tuple<CommitID, int>> gecCommits = new ConcurrentQueue<Tuple<CommitID, int>>();
		private void GECThreadMain()
		{
			int last = -1;
			while (!IsDisposed)
			{
				if (ThreadOps.HasFlag(ThreadOperations.CheckConfiguration))
				{
					var cfg = BuildConfig();
					if (cfg != Config)
					{
						Log.Message("Change in SD configuration detected: " + Config + "->" + cfg);
						if (!cfg.ContainsIdentifier(MyID.ReplicaLevel))
							throw new ArgumentOutOfRangeException("Given self address is no longer contained by current SD configuration");
						base.ChangeConfiguration(cfg);
					}
				}

				if (ThreadOps.HasFlag(ThreadOperations.ScheduleGECs))
				{

					var current = generation;
					Clock.SleepUntil(GetDeadline(current) - timing.DeliveryEstimation);
					//Log.Message("Progressing ");
					if (last != current)
					{
						Schedule(new GEC(current));
						last = current;
					}
				}
				else
				{
					Thread.Sleep(500);
				}
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

			public void Commit(Node node, CommitID myID)
			{
				var parent = node as Interface;
				parent.Notify.OnMessageCommit(confirmTo, msg);
			}
		}

		public void Dispatch(ClientMessage msg, Address confirmTo)
		{
			Schedule(new CommitMessage(msg, confirmTo));
		}

		public override void OnAddressMismatchDispose()
		{
			Notify.OnAddressMismatchConsensusLoss(BoundAddress,PublicAddress);
		}

		public override void OnOutOfConfig(Configuration newConfig)
		{
			Notify.OnOutOfConfig(newConfig, MemberID);
		}

		public override void OnConsensusChange(Status newState, Identity newLeader)
		{
			Notify.OnConsensusChange(newState, newLeader);
		}
	}
}
