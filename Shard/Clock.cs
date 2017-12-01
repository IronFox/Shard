using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shard
{
	public static class Clock
	{
		public static DateTime Now
		{
			get
			{
				return DateTime.Now;	//for now
			}
		}

		public static TimeSpan Milliseconds(int milliseconds)
		{
			return TimeSpan.FromMilliseconds(milliseconds);
		}

		internal static void Sleep(TimeSpan timeSpan)
		{
			Thread.Sleep(timeSpan);
		}
	}
}
