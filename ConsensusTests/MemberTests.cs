using Microsoft.VisualStudio.TestTools.UnitTesting;
using Consensus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Consensus.Tests
{
	[TestClass()]
	public class MemberTests
	{
		[TestMethod()]
		public void AddressTest()
		{
			Assert.AreEqual(new Address("localhost", 1024), new Address(1024));
			Assert.AreEqual(new Address("127.0.0.1", 1024), new Address(1024));
			Assert.AreEqual(new Address("::1", 1024), new Address(1024));
		}

		[TestMethod()]
		public void GetLogTermTest()
		{
			Assert.Fail();
		}

		[TestMethod()]
		public void MemberTest()
		{
			Configuration cfg = new Configuration(new Address[] { new Address(1024), new Address(1025), new Address(1026) });

			Member[] members = new Member[]{
				new Member(cfg.Addresses[0], cfg),
				new Member(cfg.Addresses[1], cfg),
				new Member(cfg.Addresses[2], cfg) };

			for (int j = 0; j < 3; j++)
			{
				for (int i = 0; i < 100; i++)
				{
					if (members.All(m => m.IsFullyConnected))
						break;
					Thread.Sleep(100);
				}
				Assert.IsTrue(members.All(m => m.IsFullyConnected),j.ToString());
				Thread.Sleep(1000);


				Assert.IsTrue(members.Any(m => m.IsLeader));

				int leader = -1;
				for (int i = 0; i < members.Length; i++)
					if (members[i].IsLeader)
					{
						leader = i;
						break;
					}
					else
						Assert.AreEqual(members[i].CurrentState, Member.State.Follower);

				var ad = members[leader].Address;
				members[leader].Dispose();

				for (int i = 0; i < members.Length; i++)
					if (i != leader)
					{
						Assert.IsFalse(members[i].IsFullyConnected);
					}

				members[leader] = new Member(ad, cfg);
			}
			foreach (var m in members)
				m.Dispose();
		}

		[TestMethod()]
		public void ConnectedToTest()
		{
			Assert.Fail();
		}

		[TestMethod()]
		public void JoinTest()
		{
			Assert.Fail();
		}

		[TestMethod()]
		public void DisposeTest()
		{
			Assert.Fail();
		}

		[TestMethod()]
		public void CommitTest()
		{
			Assert.Fail();
		}
	}
}