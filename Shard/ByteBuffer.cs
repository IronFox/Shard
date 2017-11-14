using System;
using System.IO;
using VectorMath;

namespace Shard
{
	public class ByteBuffer : Stream
	{
		public class Reader : Stream
		{
			int readCursor = 0;
			readonly ByteBuffer buffer;

			public Reader(ByteBuffer buffer)
			{
				this.buffer = buffer;
			}

			public override bool CanRead => true;

			public override bool CanSeek => true;

			public override bool CanWrite => false;

			public override long Length => buffer.Length;

			public override long Position { get => readCursor; set => readCursor = Math.Max(0, (int)Math.Min(Length-1,value)); }

			public override void Flush()
			{
				throw new NotSupportedException();
			}

			public override int Read(byte[] data, int offset, int count)
			{
				int copy = Math.Min(count, (int)buffer.Length - readCursor);
				buffer.Export(readCursor, data, offset, copy);
				readCursor += copy;
				return copy;
			}

			private void GoTo(long position)
			{
				if (position < 0)
					throw new IndexOutOfRangeException(position + "/" + Length);
				if (position >= Length)
					throw new IndexOutOfRangeException(position + "/" + Length);
				readCursor = (int)position;
			}


			public override long Seek(long offset, SeekOrigin origin)
			{
				switch (origin)
				{
					case SeekOrigin.Begin:
						GoTo(offset);
						break;
					case SeekOrigin.Current:
						GoTo(readCursor + offset);
						break;
					case SeekOrigin.End:
						GoTo(buffer.Length + offset);
						break;
				}
				throw new Exception("Unexpected origin "+origin);
			}


			public override void SetLength(long value)
			{
				throw new NotSupportedException();
			}

			public override void Write(byte[] buffer, int offset, int count)
			{
				throw new NotSupportedException();
			}
		}


		public void Add(string str)
		{
			if (str == null)
				return;	//like empty string
			Add(System.Text.Encoding.UTF8.GetBytes(str));
		}

		public void Add(Guid guid)
		{
			Add(guid.ToByteArray());
		}

		byte[] data = null;
		int dataWritten = 0;

		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override bool CanWrite => true;

		public override long Length => dataWritten;

		public override long Position { get => dataWritten; set => throw new NotSupportedException(); }

		private void Require(int bytes)
		{
			if (bytes == 0)
				return;
			if (data == null)
			{
				data = new byte[bytes];
				return;
			}
			if (data.Length - dataWritten < bytes)
			{
				int newLen = data.Length * 2;
				while (newLen - dataWritten < bytes)
					newLen *= 2;

				byte[] newField = new byte[newLen];
				Buffer.BlockCopy(data, 0, newField, 0, dataWritten);
				data = newField;
			}
		}

		public void Reset()
		{
			dataWritten = 0;
		}

		public void Add(uint[,,] grid)
		{
			int len = grid.Length * 4;
			Require(len);
			Buffer.BlockCopy(grid, 0, data, dataWritten, len);
			dataWritten += len;
		}

		public void Add(Int3 size)
		{
			Add(size.X);
			Add(size.Y);
			Add(size.Z);
		}

		public void Add(Vec3 position)
		{
			Add(position.X);
			Add(position.Y);
			Add(position.Z);
		}

		public void Add(float f)
		{
			Add(Helper.FloatToInt(f));
		}

		public void Add(byte[] v)
		{
			if (v == null)
				return;
			Require(v.Length);
			Buffer.BlockCopy(v, 0, data, 0, v.Length);
			dataWritten += v.Length;
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
			Put(data, dataWritten,i);
			dataWritten += 4;
		}

		internal byte[] GetInternalArray()
		{
			return data;
		}

		public void Add(uint u)
		{
			Require(4);
			Put(data, dataWritten, u);
			dataWritten += 4;
		}

		public byte[] ToArray()
		{
			if (dataWritten == data.Length)
				return data;
			byte[] rs = new byte[dataWritten];
			Buffer.BlockCopy(data, 0, rs, 0, dataWritten);
			return rs;
		}

		internal void Add(bool b)
		{
			Require(1);
			data[dataWritten++] = (byte)(b ? 1 : 0);
		}

		public void Export(int srcOffset, byte[] dstArray, int dstOffset, int byteCount)
		{
			if (srcOffset + byteCount > dataWritten)
				throw new ArgumentOutOfRangeException("srcOffset+byteCount",srcOffset + byteCount,"Expected to be <= "+dataWritten);
			Buffer.BlockCopy(data, srcOffset, dstArray, dstOffset, byteCount);
		}

		public override void Flush()
		{}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}


		public override void SetLength(long newLen)
		{
			throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}


		public override void Write(byte[] buffer, int offset, int count)
		{
			Require(count);
			Buffer.BlockCopy(buffer, offset, data, dataWritten, count);
			dataWritten += count;
		}
	}
}