using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Shard
{
	/// <summary>
	/// Internet host address
	/// </summary>
	[Serializable]
	public struct PeerAddress
	{
		public readonly string Address;
		public readonly int Port;

		public static int DefaultPort { get; set; } = 16235;
		public bool IsEmpty { get { return string.IsNullOrEmpty(Address); } }

		public static bool operator ==(PeerAddress a, PeerAddress b)
		{
			return a.Address == b.Address && a.Port == b.Port;
		}
		public static bool operator !=(PeerAddress a, PeerAddress b)
		{
			return !(a==b);
		}

		public PeerAddress(string address, int port)
		{
			Address = address;
			Port = port;
		}
		public PeerAddress(string domainNameOptionalPort)
		{
			int at = domainNameOptionalPort.LastIndexOf(':');
			if (at >= 0)
			{
				Address = domainNameOptionalPort.Substring(0, at);
				Port = int.Parse(domainNameOptionalPort.Substring(at + 1));
			}
			else
			{
				Address = domainNameOptionalPort;
				Port = DefaultPort;
			}
		}

		public override string ToString()
		{
			return Address + ":" + Port;
		}

		public override bool Equals(object obj)
		{
			if (!(obj is PeerAddress))
			{
				return false;
			}

			var address = (PeerAddress)obj;
			return Address == address.Address &&
				   Port == address.Port;
		}

		public override int GetHashCode()
		{
			var hashCode = 1820422833;
			hashCode = hashCode * -1521134295 + Address.GetHashCode();
			hashCode = hashCode * -1521134295 + Port.GetHashCode();
			return hashCode;
		}
	}



	[Serializable]
	public struct ShardPeerAddress
	{
		public readonly ShardID ShardID;
		public readonly PeerAddress Address;

		public ShardPeerAddress(ShardID id, PeerAddress addr)
		{
			ShardID = id;
			Address = addr;
		}

		public override string ToString()
		{
			return ShardID + "->" + Address;
		}
	}
}