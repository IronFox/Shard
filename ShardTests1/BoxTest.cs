using System;
using Base;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using VectorMath;

namespace ShardTests1
{
	[TestClass]
	public class BoxTest
	{
		static Random random = new Random();

		static void AssertLess(float x, float y)
		{
			if (x >= y)
				Assert.Fail("x (" + x + ") < y (" + y + ") failed");
		}

		[TestMethod]
		public void ClampTest()
		{
			for (int k = 0; k < 100; k++)
			{
				Vec3 offset = random.NextVec3(-1, 1);
				Vec3 size = random.NextVec3(0.01f, 1);

				Box test = Box.OffsetSize(offset, size, Bool3.False);
				AssertLess(test.X.InclusiveMax, test.X.Max);
				AssertLess(test.Y.InclusiveMax, test.Y.Max);
				AssertLess(test.Z.InclusiveMax, test.Z.Max);

				Box box = Box.OffsetSize(offset, size, random.NextBool3());
				for (int i = 0; i < 100; i++)
				{
					Vec3 clamped = box.Clamp(random.NextVec3(-5, 5));
					AssertContains(box.X, clamped.X, "X");
					AssertContains(box.Y, clamped.Y, "Y");
					AssertContains(box.Z, clamped.Z, "Z");
					Assert.IsTrue(box.Contains(clamped),box+".Contains("+clamped+")");
				}
			}
		}

		private static void AssertContains(Box.Range range, float v, string name)
		{
			if (!range.Contains(v))
				Assert.Fail(name + ": " + v + " is not contained by " + range);
		}
	}
}
