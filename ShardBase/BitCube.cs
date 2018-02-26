using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using VectorMath;

namespace Shard
{
	[Serializable]
	public class BitCube
	{
		private uint[,,] grid;
		public const int BytesPerEntry = 4;
		public const int BitsPerEntry = BytesPerEntry*8;
		public const uint HighestBit = (1u << (BitsPerEntry - 1));

		[Serializable]
		public struct DBSerial
		{
			public int width, height, depth;
			public byte[] data;
			[JsonIgnore]
			public bool IsEmpty { get { return width == 0; } }

			public override bool Equals(object obj)
			{
				if (!(obj is DBSerial))
					return false;
				DBSerial other = (DBSerial)obj;
				return width == other.width 
					&& height == other.height 
					&& depth == other.depth 
					&& Helper.AreEqual(data, other.data);
			}

			public override int GetHashCode()
			{
				var hashCode = 1415848296;
				//hashCode = hashCode * -1521134295 + base.GetHashCode();
				hashCode = hashCode * -1521134295 + width.GetHashCode();
				hashCode = hashCode * -1521134295 + height.GetHashCode();
				hashCode = hashCode * -1521134295 + depth.GetHashCode();
				hashCode = hashCode * -1521134295 + EqualityComparer<byte[]>.Default.GetHashCode(data);
				return hashCode;
			}
		}

		/// <summary>
		/// Number of cells (bits) that contain a 1. -1 if currently unknown
		/// </summary>
		private int oneCount = -1;
		/// <summary>
		/// Bit size of the local cube. Actually stored bits may exceed this value
		/// </summary>
		private Int3 size;
		public Int3 Size
		{
			get
			{
				return size;
			}
			set
			{
				if (size == value)
					return;
				UpdateSize(value);
			}
		}

		/// <summary>
		/// Determines the size of the actual grid
		/// </summary>
		private Int3 GridSize
		{
			get
			{
				return new Int3(size.X, size.Y, (size.Z + BitsPerEntry - 1) / BitsPerEntry);
			}
		}

		/// <summary>
		/// Returns a linear enumerable of the local grid
		/// </summary>
		private IEnumerable<uint> Flat
		{
			get
			{
				for (int i = 0; i < grid.GetLength(0); i++)
					for (int j = 0; j < grid.GetLength(1); j++)
						for (int k = 0; k < grid.GetLength(2); k++)
							yield return grid[i, j, k];
			}
		}

		public IEnumerable<bool> Bits
		{
			get
			{
				for (int i = 0; i < Size.X; i++)
					for (int j = 0; j < Size.Y; j++)
						for (int k = 0; k < Size.Z; k++)
							yield return this[i, j, k];
			}
		}

		private void UpdateSize(Int3 newSize)
		{
			size = newSize;
			Int3 gridSize = GridSize;
			grid = new uint[gridSize.X, gridSize.Y, gridSize.Z];
			//SetAllZero();	//should be all zero in C# anyways
			oneCount = 0;
		}


		public BitCube() { }
		public BitCube(Int3 size)
		{
			UpdateSize(size);
		}


		public override bool Equals(object obj)
		{
			var other = obj as BitCube;
			if (other == null)
				return false;
			if (other == this)
				return true;
			return size == other.size && 
				((grid == null && other.grid == null)
				||
				System.Linq.Enumerable.SequenceEqual(Flat, other.Flat));
		}


		public override int GetHashCode()
		{
			return size.GetHashCode() * 17 + (grid != null ? grid.GetHashCode() : 0);
		}

		public DBSerial Export()
		{
			DBSerial rs = new DBSerial();
			rs.width = size.X;
			rs.height = size.Y;
			rs.depth = size.Z;
			int ones = this.OneCount;
			if (ones == 0)
				rs.data = null;
			else if (ones == size.Product)
				rs.data = new byte[] { 255 };
			else
			{
				int len = grid.Length * 4;
				rs.data = new byte[len];
				Buffer.BlockCopy(grid, 0, rs.data, 0, len);
			}
			return rs;
		}

		public BitCube(DBSerial serial)
		{
			UpdateSize(new Int3(serial.width,serial.height,serial.depth));
			if (serial.data != null)
			{
				if (serial.data.Length == 1)
				{
					//all ones
					this.SetAllOne();
				}
				else
				{
					if (GridSize.Product * 4 != (serial.data.Length))
						throw new ArgumentOutOfRangeException("Got " + (serial.data.Length) + " byte(s), but needed " + GridSize + "*4 = " + GridSize.Product * 4);
					Buffer.BlockCopy(serial.data, 0, grid, 0, serial.data.Length);
					oneCount = -1;
				}
			}
		}

		

		protected BitCube(BitCube source)
		{
			size = source.size;
			grid = source.grid;
			oneCount = source.oneCount;
		}

