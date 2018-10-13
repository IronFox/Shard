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
		public void MemberTest()
		{
			int basePort = new Random().Next(1024, 32768);
			Configuration cfg = new Configuration(new Address[] { new Address(basePort), new Address(basePort+1), new Address(basePort+2) });

			Hub[] members = new Hub[]{
				new Hub(cfg,0),
				new Hub(cfg,1),
				new Hub(cfg,2) };

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
						Assert.AreEqual(members[i].CurrentState, Hub.State.Follower);

				Console.WriteLine("Disposing leader");

				var ad = members[leader].Address;
				members[leader].Close();
				Thread.Sleep(100);
				for (int i = 0; i < members.Length; i++)
					if (i != leader)
					{
						Assert.IsFalse(members[i].IsDisposed);
						if (members[i].IsFullyConnected)
						{
							bool grk = true;
						}
						Assert.IsFalse(members[i].IsFullyConnected,j+"["+i+"]L"+leader);
					}

				members[leader] = new Hub(cfg,leader);
			}
			foreach (var m in members)
				m.Close();
		}

	}
}