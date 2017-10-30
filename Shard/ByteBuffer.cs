using System;
using VectorMath;

namespace Shard
{
	public class ByteBuffer
	{
		byte[] data = null;
		int offset = 0;

		private void Require(int bytes)
		{
			if (bytes == 0)
				return;
			if (data == null)
			{
				data = new byte[bytes];
				return;
			}
			if (data.Length - offset < bytes)
			{
				int newLen = data.Length * 2;
				while (newLen - offset < bytes)
					newLen *= 2;

				byte[] newField = new byte[newLen];
				Buffer.BlockCopy(data, 0, newField, 0, offset);
				data = newField;
			}
		}



		public void Add(uint[,,] grid)
		{
			int len = grid.Length * 4;
			Require(len);
			Buffer.BlockCopy(grid, 0, data, offset, len);
			offset += len;
		}

		public void Add(Int3 size)
		{
			Add(size.X);
			Add(size.Y);
			Add(size.Z);
		}

		public static void Put(byte[] array, int offset, int v)
		{
			array[offset++] = (byte)(v & 0xFF);
			array[offset++] = (byte)((v >> 8) & 0xFF);
			array[offset++] = (byte)((v >> 16) & 0xFF);
			array[offset++] = (byte)((v >> 24) & 0xFF);
		}
		public static void Put(byte[] array, int offset, uint v)
		{
			array[offset++] = (byte)(v & 0xFF);
			array[offset++] = (byte)((v >> 8) & 0xFF);
			array[offset++] = (byte)((v >> 16) & 0xFF);
			array[offset++] = (byte)((v >> 24) & 0xFF);
		}

		public void Add(int i)
		{
			Require(4);
			Put(data, offset,i);
			offset += 4;
		}
		public void Add(uint u)
		{
			Require(4);
			Put(data, offset, u);
			offset += 4;
		}

		public byte[] ToArray()
		{
			if (offset == data.Length)
				return data;
			byte[] rs = new byte[offset];
			Buffer.BlockCopy(data, 0, rs, 0, offset);
			return rs;
		}
	}
}