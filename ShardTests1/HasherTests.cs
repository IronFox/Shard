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
	public class HasherTests
	{
		[TestMethod()]
		public void FinishTest()
		{
			Random random = new Random();
			for (int i = 0; i < 10; i++)
			{
				Hasher h0 = new Hasher();
				Hasher h1 = new Hasher();

				int n0 = random.Next(3);
				int n1 = random.Next(3);
				h0.Add(n0);
				h0.Add(n1);

				h1.Add(n1);
				h1.Add(n0);


				bool equal = h0.Finish() == h1.Finish();
				Assert.AreEqual(equal, n0 == n1);
			}




		}
	}
}