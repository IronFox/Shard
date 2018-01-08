using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shard
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


		static Thread ntpThread;
		static DateTime ntpTime = DateTime.Now;
		static long ntpQueryStamp = Stopwatch.GetTimestamp();
		static SpinLock timeLock = new SpinLock();
		static IPEndPoint ntpHost;


		public const int NTPPort = 123;//The UDP port number assigned to NTP is 123


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
				if (ntpThread == null || !ntpThread.IsAlive)
				{
					ntpThread = new Thread(new ThreadStart(NTPThread));
					ntpThread.Start();
				}
			}

		}

		static void NTPThread()
		{
			try
			{
				while (true)
				{
					DateTime newNTPTime = GetNetworkTime(ntpHost);
					long ntpStamp = Stopwatch.GetTimestamp();
					timeLock.DoLocked(() =>
					{
						ntpTime = newNTPTime;
						ntpQueryStamp = ntpStamp;
					});
					Thread.Sleep(TimeSpan.FromMinutes(2));
				}
			}
			catch (Exception ex)
			{
				Log.Error("NTP query thread: "+ex);
			}
		}



		public static DateTime Now
		{
			get
			{
				DateTime rs = new DateTime();
				timeLock.DoLocked(() =>
				{
					rs = ntpTime + TimeSpan.FromSeconds((double)(Stopwatch.GetTimestamp() - ntpQueryStamp) / Stopwatch.Frequency);
				});
				return rs;
			}
		}


		public static void Sleep(TimeSpan timeSpan)
		{
			Thread.Sleep(timeSpan);
		}

		public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(1);

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
