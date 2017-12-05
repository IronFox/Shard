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
	public class EntityRandomTests
	{
		[TestMethod()]
		public void NextBoolTest()
		{
			EntityRandom random = new EntityRandom(new Random().Next());

			int[] buckets = new int[2];
			for (int i = 0; i < 2000; i++)
				if (random.NextBool())
					buckets[1]++;
				else
					buckets[0]++;
			CheckEvenDistribution(buckets, 2000);
		}

		[TestMethod()]
		public void NextTest()
		{
			EntityRandom random = new EntityRandom(0);

			TestIntBuckets(random, 2);
			TestIntBuckets(random, 10);
			TestIntBuckets(random, 100);
		}

		private static void TestIntBuckets(EntityRandom random, int numBuckets)
		{
			int[] count = new int[numBuckets];
			int numRuns = 1000 * count.Length;
			for (int i = 0; i < numRuns; i++)
			{
				int n = random.Next(count.Length);
				Assert.IsTrue(n >= 0 && n < count.Length);
				count[n]++;
			}
			CheckEvenDistribution(count, numRuns);
		}


		private static void CheckEvenDistribution(int[] buckets, int bucketSum)
		{
			for (int i = 0; i < buckets.Length; i++)
			{
				float relative = (float)buckets[i] / bucketSum;
				float shouldBe = 1f / buckets.Length;
				float error = Math.Abs((relative - shouldBe) / shouldBe);
				Assert.IsTrue(error < 0.1f,"error: "+error+", relative: "+relative+", shouldBe: "+shouldBe+", bucket "+ i+"/"+buckets.Length);
			}

		}

		[TestMethod()]
		public void NextFloatTest()
		{
			
			EntityRandom random = new EntityRandom(new Random().Next());
			int[] count = new int[1000];
			int numRuns = 10000 * count.Length;
			for (int i = 0; i < numRuns; i++)
			{
				float f = random.NextFloat();
				int bucket = Math.Min((int)(f * count.Length), count.Length - 1);
				count[bucket]++;
			}
			CheckEvenDistribution(count, numRuns);

		}
	}
}