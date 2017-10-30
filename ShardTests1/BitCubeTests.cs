using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorMath;


namespace Shard.Tests
{
	[TestClass()]
	public class BitCubeTests
	{
		[TestMethod()]
		public void BitCubeTest()
		{

			BitCube cube = new BitCube(new Int3(2));
			Assert.AreEqual(cube.OneCount, 0);
			Assert.AreEqual(cube.Size, new Int3(2));
			Assert.AreEqual(cube.IsEmpty, true);

			Random random = new Random(); ;
			for (int i = 0; i < 1000; i++)
			{
				int set;
				BitCube cube2 = MakeRandomCube(random, out set);
				Assert.AreEqual(cube2.OneCount, set);
				Assert.AreEqual(cube2.IsEmpty, set == 0);
			}

		}
		private static int FillCube(BitCube cube, Func<Int3, bool> f)
		{
			return FillCube(cube, (x, y, z) => f(new Int3(x, y, z)));
		}

		private static int FillCube(BitCube cube, Func<int, int, int, bool> f)
		{
			int numSet = 0;
			var size = cube.Size;
			for (int x = 0; x < size.X; x++)
				for (int y = 0; y < size.Y; y++)
					for (int z = 0; z < size.Z; z++)
					{
						bool set = f(x, y, z);
						cube[x, y, z] = set;
						if (set)
							numSet++;
					}
			return numSet;
		}

		private static BitCube AllocateRandomCube(Random random)
		{
			Int3 size = new Int3(random.Next(1, 64), random.Next(1, 64), random.Next(1, 64));
			return new BitCube(size);
		}
		private static BitCube MakeRandomCube(Random random)
		{
			int set;
			return MakeRandomCube(random, out set);
		}
		private static BitCube MakeRandomCube(Random random, out int numSet)
		{
			BitCube rs = AllocateRandomCube(random);
			numSet = FillCube(rs, (x, y, z) => (random.Next(2) == 1));
			return rs;
		}

		private static Int3 RandomInt3(Random random, Int3 inclusiveMin, Int3 exclusiveMax)
		{
			return new Int3(random.Next(inclusiveMin.X, exclusiveMax.X), random.Next(inclusiveMin.Y, exclusiveMax.Y), random.Next(inclusiveMin.Z, exclusiveMax.Z));
		}

		[TestMethod()]
		public void SubCubeTest()
		{
			Random random = new Random();
			for (int i = 0; i < 100; i++)
			{
				BitCube cube = new BitCube(RandomInt3(random, new Int3(1), new Int3(32)) * 2);  //make sure divisible by 2
				int xThreadhold = cube.Size.X / 2;
				FillCube(cube, (x, y, z) => x >= xThreadhold); //left half = 0, right half = 1
				Assert.AreEqual(cube.Size.Product / 2, cube.OneCount);

				Int3 size = RandomInt3(random, new Int3(1), cube.Size + 2);
				Int3 offset = RandomInt3(random, -size + 1, cube.Size);

				int ones = 0, zeroes = 0;
				if (offset.X >= xThreadhold)
					ones = Math.Max(0, Math.Min(offset.X + size.X, cube.Size.X) - offset.X);
				else
				if (offset.X + size.X > xThreadhold)
				{
					ones = Math.Min(offset.X + size.X, cube.Size.X) - xThreadhold;
					zeroes = xThreadhold - Math.Max(0, offset.X);
				}
				else
				{
					zeroes = Math.Max(0, offset.X + size.X) - Math.Max(0, offset.X);
				}
				BitCube sub = cube.SubCube(offset, size);
				Assert.IsTrue((sub.Size <= size).All, sub.Size + " > " + size);
				ones *= sub.Size.YZ.Product;
				zeroes *= sub.Size.YZ.Product;
				Assert.AreEqual(ones, sub.OneCount, i + ": " + offset + ", " + size + " in " + cube.Size + "; sub: " + sub.Size);
				Assert.AreEqual(zeroes, sub.Size.Product - sub.OneCount, i.ToString());
			}
		}

