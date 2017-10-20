using System;
using VectorMath;

namespace Shard
{
	public class BitCube
	{
		private uint[,,] grid;
		public const int BitsPerEntry = 32;
		public const uint HighestBit = (1u << (BitsPerEntry - 1));

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
				UpdateSize(size);
			}
		}

		private void UpdateSize(Int3 newSize)
		{
			size = newSize;
			grid = new uint[size.X, size.Y, (size.Z + BitsPerEntry-1) / BitsPerEntry];
			SetAllZero();
		}

		public BitCube(Int3 size)
		{
			UpdateSize(size);
		}

		public BitCube()
		{
		}

		private bool allZero = true;
		private bool rangeUnknown = false;

		public bool IsEmpty {
			get
			{
				if (rangeUnknown)
					ReDetermineRange();
				return allZero;
			}
		}

		public int OneCount
		{
			get
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
				return rs;
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

		public BitCube GrowOnes()
		{
			BitCube rs0 = new BitCube(Size + 2);
			if (IsEmpty)
				return rs0;

			for (int x = 0; x < grid.GetLength(0); x++)
				for (int y = 0; y < grid.GetLength(1); y++)
				{
					for (int z = 0; z < grid.GetLength(2); z++)
					{
						rs0.grid[x, y, z] = grid[x, y, z] | (grid[x, y, z] << 1) | (grid[x, y, z] >> 1);
					}
					for (int z = 1; z < rs0.grid.GetLength(2); z++)
					{
						rs0.grid[x, y, z] |= (grid[x, y, z - 1] & HighestBit) != 0 ? 1u : 0;
					}
					for (int z = 0; z+1 < grid.GetLength(2); z++)
					{
						rs0.grid[x, y, z] |= (grid[x, y, z + 1] & (1u)) != 0 ? HighestBit : 0;
					}
				}
			BitCube rs1 = new BitCube(Size + 2);
			for (int x = 0; x < rs1.grid.GetLength(0); x++)
				for (int y = 1; y+1 < rs1.grid.GetLength(1); y++)
					for (int z = 0; z < rs1.grid.GetLength(2); z++)
					{
						rs1.grid[x, y, z] = rs0.grid[x, y, z] | rs0.grid[x, y + 1, z] | rs0.grid[x, y - 1, z];
					}
			BitCube rs2 = new BitCube(Size + 2);
			for (int x = 1; x + 1 < rs1.grid.GetLength(0); x++)
				for (int y = 0; y < rs1.grid.GetLength(1); y++)
					for (int z = 0; z < rs1.grid.GetLength(2); z++)
					{
						rs2.grid[x, y, z] = rs1.grid[x, y, z] | rs1.grid[x + 1, y, z] | rs1.grid[x -1 , y , z];
					}

			rs2.rangeUnknown = false;
			rs2.allZero = false;

			return rs2;
		}

		private void ReDetermineRange()
		{
			allZero = true;
			foreach (var value in grid)
				if (value != 0)
					allZero = false;
			rangeUnknown = false;
		}

		public bool AnySet { get { return !IsEmpty; } }

		public void SetAllZero()
		{
			for (int x = 0; x < grid.GetLength(0); x++)
				for (int y = 0; y < grid.GetLength(1); y++)
					for (int z = 0; z < grid.GetLength(2); z++)
						grid[x, y, z] = 0;
			allZero = true;
			rangeUnknown = false;
		}
		public void SetAllOne()
		{
			for (int x = 0; x < grid.GetLength(0); x++)
				for (int y = 0; y < grid.GetLength(1); y++)
					for (int z = 0; z < grid.GetLength(2); z++)
						grid[x, y, z] = uint.MaxValue;
			allZero = false;
			rangeUnknown = false;
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
				if (value)
				{
					val |= (1u << zBit);
					allZero = false;
					rangeUnknown = false;
				}
				else
				{
					val &= ~(1u << zBit);
					if (!allZero)
						rangeUnknown = true;
				}
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