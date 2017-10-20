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

		private static int FillCube(BitCube cube, Func<int, int, int, bool> f)
		{
			int numSet = 0;
			var size = cube.Size;
			for (int x = 0; x < size.X; x++)
				for (int y = 0; y < size.X; y++)
					for (int z = 0; z < size.X; z++)
					{
						bool set = f(x,y,z);
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
			for (int i = 0; i < 1000; i++)
			{
				BitCube cube = AllocateRandomCube(random);
				FillCube(cube, (x, y, z) => x > cube.Size.X / 2); //left half = 0, right half = 1

				Int3 size = RandomInt3(random, new Int3(1), cube.Size + 2);
				Int3 offset = RandomInt3(random, -size + 1, cube.Size);
			}
		}

		[TestMethod()]
		public void GrowOnesTest()
		{
			Assert.Fail();
		}

		[TestMethod()]
		public void SetAllZeroTest()
		{
			Assert.Fail();
		}

		[TestMethod()]
		public void SetAllOneTest()
		{
			Assert.Fail();
		}
	}
}