		protected void LoadMinimum(BitCube a, BitCube b)
		{
			if (a.Size != b.Size)
				throw new ArgumentException("Parameter size mismatch: " + a.Size + " != " + b.Size);
			oneCount = -1;
			UpdateSize(a.Size);
			for (int x = 0; x < grid.GetLength(0); x++)
				for (int y = 0; y < grid.GetLength(1); y++)
					for (int z = 0; z < grid.GetLength(2); z++)
						grid[x, y, z] = a.grid[x, y, z] & b.grid[x, y, z];
		}
		protected void LoadMaximum(BitCube a, BitCube b)
		{
			if (a.Size != b.Size)
				throw new ArgumentException("Parameter size mismatch: " + a.Size + " != " + b.Size);
			oneCount = -1;
			UpdateSize(a.Size);
			for (int x = 0; x < grid.GetLength(0); x++)
				for (int y = 0; y < grid.GetLength(1); y++)
					for (int z = 0; z < grid.GetLength(2); z++)
						grid[x, y, z] = a.grid[x, y, z] | b.grid[x, y, z];
		}

		public void LoadCopy(BitCube source)
		{
			UpdateSize(source.size);
			Buffer.BlockCopy(source.grid, 0, grid, 0, GridSize.Product * BytesPerEntry);
			oneCount = source.oneCount;
		}

		public void Include(BitCube other, Int3 offset)
		{
			if ((offset >= size).Any)
				return;

			Int3 sourceStart = Int3.Max(-offset,0);
			Int3 sourceCount = other.Size - sourceStart;

			Int3 destStart = Int3.Max(offset, 0);
			sourceCount = Int3.Min(destStart + sourceCount, size) - destStart;

			Int3 at = new Int3();
			for (at.X = 0; at.X < sourceCount.X; at.X++)
				for (at.Y = 0; at.Y < sourceCount.Y; at.Y++)
					for (at.Z = 0; at.Z < sourceCount.Z; at.Z++)
					{
						this[at + destStart] |= other[at + sourceStart];
					}
		}

		public void SetOne(Int3 offset, Int3 count)
		{
			Int3 sourceStart = Int3.Max(-offset, 0);
			Int3 sourceCount = count - sourceStart;
			Int3 destStart = Int3.Max(offset, 0);
			sourceCount = Int3.Min(destStart + sourceCount, size) - destStart;

			Int3 at = new Int3();
			for (at.X = 0; at.X < sourceCount.X; at.X++)
				for (at.Y = 0; at.Y < sourceCount.Y; at.Y++)
					for (at.Z = 0; at.Z < sourceCount.Z; at.Z++)
					{
						this[at + destStart] = true;
					}
		}



		public bool IsEmpty {
			get
			{
				if (oneCount == -1)
					ReDetermineRange();
				return oneCount == 0;
			}
		}

		public int OneCount
		{
			get
			{
				if (oneCount == -1)
					ReDetermineRange();
				return oneCount;
			}
		}

		public BitCube SubCube(Int3 offset, Int3 size)
		{
			if ((offset >= Size).Any)
				return new BitCube();
			for (int i = 0; i < 3; i++)
			{
				if (offset[i] < 0)
				{
					size[i] += offset[i];
					offset[i] = 0;
				}
				if (offset[i] + size[i] > Size[i])
				{
					size[i] = Size[i] - offset[i];
				}
			}
			BitCube rs = new BitCube(size);
			for (int x = 0; x < size.X; x++)
				for (int y = 0; y < size.Y; y++)
					for (int z = 0; z < size.Z; z++)
						rs[x, y, z] = this[offset.X + x, offset.Y + y, offset.Z + z];
			return rs;
		}

		public BitCube GrowOnesX()
		{
			Int3 rsSize = Size;
			rsSize.X += 2;
			BitCube rs = new BitCube(rsSize);
			for (int y = 0; y < grid.GetLength(1); y++)
				for (int z = 0; z < grid.GetLength(2); z++)
				{
					for (int x = 0; x < grid.GetLength(0); x++)
						rs.grid[x + 1, y, z] = grid[x, y, z];
					for (int x = 0; x + 1 < grid.GetLength(0); x++)
						rs.grid[x + 1, y, z] |= grid[x + 1, y, z];
					rs.grid[0, y, z] = grid[0, y, z];
					for (int x = 1; x <= grid.GetLength(0); x++)
						rs.grid[x + 1, y, z] |= grid[x - 1, y, z];
					rs.grid[rs.grid.GetLength(0) - 1, y, z] = grid[grid.GetLength(0) - 1, y, z];
				}
			rs.oneCount = -1;
			return rs;
		}
		public BitCube GrowOnesY()
		{
			Int3 rsSize = Size;
			rsSize.Y += 2;
			int lenY = grid.GetLength(1);
			BitCube rs = new BitCube(rsSize);
			for (int x = 0; x < grid.GetLength(0); x++)
				for (int z = 0; z < grid.GetLength(2); z++)
				{
					for (int y = 0; y < lenY; y++)
						rs.grid[x, y + 1, z] = grid[x, y, z];
					for (int y = 0; y + 1 < lenY; y++)
						rs.grid[x, y + 1, z] |= grid[x, y + 1, z];
					rs.grid[x, 0, z] = grid[x, 0, z];
					for (int y = 1; y <= lenY; y++)
						rs.grid[x, y + 1, z] |= grid[x, y - 1, z];
					rs.grid[x, lenY + 1, z] = grid[x, lenY - 1, z];
				}
			rs.oneCount = -1;
			return rs;
		}

