using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Consensus
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
		public readonly string HostName;
		public readonly int Port;

		public Address(string hostname, int port)
		{
			HostName = hostname;
			Port = port;
		}

		public Address(int port)
		{
			HostName = "localhost";
			Port = port;
		}

		public Address(IPEndPoint remoteEndPoint) : this()
		{
			HostName = remoteEndPoint.Address.ToString();
			Port = remoteEndPoint.Port;
		}

		public AddressWrapper[] Resolved
		{
			get
			{
				return Dns.GetHostAddresses(HostName).Select(addr => new AddressWrapper(addr)).ToArray();
			}
		}

		public override string ToString()
		{
			return HostName + ":" + Port;
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

		public override int GetHashCode()
		{
			var hashCode = -356659493;
			hashCode = hashCode * -1521134295 + Port.GetHashCode();
			hashCode = hashCode * -1521134295 + EqualityComparer<AddressWrapper[]>.Default.GetHashCode(Resolved);
			return hashCode;
		}
	}
}
