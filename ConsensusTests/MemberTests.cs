using Microsoft.VisualStudio.TestTools.UnitTesting;
using Consensus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using Base;

namespace Consensus.Tests
{
	[TestClass()]
	public class MemberTests
	{
		public static Random random = new Random();

		[TestMethod()]
		public void AddressTest()
		{
			Assert.AreEqual(new Address("localhost", 1024), new Address(1024));
			Assert.AreEqual(new Address("127.0.0.1", 1024), new Address(1024));
			Assert.AreEqual(new Address("::1", 1024), new Address(1024));
		}


		private class ClusterNode : Node
		{
			private Func<int, Address> getAddressOf;
			public readonly int Port;
			public ClusterNode(Configuration cfg, Configuration.Member self, Address selfAddress, Func<int, Address> getAddressOf):base(self)
			{
				this.getAddressOf = getAddressOf;
				Port = Start(cfg, selfAddress);
			}

			public override Address GetAddress(int memberID)
			{
				return getAddressOf(memberID);
			}

			public override void OnAddressMismatchDispose()
			{
				Assert.Fail("Internal test error. Locally bound address "+BoundAddress+" does not match globally registered address "+PublicAddress);
			}

			public override void OnOutOfConfig(Configuration newConfig)
			{
				Assert.Fail("Node incorrectly fell out of config "+newConfig);
			}
		}

		private class Cluster : IDisposable
		{
			private Node[] members;
			private readonly object[] attachments;
			private Configuration cfg;

			public int LeaderIndex
			{
				get
				{
					for (int i = 0; i < members.Length; i++)
						if (members[i].IsLeader)
							return i;
					return -1;
				}
			}

			public int Size => members.Length;

			public void Dispose()
			{
				foreach (var m in members)
					m.Dispose();
			}

			private int testOffset = 1024;
			private int basePort;

			public Address GetAddressOf(int idx)
			{
				return new Address(basePort + idx - testOffset);
			}

			public Cluster(int basePort, int size) : this(basePort,size,i => true)
			{
			}

			public Cluster(int basePort, int size, Func<int, bool> leaderCandidate)
			{
				this.basePort = basePort;
				var members = new Configuration.Member[size];
				for (int i = 0; i < size; i++)
					members[i] = new Configuration.Member(testOffset + i, leaderCandidate(i));
				cfg = new Configuration(members);
				this.members = new Node[size];
				for (int i = 0; i < size; i++)
					this.members[i] = new ClusterNode(cfg, cfg.Members[i], new Address(basePort + i), GetAddressOf);
				attachments = new object[size];
			}

			public void ChangeConfiguration(int size, Func<int, bool> leaderCandidate)
			{
				var cfgMembers = new Configuration.Member[size];
				for (int i = 0; i < size; i++)
					cfgMembers[i] = new Configuration.Member(testOffset + i, leaderCandidate(i));
				var newCFG = new Configuration(cfgMembers);
				if (size < members.Length)
				{
					for (int i = 0; i < size; i++)
						members[i].ChangeConfiguration(newCFG);
					for (int i = size; i < this.members.Length; i++)
						members[i].Dispose();
					members = members.Subarray(0, size);
				}
				else
				{
					var newMembers = new Node[size];
					for (int i = 0; i < members.Length; i++)
					{
						members[i].ChangeConfiguration(newCFG);
						newMembers[i] = members[i];
					}
					for (int i = members.Length; i < size; i++)
						newMembers[i] = new ClusterNode(newCFG, newCFG.Members[i], new Address(basePort + i), GetAddressOf);
					members = newMembers;
				}
				cfg = newCFG;
			}

			public bool AwaitInterconnected()
			{
				for (int i = 0; i < 100; i++)
				{
					if (members.All(m => m.IsFullyConnected))
						return true;
					Thread.Sleep(100);
				}
				return false;
			}

			public bool ConsensusEstablished => members.Count(m => m.IsLeader) == 1 && members.Count(m => m.KnowsRemoteLeader) == members.Length - 1;


			public bool AwaitConsensus()
			{
				for (int i = 0; i < 100; i++)
				{
					if (ConsensusEstablished)
						return true;
					Thread.Sleep(100);
				}
				return false;
			}

			internal int AssertLeaderFollowerCorrectness()
			{
				int leader = -1;
				for (int i = 0; i < members.Length; i++)
					if (members[i].IsLeader)
					{
						Assert.AreEqual(-1, leader);	//only one leader
						leader = i;
						break;
					}
					else
						Assert.AreEqual(Node.State.Follower, members[i].CurrentState);	//should be a follower now
				return leader;
			}

			internal void Failover(int idx, bool preserveAttachment=true)
			{
				members[idx].Dispose();

				Thread.Sleep(100);
				for (int i = 0; i < members.Length; i++)
					if (i != idx)
					{
						Assert.IsFalse(members[i].IsDisposed);
						Assert.IsFalse(members[i].IsFullyConnected, "[" + i + "]L" + idx);
					}
				members[idx] = new ClusterNode(cfg, cfg.Members[idx],new Address(basePort+idx),GetAddressOf);
				if (preserveAttachment)
					members[idx].Attachment = attachments[idx];
				else
					attachments[idx] = null;
			}

			internal void Attach<T>() where T: new()
			{
				for (int i = 0; i < members.Length; i++)
					attachments[i] = members[i].Attachment = new T();
			}

			internal void Commit(ICommitable comm)
			{
				Schedule(random.Next(members.Length),comm);
			}

			internal Consensus.CommitID Schedule(int memberIndex, ICommitable comm)
			{
				return members[memberIndex].Schedule(comm);
			}

