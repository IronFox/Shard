using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	public interface IHashable
	{
		void Hash(Hasher h);
	}


	public class Hasher : SHA1Managed
	{
		public new struct Hash
		{
			// Copyright (c) 2008-2013 Hafthor Stefansson
			// Distributed under the MIT/X11 software license
			// Ref: http://www.opensource.org/licenses/mit-license.php


			private byte[] hashBytes;

			public Hash(byte[] v)
			{
				this.hashBytes = v;
			}

			public static bool operator ==(Hash a, Hash b) => Helper.AreEqual(a.hashBytes, b.hashBytes);
			public static bool operator !=(Hash a, Hash b) => !Helper.AreEqual(a.hashBytes, b.hashBytes);

			public override string ToString()
			{
				StringBuilder rs = new StringBuilder(hashBytes.Length * 2);
				foreach (byte bi in hashBytes)
					rs.AppendFormat("{0:x2}", bi);
				return rs.ToString();
			}

			public override bool Equals(object obj)
			{
				if (!(obj is Hash))
				{
					return false;
				}

				var hash = (Hash)obj;
				return this == hash;
			}

			public override int GetHashCode()
			{
				return hashBytes.GetHashCode();
			}
		}

		public void Add(Type type)
		{
			buffer.Add(type.AssemblyQualifiedName);
			Check();
		}

		ByteBuffer buffer = new ByteBuffer();

		private void Check()
		{
			if (buffer.Length > 128)
				Flush();
		}

		private void Flush()
		{
			base.HashCore(buffer.GetInternalArray(), 0, (int)buffer.Length);
			buffer.Reset();
		}

		public void Add(byte[] binAr)
		{
			buffer.Add(binAr);
			Check();
		}

		public void Add(IHashable h)
		{
			if (h == null)
				return;
			h.Hash(this);
			Check();
		}

		public void Add(Vec3 v)
		{
			buffer.Add(v);
			Check();
		}

		public void Add(bool b)
		{
			buffer.Add(b);
			Check();
		}

		public void Add(int i)
		{
			buffer.Add(i);
			Check();
		}

		public Hash Finish()
		{
			Flush();
			return new Hash(base.HashFinal());
		}

		public void Add(Guid guid)
		{
			buffer.Add(guid);
			Check();
		}
		public void Add(string str)
		{
			buffer.Add(str);
			Check();
		}

		public void Add(Int3 i3)
		{
			buffer.Add(i3);
			Check();
		}

		public void Add(uint[,,] grid)
		{
			buffer.Add(grid);
			Check();
		}
	}

}
