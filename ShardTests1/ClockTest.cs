using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShardTests1
{
	[TestClass]
	public class ClockTest
	{
		[TestMethod]
		public void ConnectionTest()
		{
			var endPoint = Clock.ResolveNTPHost("uhr.uni-trier.de");

			var time = Clock.GetNetworkTime(endPoint);
			var delta = time - DateTime.Now;
			Assert.IsTrue(delta.Duration() < TimeSpan.FromHours(1));
			Console.WriteLine(DateTime.Now + " NTP: " + time);
		}

		[TestMethod]
		public void ContinuousClockTest()
		{
			Clock.NTPHost = "uhr.uni-trier.de";

			//total one seconds
			for (int i = 0; i < 10; i++)
			{
				var s = Clock.GetSample();
				Thread.Sleep(100);
				Console.WriteLine(s.Now.Ticks+" age "+s.NTPReplyAge.TotalMilliseconds+" ntp #"+s.NTPReplyNumber);
			}

			Assert.AreEqual(1, Clock.NumQueries);   //1 sec. should be enough to query the time server once, but not enough to sample twice
		}
	}
}

