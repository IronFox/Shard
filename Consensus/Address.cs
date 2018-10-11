using System;
using System.Collections.Generic;
using System.Net;

namespace Consensus
{
	[Serializable]
	public struct Address : IComparable<Address>, IEquatable<Address>
	{
		public readonly string HostName;
		public readonly int Port;

		public Address(string hostname, int port)
		{
			HostName = hostname;
			Port = port;
		}

		public Address(IPEndPoint remoteEndPoint) : this()
		{
			HostName = remoteEndPoint.Address.ToString();
			Port = remoteEndPoint.Port;
		}

		public int CompareTo(Address other)
		{
			int rs = HostName.CompareTo(other.HostName);
			if (rs == 0)
				rs = Port.CompareTo(other.Port);
			return rs;
		}

		public static bool operator ==(Address a, Address b)
		{
			return a.HostName == b.HostName && a.Port == b.Port;
		}
		public static bool operator !=(Address a, Address b)
		{
			return !(a == b);
		}

		public override bool Equals(object obj)
		{
			return obj is Address && Equals((Address)obj);
		}

		public bool Equals(Address other)
		{
			return HostName == other.HostName &&
				   Port == other.Port;
		}

		public override int GetHashCode()
		{
			var hashCode = -1741703244;
			hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(HostName);
			hashCode = hashCode * -1521134295 + Port.GetHashCode();
			return hashCode;
		}
	}
}
