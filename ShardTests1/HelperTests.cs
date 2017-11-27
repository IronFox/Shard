using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard.Tests
{
	[TestClass()]
	public class HelperTests
	{
		static Random random = new Random();

		private void TestFloat(float f)
		{
			int b0 = Helper.FloatToInt(f);
			int b1 = Helper.FloatToInt2(f);
			int b2 = BitConverter.ToInt32(BitConverter.GetBytes(f), 0);

			Assert.AreEqual(b0, b1);
			Assert.AreEqual(b0, b2);

			float back = BitConverter.ToSingle(BitConverter.GetBytes(b2), 0);
			float back2 = Helper.IntToFloat(b2);
			Assert.AreEqual(f, back);
			Assert.AreEqual(back2, back);
		}

		[TestMethod()]
		public void FloatToIntTest()
		{
			TestFloat(0);
			TestFloat(1);
			TestFloat(-1);
			TestFloat(float.MaxValue);
			TestFloat(float.MinValue);
			TestFloat(float.Epsilon);
			TestFloat(float.NegativeInfinity);
			TestFloat(float.PositiveInfinity);
			TestFloat(float.NaN);
			for (int i = 0; i < 1000; i++)
			{
				TestFloat(random.NextFloat(-1000, 1000));
			}
		}


		static void TestByteArrays(int sizeA, int sizeB)
		{
			byte[] a = new byte[sizeA];
			byte[] b = new byte[sizeB];
			random.NextBytes(a);
			random.NextBytes(b);

			bool equal1 = a.SequenceEqual(b);
			//bool equal = EqualityComparer<byte[]>.Default.Equals(a, b);	//don't work on empty arrays
			bool checkEqual = Helper.AreEqual(a, b);
			//Assert.AreEqual(equal, equal1, Helper.ToString(a)+"=="+ Helper.ToString(b));
			Assert.AreEqual(equal1, checkEqual, Helper.ToString(a)+"=="+ Helper.ToString(b));
			Assert.IsTrue(Helper.AreEqual(a, a), Helper.ToString(a));
			Assert.IsTrue(Helper.AreEqual(b, b), Helper.ToString(b));
		}
		static void TestByteArrays(int size)
		{
			TestByteArrays(size, size);
		}

		[TestMethod()]
		public void ByteArraysAreEqualTest()
		{
			TestByteArrays(0);
			for (int i = 0; i < 1000; i++)
			{
				TestByteArrays(random.Next(32));
				TestByteArrays(random.Next(32), random.Next(32));
			}
		}
	}
}