﻿using System;
using System.Collections.Generic;
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

		private Int3 GridSize
		{
			get
			{
				return new Int3(size.X, size.Y, (size.Z + BitsPerEntry - 1) / BitsPerEntry);
			}
		}

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

		private void UpdateSize(Int3 newSize)
		{
			size = newSize;
			Int3 gridSize = GridSize;
			grid = new uint[gridSize.X, gridSize.Y, gridSize.Z];
			//SetAllZero();	//should be all zero in C# anyways
			allZero = true;
			rangeUnknown = false;
		}


		public BitCube() { }
		public BitCube(Int3 size)
		{
			UpdateSize(size);
		}

		public static bool operator==(BitCube a, BitCube b)
		{
			return a.size == b.size && 
				((a.grid == null && b.grid == null)
				||
				System.Linq.Enumerable.SequenceEqual(a.Flat, b.Flat));
		}
		public static bool operator !=(BitCube a, BitCube b)
		{
			return !(a == b);
		}

		public override bool Equals(object obj)
		{
			return obj is BitCube && ((BitCube)obj == this);
		}

		public override int GetHashCode()
		{
			return size.GetHashCode() * 17 + (grid != null ? grid.GetHashCode() : 0);
		}

		public byte[] ToByteArray()
		{
			ByteBuffer stream = new ByteBuffer();
			stream.Add(size);
			stream.Add(grid);
			return stream.ToArray();
		}

		public BitCube(byte[] data)
		{
			Int3 size;
			size.X = BitConverter.ToInt32(data, 0);
			size.Y = BitConverter.ToInt32(data, 4);
			size.Z = BitConverter.ToInt32(data, 8);
			UpdateSize(size);
			if (GridSize.Product * 4 != (data.Length - 12))
				throw new ArgumentOutOfRangeException("Got extra "+ (data.Length - 12)+" byte(s), but needed "+GridSize+"*4 = "+GridSize.Product*4);
			Buffer.BlockCopy(data, 12, grid, 0, data.Length - 12);
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