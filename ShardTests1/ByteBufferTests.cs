using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard.Tests
{
	[TestClass()]
	public class ByteBufferTests
	{
		[TestMethod()]
		public void PutTest()
		{
			byte[] ar = new byte[4];
			Random random = new Random();
			for (int i = 0; i < 1000; i++)
			{
				int v = random.Next(int.MinValue, int.MaxValue);
				Shard.ByteBuffer.Put(ar, 0, v);
				int decoded = BitConverter.ToInt32(ar, 0);
				Assert.AreEqual(v, decoded);
			}

		}
	}
}