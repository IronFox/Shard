using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	/// <summary>
	/// Data digest to be used for comparison of long data.
	/// On short data, the digest may equal the actual data
	/// </summary>
	[Serializable]
	public struct Digest
	{
		public readonly byte[] Bytes;
		public readonly bool IsHashed;

		public Digest(byte[] digestData, bool isHashed) : this()
		{
			Bytes = digestData;
			IsHashed = isHashed;
		}

		public static Digest FromBinaryData(byte[] data)
		{
			if (Helper.Length(data) < 256 / 8)
				return new Digest(data, false);
			return new Digest(SHA256.Create().ComputeHash(data),true);
		}

		public override bool Equals(object obj)
		{
			if (!(obj is Digest))
			{
				return false;
			}

			var digest = (Digest)obj;
			return this == digest;
		}

		public override int GetHashCode()
		{
			return new Helper.HashCombiner(GetType())
				.Add(IsHashed)
				.Add(Bytes)
				.GetHashCode();
		}

		public static bool operator ==(Digest a, Digest b)
		{
			return a.IsHashed == b.IsHashed 
				&& Helper.AreEqual(a.Bytes, b.Bytes);
		}
		public static bool operator !=(Digest a, Digest b)
		{
			return !(a == b);
		}

	}
}