		[TestMethod()]
		public void GrowOnesTest()
		{
			Random random = new Random();
			for (int i = 0; i < 100; i++)
			{
				BitCube cube = new BitCube(RandomInt3(random, new Int3(1), new Int3(32)) * 2);  //make sure divisible by 2
				Int3 half = cube.Size / 2;
				FillCube(cube, (coords) => (coords >= half / 2 & coords < half * 3 / 2).All); //left half = 0, right half = 1

				int ones = cube.OneCount;
				Assert.AreEqual(ones, half.Product);
				Assert.AreNotEqual(ones, cube.Size.Product);
				Assert.AreNotEqual(ones, 0);

				BitCube expanded = cube.GrowOnes();//.SubCube(new Int3(1),cube.Size);
				int newOnes = expanded.OneCount;
				Assert.IsTrue(newOnes > ones);
				int expectNewOnes = ones
									+ half.XY.Product * 2
									+ half.XZ.Product * 2
									+ half.YZ.Product * 2
									+ half.X * 4
									+ half.Y * 4
									+ half.Z * 4
									+ 8;
				Assert.AreEqual(expectNewOnes, newOnes, "old=" + ones + ", " + half);

			}
		}

		[TestMethod()]
		public void SetAllZeroTest()
		{
			Random random = new Random();
			int numNonZero = 0;
			for (int i = 0; i < 100 || numNonZero == 0; i++)
			{
				int set;
				var cube = MakeRandomCube(random, out set);
				if (set != 0)
					numNonZero++;
				cube.SetAllZero();
				Assert.IsTrue(cube.IsEmpty);

			}
		}

		[TestMethod()]
		public void SetAllOneTest()
		{
			Random random = new Random();
			int numNonOne = 0;
			for (int i = 0; i < 100 || numNonOne == 0; i++)
			{
				int set;
				var cube = MakeRandomCube(random, out set);
				//if (cube.OneCount != cube.Size.Product) ;
				numNonOne++;
				cube.SetAllOne();
				Assert.AreEqual(cube.OneCount, cube.Size.Product);

			}
		}

		private void GrowAxisTest(BitCube cube, int a)
		{
			Int3 half = cube.Size / 2;
			FillCube(cube, (coords) => (coords >= half / 2 & coords < half * 3 / 2).All); //left half = 0, right half = 1

			int ones = cube.OneCount;
			Assert.AreEqual(ones, half.Product);
			Assert.AreNotEqual(ones, cube.Size.Product);
			Assert.AreNotEqual(ones, 0);

			Int3 offset = Int3.Zero;
			offset[a] = 1;

			BitCube expanded = cube.GrowOnes(a);
			BitCube truncated = expanded.SubCube(offset, cube.Size);
			int newOnes = truncated.OneCount;
			Assert.IsTrue(newOnes > ones);
			Assert.AreEqual(expanded.OneCount, newOnes, "old=" + ones + ", " + half + " in " + cube.Size + " at " + offset + " => " + truncated.Size);

			int slice = 0;
			switch (a)
			{
				case 0:
					slice = half.YZ.Product;
					break;
				case 1:
					slice = half.XZ.Product;
					break;
				case 2:
					slice = half.XY.Product;
					break;

			}

			int expectNewOnes = ones
								+ slice * 2;
			Assert.AreEqual(expectNewOnes, newOnes, "old=" + ones + ", " + half + " in " + cube.Size + " at " + offset + " => " + truncated.Size);

			Foreach(cube.Size, (p)
			 =>
				{
					bool input = cube[p];
					if (input)
					{
						Assert.IsTrue(truncated[p]);
						Assert.IsTrue(expanded[p + offset]);
						Assert.IsTrue(expanded[p + offset + offset]);
						Assert.IsTrue(expanded[p]);
					}
				}

			);

		}

		private void Foreach(Int3 size, Action<Int3> p)
		{
			for (int x = 0; x < size.X; x++)
				for (int y = 0; y < size.Y; y++)
					for (int z = 0; z < size.Z; z++)
					{
						p(new Int3(x, y, z));
					}
		}

		[TestMethod()]
		public void GrowAxisTest()
		{
			Random random = new Random();

			{
				BitCube cube = new BitCube(new Int3(4));  //make sure divisible by 2
				GrowAxisTest(cube, 2);
			}



			for (int a = 0; a < 3; a++)
				for (int i = 0; i < 100; i++)
				{
					BitCube cube = new BitCube(RandomInt3(random, new Int3(1), new Int3(16)) * 4);  //make sure divisible by 4
					GrowAxisTest(cube, a);
				}
		}

		[TestMethod()]
		public void ToByteArrayTest()
		{
			Random random = new Random();

			for (int i = 0; i < 100; i++)
			{
				BitCube cube = MakeRandomCube(random);
				BitCube decoded = new BitCube(cube.ToByteArray());
				Assert.IsTrue(cube == decoded);
			}
		}
	}
}