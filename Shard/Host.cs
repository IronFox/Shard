using System.Diagnostics;

namespace Shard
{
	public struct Host
	{
		public readonly string URL;
		public readonly int Port;

		//public readonly static int DefaultPort = 16235;
		public static int DefaultPort { get; set; } = 16235;
		public static string Domain { get; set; } = null;
		public static bool HaveDomain { get { return Domain != null && Domain.Length > 0; } }

		public static readonly string Prefix = "shard-";

		public Host(ShardID id)
		{
			URL = Prefix+id.Encode();
			if (HaveDomain)
				URL += "." + Domain;
			Port = DefaultPort;
		}

		public ShardID ID
		{
			get
			{
				if (!URL.StartsWith(Prefix))
					return new ShardID();
				if (HaveDomain)
					Debug.Assert(URL.EndsWith(Domain));
				string sid = URL.Substring(Prefix.Length);
				int dotAt = sid.LastIndexOf('.');
				if (dotAt >= 0)
					sid = sid.Substring(0, dotAt);
				return ShardID.Decode(sid);
			}
		}

		public Host(string url, int port)
		{
			URL = url;
			Port = port;
		}
		public Host(string urlHost)
		{
			int at = urlHost.LastIndexOf(':');
			if (at >= 0)
			{
				URL = urlHost.Substring(0, at);
				Port = int.Parse(urlHost.Substring(at + 1));
			}
			else
			{
				URL = urlHost;
				Port = DefaultPort;
			}
		}

		public override string ToString()
		{
			//if (Port != DefaultPort)
			return URL + ":" + Port;
			//return URL;
		}
	}
}