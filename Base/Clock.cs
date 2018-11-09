using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Base
{
	public static class Clock
	{
		//https://stackoverflow.com/questions/1193955/how-to-query-an-ntp-server-using-c Nasreddine
		public static DateTime GetNetworkTime(IPEndPoint ipEndPoint)
		{
			// NTP message size - 16 bytes of the digest (RFC 2030)
			var ntpData = new byte[48];

			//Setting the Leap Indicator, Version Number and Mode values
			ntpData[0] = 0x1B; //LI = 0 (no warning), VN = 3 (IPv4 only), Mode = 3 (Client Mode)

			//NTP uses UDP
			using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
			{
				socket.Connect(ipEndPoint);

				//Stops code hang if NTP is blocked
				socket.ReceiveTimeout = 3000;

				socket.Send(ntpData);
				socket.Receive(ntpData);
				socket.Close();
			}

			//Offset to get to the "Transmit Timestamp" field (time at which the reply 
			//departed the server for the client, in 64-bit timestamp format."
			const byte serverReplyTime = 40;

			//Get the seconds part
			ulong intPart = BitConverter.ToUInt32(ntpData, serverReplyTime);

			//Get the seconds fraction
			ulong fractPart = BitConverter.ToUInt32(ntpData, serverReplyTime + 4);

			//Convert From big-endian to little-endian
			intPart = SwapEndianness(intPart);
			fractPart = SwapEndianness(fractPart);

			var milliseconds = (intPart * 1000) + ((fractPart * 1000) / 0x100000000L);

			//**UTC** time
			var networkDateTime = (new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)).AddMilliseconds((long)milliseconds);

			return networkDateTime.ToLocalTime();
		}

		public static IPEndPoint ResolveNTPHost(string host)
		{
			IPAddress ip = null;
			if (IPAddress.TryParse(host, out ip))
			{
				if (ip.AddressFamily != AddressFamily.InterNetwork)
					throw new ArgumentException("Given IP address '" + host + "' is " + ip.AddressFamily + ". Expected IPv4");
			}
			else
			{
				var addresses = Dns.GetHostEntry(host).AddressList;
				foreach (var addr in addresses)
					if (addr.AddressFamily == AddressFamily.InterNetwork)
					{
						ip = addr;
						break;
					}
			}
			if (ip == null)
				throw new ArgumentException("Unable to resolve NTP host '" + host+"'");
			return new IPEndPoint(ip, NTPPort);
		}

		// stackoverflow.com/a/3294698/162671
		static uint SwapEndianness(ulong x)
		{
			return (uint)(((x & 0x000000ff) << 24) +
						   ((x & 0x0000ff00) << 8) +
						   ((x & 0x00ff0000) >> 8) +
						   ((x & 0xff000000) >> 24));
		}


		private static Thread ntpThread;
		private static DateTime ntpTime = DateTime.Now;
		private static long ntpQueryStamp = GetTimestamp();
		private static SpinLock timeLock = new SpinLock();
		private static IPEndPoint ntpHost;


		public const int NTPPort = 123;//The UDP port number assigned to NTP is 123

		private static int numQueries = 0;
		public static int NumQueries { get { return numQueries; } }


		/// <summary>
		/// Gets or sets the url used for NTP queries.
		/// First call starts the query thread that updates the local time once every 2 minutes (starting now)
		/// </summary>
		public static string NTPHost
		{
			get { return ntpHost.ToString(); }
			set
			{
				ntpHost = ResolveNTPHost(value);
				numQueries = 0;
				if (ntpThread == null || !ntpThread.IsAlive)
				{
					ntpThread = new Thread(new ThreadStart(NTPThread));
					ntpThread.Start();
				}
			}

		}


		private static long GetTimestamp()
		{
			return Stopwatch.GetTimestamp();
			//return watch.ElapsedTicks;
		}

		private static void NTPThread()
		{
			try
			{
				while (true)
				{
					DateTime newNTPTime = GetNetworkTime(ntpHost);
					long ntpStamp = GetTimestamp();
					timeLock.DoLocked(() =>
					{
						ntpTime = newNTPTime;
						ntpQueryStamp = ntpStamp;
					});
					Interlocked.Increment(ref numQueries);
					Thread.Sleep(TimeSpan.FromMinutes(2));
				}
			}
			catch (Exception ex)
			{
				Log.Error("NTP query thread: "+ex);
			}
		}


		public struct Sample
		{
			public readonly DateTime Now;
			public readonly TimeSpan NTPReplyAge;
			public readonly int	NTPReplyNumber;

			public Sample(DateTime now, TimeSpan ntpAge)
			{
				Now = now;
				NTPReplyAge = ntpAge;
				NTPReplyNumber = numQueries;
			}
		}

		private static TimeSpan ConvertSWTicks(long ticks)
		{
			//var rs0 = TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
			//var rs = TimeSpan.FromMilliseconds((double)ticks / Stopwatch.Frequency * 1000.0);
			var rs = TimeSpan.FromTicks(ticks * 10000000 / Stopwatch.Frequency);

			return rs;
		}

		public static Sample GetSample()
		{
			DateTime now = new DateTime();
			TimeSpan age = new TimeSpan();
			timeLock.DoLocked(() =>
			{
				var a = ConvertSWTicks(GetTimestamp() - ntpQueryStamp);
				now = ntpTime + a;
				age = a;
			});
			return new Sample(now, age);
		}

		private static DateTime CalculateTime()
		{
			return ntpTime + ConvertSWTicks(GetTimestamp() - ntpQueryStamp);
		}


		public static Func<DateTime> TimeOverrideFunction { get; set; }

		/// <summary>
		/// Queries the current time stamp using local and/or server-queried time data, as available
		/// </summary>
		public static DateTime Now
		{
			get
			{
				if (TimeOverrideFunction != null)
					return TimeOverrideFunction();
				DateTime rs = new DateTime();
				timeLock.DoLocked(() =>
				{
					rs = CalculateTime();
				});
				return rs;
			}
		}


		public static void Sleep(TimeSpan timeSpan)
		{
			Thread.Sleep(timeSpan);
		}

		public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(1);

		public static void SleepUntil(DateTime deadline, TimeSpan maxSleep)
		{
			DateTime t1 = Now + maxSleep;
			SleepUntil(Helper.Min(deadline, t1));
		}
		public static void SleepUntil(DateTime deadline)
		{

			var delta = deadline - Now;
			if (delta <= TimeSpan.Zero)
				return;
			if (delta > MaxWait)
			{
				Sleep(MaxWait);
				return;
			}

			while (delta > TimeSpan.FromMilliseconds(2))
			{
				var sleep = delta-TimeSpan.FromMilliseconds(2);	//2 ms tolerance _should_ be fine
				Sleep(sleep);
				delta = deadline - Now;
			}
			while (Now < deadline)	//busy wait
			{ };
		}
	}
}
