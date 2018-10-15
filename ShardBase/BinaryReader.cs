using Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	public class BinaryReader
	{
		private Stream stream;
		public int RemainingBytes { get; set; } = int.MaxValue;
		private byte[] buffer = new byte[16];

		public BinaryReader(Stream stream)
		{
			this.stream = stream;
		}


		public void ReadBytes(byte[] target, int offset, int count)
		{
			if (RemainingBytes < count)
				throw new SerializationException("Not enough bytes left in packet to deserialize " + count + " byte(s)");
			while (count > 0)
			{
				int read = stream.Read(target, offset,count);
				if (read <= 0)
					throw new SerializationException("Unexpected EOF reached");
				offset += read;
				count -= read;
				RemainingBytes -= read;
			}
		}

		public byte[] ReadBytes(int numBytes)
		{
			byte[] rs = new byte[numBytes];
			ReadBytes(rs, 0, numBytes);
			return rs;
		}

		private static byte[] skipBuffer = new byte[1024];
		public void Skip(int bytes)
		{
			if (RemainingBytes < bytes)
				throw new SerializationException("Not enough bytes left in packet to deserialize " + bytes + " byte(s)");
			while (bytes > 0)
			{
				int read = stream.Read(skipBuffer, 0, Math.Min(bytes, skipBuffer.Length ));
				if (read <= 0)
					throw new SerializationException("Unexpected EOF reached");
				bytes -= read;
				RemainingBytes -= read;
			}
		}

		private void FillBuffer(int numBytes)
		{
			ReadBytes(buffer, 0, numBytes);
		}
		public Guid NextGuid()
		{
			FillBuffer(16);
			return new Guid(buffer);
		}

		public int NextInt()
		{
			FillBuffer(4);
			return BitConverter.ToInt32(buffer, 0);
		}
		public float NextFloat()
		{
			FillBuffer(4);
			return BitConverter.ToSingle(buffer, 0);
		}

		public Vec3 NextVec3()
		{
			return new Vec3(NextFloat(), NextFloat(), NextFloat());
		}

		public ShardID NextShardID()
		{
			return new ShardID(NextInt(),NextInt(),NextInt(),NextInt());
		}

		public byte[] NextBytes()
		{
			int numBytes = NextInt();
			return ReadBytes(numBytes);
		}

		public uint NextUInt()
		{
			FillBuffer(4);
			return BitConverter.ToUInt32(buffer, 0);
		}

		public void SkipRemaining()
		{
			Skip(RemainingBytes);
		}
	}
}
