using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;

namespace Shard.Tests
{
	[TestClass]
	public class DBTests
	{
		static Random random = new Random();


		[TestMethod]
		public void DBRCSStackTest()
		{
			var s = new DB.RCSStack(RandomRCSID());
			for (int i = 0; i < 10; i++)
			{
				RCS rcs = RandomOutboundRCS(true);
				s.Put(i, rcs);
			}

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
