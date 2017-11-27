using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Shard.EntityChange;

namespace Shard.Tests
{
	[TestClass()]
	public class MyCouchSerializerTests
	{
		static Random random = new Random();

		public static SDS RandomSDS()
		{
			return new SDS(random.Next(100),EntityPoolTests.CreateEntities(16).ToArray(),BitCubeTests.RandomIC(),RandomIntermediate(),RandomOutboundRCSs());
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

		private static RCS RandomOutboundRCS()
		{
			return new RCS(EntityChangeSetTests.RandomSet(), BitCubeTests.RandomIC());
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
			Shard.Helper.MyCouchSerializer serial = new Shard.Helper.MyCouchSerializer();


			for (int i = 0; i < 100; i++)
			{
				SerialRCSStack stack = SerialRCSStackTests.RandomStack();
				string json = serial.Serialize(stack);
				SerialRCSStack stackBack = serial.Deserialize<SerialRCSStack>(json);
				Assert.AreEqual(stack, stackBack);
			}

			for (int i = 0; i < 100; i++)
			{
				SDS sds = RandomSDS();
				var s = sds.Export();
				string json = serial.Serialize(s);
				var reverse = serial.Deserialize<SDS.Serial>(json);
				Assert.AreEqual(s, reverse);

			}
		}
		
	}
}