			internal void RemoveFossils(int memberIndex)
			{
				members[memberIndex].RemoveFossils();
			}
			internal void RemoveFossils(int memberIndex, CommitID threshold, bool includeThreshold)
			{
				members[memberIndex].RemoveFossils(threshold, includeThreshold);
			}

			internal void ForeachMember(Action<Node> action)
			{
				for (int i = 0; i < members.Length; i++)
					action(members[i]);
			}

			internal void AssertLogsAreEmpty()
			{
				foreach (var m in members)
				{
					Assert.IsTrue( m.CountStoredLogEntries<=1);	//last entry can't be removed
				}
			}

			internal void Suspend(int suspended)
			{
				Assert.IsNotNull(members[suspended]);
				members[suspended].Dispose();
				members[suspended] = null;
			}

			internal void Resume(int suspended)
			{
				Assert.IsNull(members[suspended]);
				members[suspended] = new ClusterNode(cfg, cfg.Members[suspended],new Address(basePort+suspended),GetAddressOf);
				members[suspended].Attachment = attachments[suspended];
			}
		}

		private class TestAttachment
		{
			public readonly ConcurrentQueue<int> Received = new ConcurrentQueue<int>();

			internal void AssertIsComplete(Node hub, int range)
			{
				for (int i = 0; i < range; i++)
				{
					int got;
					Assert.IsTrue(Received.TryDequeue(out got), hub + ": Dequeue " + i + "/" + range);
					Assert.AreEqual(i, got, hub + ": Element " + i + "/" + range);
				}
			}
		}

		[Serializable]
		private class TestCommitable : ICommitable
		{
			public readonly int Index;
			public TestCommitable(int index)
			{
				Index = index;
			}
			public void Commit(Node hub, CommitID commitID)
			{
				TestAttachment attach = (TestAttachment)hub.Attachment;
				attach.Received.Enqueue(Index);
			}

			public override string ToString()
			{
				return "Test<" + Index + ">";
			}
		}


		[TestMethod()]
		public void LogTest()
		{
			int basePort = new Random().Next(1024, 32768);
			Cluster c = new Cluster(basePort, 3);
			Assert.IsTrue(c.AwaitConsensus());

			for (int j = 0; j < c.Size; j++)
			{
				c.Attach<TestAttachment>();
				for (int i = 0; i < 10; i++)
				{
					c.Schedule(j, new TestCommitable(i));
				}

				Thread.Sleep(1000);
				c.ForeachMember(hub => ((TestAttachment)hub.Attachment).AssertIsComplete(hub, 10));
			}

			c.Dispose();


		}

		[TestMethod()]
		public void FossilRemovalTest()
		{
			int basePort = new Random().Next(1024, 32768);
			Cluster c = new Cluster(basePort, 3);
			Assert.IsTrue(c.AwaitConsensus());

			for (int j = 0; j < c.Size; j++)
			{
				c.Attach<TestAttachment>();
				for (int i = 0; i < 10; i++)
				{
					c.Schedule(j, new TestCommitable(i));
				}
				c.RemoveFossils(j);
				

				Thread.Sleep(1000);
				c.ForeachMember(hub => ((TestAttachment)hub.Attachment).AssertIsComplete(hub, 10));
			}

			c.AssertLogsAreEmpty();

			c.Dispose();


		}

		[TestMethod()]
		public void NodeSkipTest()
		{
			using (var c = new Cluster(new Random().Next(1024, 32768), 3))
			{
				c.Attach<TestAttachment>();
				Assert.IsTrue(c.AwaitConsensus());
				var leader = c.LeaderIndex;
				var suspended = (c.LeaderIndex + 1) % c.Size;
				c.Suspend(suspended);
				for (int i = 0; i < 10; i++)
					c.Schedule(leader, new TestCommitable(i));
				c.RemoveFossils(leader);
				Thread.Sleep(1000);
				c.ForeachMember(n => { if (n != null) ((TestAttachment)n.Attachment).AssertIsComplete(n, 10);});
				c.Resume(suspended);
				DateTime resumed = DateTime.Now;
				Assert.IsTrue(c.AwaitConsensus());
				DateTime consensus = DateTime.Now;
				for (int i = 0; i < 10; i++)
					c.Schedule(leader, new TestCommitable(i));
				DateTime preSleep = DateTime.Now;
				Thread.Sleep(1000);
				DateTime endSleep = DateTime.Now;
				c.ForeachMember(hub => ((TestAttachment)hub.Attachment).AssertIsComplete(hub, 10));

			}
		}

		[TestMethod()]
		public void NodeConfigTest()
		{
			using (var c = new Cluster(new Random().Next(1024, 32768), 3,i => i <= 1))
			{
				Assert.IsTrue(c.AwaitConsensus());
				c.ChangeConfiguration(4,i => i <= 1);
				Assert.IsTrue(c.AwaitConsensus());
				c.ChangeConfiguration(5, i => i <= 1);
				Assert.IsTrue(c.AwaitConsensus());
				c.ChangeConfiguration(3, i => i <= 1);
				Assert.IsTrue(c.AwaitConsensus());

			}
		}


		[TestMethod()]
		public void NodeTest()
		{
			using (var c = new Cluster(new Random().Next(1024, 32768), 3))
			{
				for (int j = 0; j < 3; j++)
				{
					Assert.IsTrue(c.AwaitInterconnected());
					Assert.IsTrue(c.AwaitConsensus());
					int leader = c.AssertLeaderFollowerCorrectness();
					var ad = c.GetAddressOf(leader);
					Console.WriteLine("Closing leader " + leader);
					c.Failover(leader);
				}
			}
		}

	}
}