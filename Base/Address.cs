using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Base
{

	public struct AddressWrapper : IComparable<AddressWrapper>, IEquatable<AddressWrapper>
	{
		public readonly IPAddress IPAddress;
		public AddressWrapper(IPAddress addr)
		{
			IPAddress = addr;
		}

		public int CompareTo(AddressWrapper other)
		{
			return IPAddress.ToString().CompareTo(other.IPAddress.ToString());
		}

		public override bool Equals(object obj)
		{
			return obj is AddressWrapper && Equals((AddressWrapper)obj);
		}

		public bool Equals(AddressWrapper other)
		{
			return EqualityComparer<IPAddress>.Default.Equals(IPAddress, other.IPAddress);
		}

		public override int GetHashCode()
		{
			return -2138420020 + EqualityComparer<IPAddress>.Default.GetHashCode(IPAddress);
		}

		public override string ToString()
		{
			return IPAddress.ToString();
		}
	}

	[Serializable]
	public struct Address : IComparable<Address>, IEquatable<Address>
	{
		public readonly string Host;
		public readonly int Port;

		public Address(string hostname, int port)
		{
			Host = hostname;
			Port = port;
		}

		public Address(string hostPort)
		{
			int colonAt = hostPort.IndexOf(':');
			if (colonAt < 0)
				throw new ArgumentException("':' expected in hostPort '" + hostPort + "'");
			Host = hostPort.Substring(0, colonAt);
			Port = int.Parse(hostPort.Substring(colonAt + 1));
			if (Port <= 0 || Port >= 65536)
				throw new ArgumentOutOfRangeException("hostPort", "Expected valid port value");
		}

		public Address(int port)
		{
			Host = "localhost";
			Port = port;
		}

		public Address(IPEndPoint remoteEndPoint) : this()
		{
			Host = remoteEndPoint.Address.ToString();
			Port = remoteEndPoint.Port;
		}

		public Address(Address hostPortion, int port)
		{
			Host = hostPortion.Host;
			Port = port;
		}

		public AddressWrapper[] Resolved
		{
			get
			{
				return Dns.GetHostAddresses(Host).Select(addr => new AddressWrapper(addr)).ToArray();
			}
		}

		public override string ToString()
		{
			if (IsEmpty)
				return "<empty>";
			return Host + ":" + Port;
		}


		public static bool operator >(Address a, Address b)
		{
			return a.CompareTo(b) > 0;
		}
		public static bool operator <(Address a, Address b)
		{
			return a.CompareTo(b) < 0;
		}


		public int CompareTo(Address other)
		{
			int rs = Port.CompareTo(other.Port);
			if (rs != 0)
				return rs;
			rs = IsSet.CompareTo(other.IsSet);
			if (rs != 0 || IsEmpty)
				return rs;
			var local = Resolved;
			var remote = other.Resolved;
			foreach (var l in local)
			{
				if (remote.Contains(l))
					return 0;
			}
			return local.Min().CompareTo(remote.Min());
		}

		public static bool operator ==(Address a, Address b)
		{
			return a.CompareTo(b)==0;
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
			return this == other;
		}

		public bool IsEmpty => string.IsNullOrWhiteSpace(Host);
		public bool IsSet => !IsEmpty;

		public override int GetHashCode()
		{
			var hashCode = -356659493;
			hashCode = hashCode * -1521134295 + Port.GetHashCode();
			hashCode = hashCode * -1521134295 + EqualityComparer<AddressWrapper[]>.Default.GetHashCode(Resolved);
			return hashCode;
		}
	}
}
