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
			Random random = new Random();
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

		[TestMethod()]
		public void ByteArraysAreEqualTest()
		{
			Random random = new Random();
			for (int i = 0; i < 1000; i++)
			{
				byte[] a = new byte[random.Next(100)];
				byte[] b = new byte[random.Next(100)];
				random.NextBytes(a);
				random.NextBytes(b);


				bool equal = EqualityComparer<byte[]>.Default.Equals(a, b);
				bool checkEqual = Helper.AreEqual(a,b);
				Assert.AreEqual(equal, checkEqual);
				Assert.IsTrue(Helper.AreEqual(a, a),i.ToString());
				Assert.IsTrue(Helper.AreEqual(b, b),i.ToString());
			}
		}
	}
}