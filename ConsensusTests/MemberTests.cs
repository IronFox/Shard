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


		private class Cluster : IDisposable
		{
			private readonly Member[] members;
			private readonly Configuration cfg;

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

			public Cluster(int basePort, int size)
			{
				Address[] addresses = new Address[size];
				for (int i = 0; i < size; i++)
					addresses[i] = new Address(basePort + i);
				cfg = new Configuration(addresses);
				members = new Member[size];
				for (int i = 0; i < size; i++)
					members[i] = new Member(cfg, i);
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

			public bool AwaitConsensus()
			{
				for (int i = 0; i < 100; i++)
				{
					if (members.Any(m => m.IsLeader))
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
						Assert.AreEqual(Member.State.Follower, members[i].CurrentState);	//should be a follower now
				return leader;
			}

			internal Func<Address> GetAddressOf(int idx)
			{
				return members[idx].Address;
			}

			internal void Failover(int idx)
			{
				members[idx].Dispose();

				Thread.Sleep(100);
				for (int i = 0; i < members.Length; i++)
					if (i != idx)
					{
						Assert.IsFalse(members[i].IsDisposed);
						Assert.IsFalse(members[i].IsFullyConnected, "[" + i + "]L" + idx);
					}
				members[idx] = new Member(cfg, idx);
			}

			internal void Attach<T>() where T: new()
			{
				foreach (var m in members)
					m.Attachment = new T();
			}

			internal void Commit(ICommitable comm)
			{
				Commit(random.Next(members.Length),comm);
			}

			internal void Commit(int memberIndex, ICommitable comm)
			{
				members[memberIndex].Commit(comm);
			}

			internal void ForeachMember(Action<Member> action)
			{
				for (int i = 0; i < members.Length; i++)
					action(members[i]);
			}
		}

		private class TestAttachment
		{
			public readonly ConcurrentQueue<int> Received = new ConcurrentQueue<int>();

			internal void AssertIsComplete(Member hub, int range)
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
			public void Commit(Member hub)
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
					c.Commit(j, new TestCommitable(i));
				}

				Thread.Sleep(1000);
				c.ForeachMember(hub => ((TestAttachment)hub.Attachment).AssertIsComplete(hub, 10));
			}

			c.Dispose();


		}

		[TestMethod()]
		public void HubTest()
		{
			int basePort = new Random().Next(1024, 32768);
			Cluster c = new Cluster(basePort, 3);

			for (int j = 0; j < 3; j++)
			{
				Assert.IsTrue(c.AwaitInterconnected());
				Assert.IsTrue(c.AwaitConsensus());
				int leader = c.AssertLeaderFollowerCorrectness();
				var ad = c.GetAddressOf(leader);
				Console.WriteLine("Closing leader "+leader);
				c.Failover(leader);
			}
			c.Dispose();
		}

	}
}