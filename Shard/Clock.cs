using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
			return new TimeSpan(TimeSpan.TicksPerMillisecond * milliseconds);
		}

		internal static void Sleep(TimeSpan timeSpan)
		{
			throw new NotImplementedException();
		}
	}
}
