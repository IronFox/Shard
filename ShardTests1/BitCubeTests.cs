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
		static Random random = new Random();

		[TestMethod()]
		public void BitCubeTest()
		{

			BitCube cube = new BitCube(new Int3(2));
			Assert.AreEqual(cube.OneCount, 0);
			Assert.AreEqual(cube.Size, new Int3(2));
			Assert.AreEqual(cube.IsEmpty, true);

			for (int i = 0; i < 1000; i++)
			{
				int set;
				BitCube cube2 = MakeRandomCube(out set);
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

		private static BitCube AllocateRandomCube()
		{
			Int3 size = new Int3(random.Next(1, 64), random.Next(1, 64), random.Next(1, 64));
			return new BitCube(size);
		}
		private static BitCube MakeRandomCube()
		{
			int set;
			return MakeRandomCube(out set);
		}
		public static int RandomFillCube(BitCube cube)
		{
			return FillCube(cube, (x, y, z) => (random.Next(2) == 1));
		}

		private static BitCube MakeRandomCube(out int numSet)
		{
			BitCube rs = AllocateRandomCube();
			numSet = RandomFillCube(rs);
			return rs;
		}

		public static InconsistencyCoverage RandomIC()
		{
			InconsistencyCoverage ic = InconsistencyCoverage.NewCommon();
			RandomFillCube(ic);
			return ic;
		}

		private static Int3 RandomInt3(Int3 inclusiveMin, Int3 exclusiveMax)
		{
			return new Int3(random.Next(inclusiveMin.X, exclusiveMax.X), random.Next(inclusiveMin.Y, exclusiveMax.Y), random.Next(inclusiveMin.Z, exclusiveMax.Z));
		}

		[TestMethod()]
		public void SubCubeTest()
		{
			for (int i = 0; i < 100; i++)
			{
				BitCube cube = new BitCube(RandomInt3(new Int3(1), new Int3(32)) * 2);  //make sure divisible by 2
				int xThreadhold = cube.Size.X / 2;
				FillCube(cube, (x, y, z) => x >= xThreadhold); //left half = 0, right half = 1
				Assert.AreEqual(cube.Size.Product / 2, cube.OneCount);

				Int3 size = RandomInt3(new Int3(1), cube.Size + 2);
				Int3 offset = RandomInt3(-size + 1, cube.Size);

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
			for (int i = 0; i < 100; i++)
			{
				BitCube cube = new BitCube(RandomInt3(new Int3(1), new Int3(32)) * 2);  //make sure divisible by 2
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
			int numNonZero = 0;
			for (int i = 0; i < 100 || numNonZero == 0; i++)
			{
				int set;
				var cube = MakeRandomCube(out set);
				if (set != 0)
					numNonZero++;
				cube.SetAllZero();
				Assert.IsTrue(cube.IsEmpty);

			}
		}

		[TestMethod()]
		public void SetAllOneTest()
		{
			int numNonOne = 0;
			for (int i = 0; i < 100 || numNonOne == 0; i++)
			{
				int set;
				var cube = MakeRandomCube(out set);
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

			{
				BitCube cube = new BitCube(new Int3(4));  //make sure divisible by 2
				GrowAxisTest(cube, 2);
			}



			for (int a = 0; a < 3; a++)
				for (int i = 0; i < 100; i++)
				{
					BitCube cube = new BitCube(RandomInt3(new Int3(1), new Int3(16)) * 4);  //make sure divisible by 4
					GrowAxisTest(cube, a);
				}
		}

		[TestMethod()]
		public void ToByteArrayTest()
		{

			for (int i = 0; i < 100; i++)
			{
				BitCube cube = MakeRandomCube();
				BitCube decoded = new BitCube(cube.Export());
				Assert.IsTrue(cube.Equals( decoded ),i.ToString());
			}
		}

		[TestMethod()]
		public void LoadCopyTest()
		{
			for (int i = 0; i < 10; i++)
			{
				BitCube cube0 = MakeRandomCube();
				BitCube cube1 = new BitCube();
				cube1.LoadCopy(cube0);
				Assert.AreEqual(cube0.Size, cube1.Size);
				int o0 = cube0.OneCount;
				int o1 = cube0.OneCount;
				Assert.AreEqual(o0, o1);
				cube1.SetAllZero();
				Assert.AreEqual(o0, cube0.OneCount);
				Assert.AreEqual(0, cube1.OneCount);
			}

		}

		[TestMethod()]
		public void IncludeTest()
		{
			for (int i = 0; i < 10; i++)
			{
				BitCube cube = new BitCube(new Int3(10));
				BitCube inc = new BitCube(new Int3(5));
				RandomFillCube(inc);

				cube.Include(inc, new Int3(5));
				Assert.AreEqual(cube.OneCount, inc.OneCount);

				for (int x = 0; x < 2; x++)
					for (int y = 0; y < 2; y++)
						for (int z = 0; z < 2; z++)
							if (x != 1 || y != 1 || z != 1)
								Assert.AreEqual(cube.OneCountIn(new Int3(x, y, z) * 5, new Int3(5)), 0);
				Assert.AreEqual(cube.OneCountIn(new Int3(5), new Int3(5)), inc.OneCount);
			}


			BitCube basis = new BitCube(new Int3(10));
			BitCube inc1 = new BitCube(new Int3(10));
			RandomFillCube(inc1);
			for (int i = 0; i < 100; i++)
			{
				basis.Include(inc1, RandomInt3(new Int3(-10), new Int3(20)));   //mad monkey
			}

		}

		[TestMethod()]
		public void OneCountInTest()
		{
			BitCube basis = new BitCube(new Int3(10));
			RandomFillCube(basis);
			for (int i = 0; i < 100; i++)
			{
				basis.OneCountIn(RandomInt3(new Int3(-10), new Int3(20)), RandomInt3(new Int3(-1), new Int3(10)));   //mad monkey
			}
		}

		[TestMethod()]
		public void SetOneTest()
		{
			for (int i = 0; i < 10; i++)
			{
				BitCube cube = new BitCube(new Int3(10));

				cube.SetOne(new Int3(5), new Int3(5));
				Assert.AreEqual(cube.OneCount, 5*5*5);

				for (int x = 0; x < 2; x++)
					for (int y = 0; y < 2; y++)
						for (int z = 0; z < 2; z++)
							if (x != 1 || y != 1 || z != 1)
								Assert.AreEqual(cube.OneCountIn(new Int3(x, y, z) * 5, new Int3(5)), 0);
				Assert.AreEqual(cube.OneCountIn(new Int3(5), new Int3(5)), 5*5*5);
			}


			BitCube basis = new BitCube(new Int3(10));
			for (int i = 0; i < 100; i++)
			{
				basis.SetOne(RandomInt3(new Int3(-10), new Int3(20)), RandomInt3(new Int3(-10), new Int3(20)));   //mad monkey
			}

		}
	}
}