		public BitCube GrowOnesZ()
		{
			Int3 rsSize = Size;
			rsSize.Z += 2;
			int lenZ = grid.GetLength(2);
			BitCube rs = new BitCube(rsSize);
			for (int x = 0; x < grid.GetLength(0); x++)
				for (int y = 0; y < grid.GetLength(1); y++)
				{
					for (int z = 0; z < lenZ; z++)
					{
						var input = grid[x, y, z];
						rs.grid[x, y, z] = input | (input << 1) | (input << 2);
					}
					for (int z = 1; z < lenZ; z++)
					{
						var edge = grid[x, y, z - 1];
						rs.grid[x, y, z] |= (edge >> (BitsPerEntry - 1)) & 1;
						rs.grid[x, y, z] |= (edge >> (BitsPerEntry - 2)) & 1;
					}
					//for (int z = 0; z + 1 < lenZ; z++)
					//{
					//	rs.grid[x, y, z] |= (grid[x, y, z + 1] & (1u)) != 0 ? HighestBit : 0;
					//}
				}
			rs.oneCount = -1;
			return rs;
		}


		public BitCube GrowOnes(int axis)
		{
			switch (axis)
			{
				case 0:
					return GrowOnesX();
				case 1:
					return GrowOnesY();
				case 2:
					return GrowOnesZ();
			}
			return null;
		}

		public BitCube GrowOnes()
		{
			return GrowOnesZ().GrowOnesX().GrowOnesY();
		}

		private void ReDetermineRange()
		{
			int rs = 0;
			foreach (var value in grid)
			{
				uint v = value;
				while (v != 0)
				{
					v &= (v - 1);
					rs++;
				}
			}
			oneCount = rs;
		}

		public bool AnySet { get { return !IsEmpty; } }

		public void SetAllZero()
		{
			for (int x = 0; x < grid.GetLength(0); x++)
				for (int y = 0; y < grid.GetLength(1); y++)
					for (int z = 0; z < grid.GetLength(2); z++)
						grid[x, y, z] = 0;
			oneCount = 0;
		}

		public void SetAllOne()
		{
			int edgeOnes = Size.Z % BitsPerEntry;
			uint edgeValue = (uint)(1 << edgeOnes) - 1;
			if (edgeValue == 0)
				edgeValue = uint.MaxValue;
			for (int x = 0; x < grid.GetLength(0); x++)
				for (int y = 0; y < grid.GetLength(1); y++)
				{
					for (int z = 0; z + 1 < grid.GetLength(2); z++)
						grid[x, y, z] = uint.MaxValue;
					if (grid.GetLength(2) > 0)
						grid[x, y, grid.GetLength(2) - 1] = edgeValue;
				}
			oneCount = Size.Product;
		}

		public int OneCountIn(Int3 offset, Int3 count)
		{
			Int3 count3 = count - Int3.Max(-offset, 0);
			offset = Int3.Max(offset, 0);
			count3 = Int3.Min(offset + count3, size) - offset;

			Int3 at = new Int3();
			int rs = 0;
			for (at.X = 0; at.X < count3.X; at.X++)
				for (at.Y = 0; at.Y < count3.Y; at.Y++)
					for (at.Z = 0; at.Z < count3.Z; at.Z++)
						rs += this[at + offset] ? 1 : 0;
			return rs;
		}

		public bool this[int x, int y, int z]
		{
			get
			{
				int zBit = z % BitsPerEntry;
				z /= BitsPerEntry;
				uint val = grid[x, y, z];
				return ((val >> zBit) & 1) != 0;
			}
			set
			{
				int zBit = z % BitsPerEntry;
				z /= BitsPerEntry;
				uint val = grid[x, y, z];
				uint orig = val;
				if (value)
				{
					val |= (1u << zBit);
				}
				else
				{
					val &= ~(1u << zBit);
				}
				oneCount = -1;
				grid[x, y, z] = val;
			}
		}

		public bool this[Int3 coords]
		{
			get
			{
				return this[coords.X, coords.Y, coords.Z];
			}
			set
			{
				this[coords.X, coords.Y, coords.Z] = value;
			}
		}
	}
}