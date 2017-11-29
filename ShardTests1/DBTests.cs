using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;

namespace Shard.Tests
{
	[TestClass]
	public class DBTests
	{
		static Random random = new Random();


		private static async Task DBRCSStackTestAsync()
		{

			var s = new DB.RCSStack(RandomRCSID());
			int numEntries = 10;
			RCS[] rcss = new RCS[numEntries];
			Task[] tasks = new Task[numEntries];

			Parallel.For(0, numEntries, i =>
			{
				RCS rcs = RandomOutboundRCS(true);
				tasks[i] = s.PutAsync(i * 2, rcs.Export());  //only every second (0,2,4,...). Should fill intermeditate ones
				rcss[i] = rcs;
			});

			Task.WaitAll(tasks);


			await s.ViewAsync(stack =>
			{
				StringBuilder seqBuilder = new StringBuilder();
				for (int i = 0; i < stack.CountEntries(); i++)
					seqBuilder.Append((stack.Entries[i].IsDefined() ? "x" : "-"));
				string seq = seqBuilder.ToString();

				Assert.AreEqual(stack.CountEntries(), (numEntries -1) * 2 + 1);
				for (int i = 0; i+1 < numEntries; i++)
					Assert.IsTrue(stack.Entries[i * 2 + 1].IsUndefined(),i.ToString());
				for (int i = 0; i < numEntries; i++)
				{
					Assert.IsTrue(stack.Entries[i * 2].IsDefined(), i.ToString()+" "+seq);
					Assert.AreEqual(rcss[i], new RCS(stack.Entries[i * 2]), i.ToString());
				}
			}
			);

			for (int i = 0; i < numEntries; i++)
			{
				await s.SignalOldestGenerationUpdateAsync(0, i * 2, 0);
				await s.ViewAsync(stack =>
				{
					Assert.AreEqual(stack.CountEntries(), (numEntries - 1 - i) * 2 + 1);
					for (int k = i; k < numEntries; k++)
						Assert.AreEqual(rcss[k], new RCS(stack.Entries[(k - i) * 2]));
				}
				);
			}
		}

		[TestMethod]
		public void DBRCSStackTest()
		{
			DBRCSStackTestAsync().Wait();

		}

		private static RCS.ID RandomRCSID()
		{
			return new RCS.ID(random.NextInt3(0, 10), random.NextInt3(0, 10));
		}

		public static SDS RandomSDS()
		{
			return new SDS(random.Next(100), EntityPoolTests.CreateEntities(16).ToArray(), BitCubeTests.RandomIC(), RandomIntermediate(), RandomOutboundRCSs());
		}

		public static RCS[] RandomOutboundRCSs()
		{
			return RandomOutboundRCSs(random.Next(26));
		}

		private static RCS[] RandomOutboundRCSs(int count)
		{
			RCS[] rs = new RCS[count];
			for (int i = 0; i < count; i++)
				rs[i] = RandomOutboundRCS();
			return rs;
		}

		private static RCS RandomOutboundRCS(bool forceConsistent = false)
		{
			return new RCS(EntityChangeSetTests.RandomSet(), forceConsistent ? InconsistencyCoverage.NewCommon() : BitCubeTests.RandomIC());
		}

		public static SDS.IntermediateData RandomIntermediate()
		{
			SDS.IntermediateData rs = new SDS.IntermediateData();
			rs.entities = EntityPoolTests.RandomPool(random.Next(16));
			rs.ic = BitCubeTests.RandomIC();
			rs.inputConsistent = random.NextBool();
			rs.localChangeSet = EntityChangeSetTests.RandomSet();
			return rs;
		}


		[TestMethod()]
		public void SerializeTest()
		{
			DB.Serializer serializer = new DB.Serializer();


			for (int i = 0; i < 100; i++)
			{
				RCS[] rcs;
				SerialRCSStack stack = SerialRCSStackTests.RandomStack(out rcs);
				string json = serializer.Serialize(stack);
				SerialRCSStack stackBack = serializer.Deserialize<SerialRCSStack>(json);
				Assert.AreEqual(stack, stackBack);
				for (int k = 0; k < stackBack.CountEntries(); k++)
				{
					Assert.AreEqual(rcs[k], new RCS(stackBack.Entries[k]));
				}
			}

			for (int i = 0; i < 100; i++)
			{
				SDS sds = RandomSDS();
				var s = sds.Export();
				string json = serializer.Serialize(s);
				var reverse = serializer.Deserialize<SDS.Serial>(json);
				Assert.AreEqual(s, reverse);
				SDS rev2 = new SDS(reverse);
				Assert.IsTrue(sds.ICAndEntitiesAreEqual(rev2));
			}
		}


	}
}
