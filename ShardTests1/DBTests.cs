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
			var ctx = EntityChangeSetTests.RandomContext();
			var s = new DB.RCSStack(RandomRCSID());
			int numEntries = 10;
			RCS[] rcss = new RCS[numEntries];
			Task[] tasks = new Task[numEntries];

			Parallel.For(0, numEntries, i =>
			{
				RCS rcs = RandomOutboundRCS(ctx,true);
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

		private static RCS.StackID RandomRCSID()
		{
			return new RCS.StackID(random.NextInt3(0, 10), random.NextInt3(0, 10));
		}

		public static SDS RandomSDS(SimulationContext ctx)
		{
			return new SDS(random.Next(100), EntityPoolTests.CreateEntities(ctx.LocalSpace,16).ToArray(), BitCubeTests.RandomIC(), random.NextBool(0.01f), RandomClientMessages());
		}

		public static RCS[] RandomOutboundRCSs(SimulationContext ctx)
		{
			return RandomOutboundRCSs(ctx,random.Next(26));
		}

		public static Dictionary<Guid, ClientMessage[]> RandomClientMessages()
		{
			if (random.NextBool(0.8f))
				return null;
			return RandomClientMessages(random.Next(2), random.Next(16)+1);
		}

		public static Dictionary<Guid, ClientMessage[]> RandomClientMessages(int numGuids, int maxMessagesEach)
		{
			if (numGuids == 0)
				return null;
			Dictionary<Guid, ClientMessage[]> rs = new Dictionary<Guid, ClientMessage[]>();
			for (int i = 0; i < numGuids; i++)
			{
				Guid guid = Guid.NewGuid(); ;

				var ar = new ClientMessage[random.Next(maxMessagesEach - 1) + 1];
				rs[guid] = ar;
				for (int j = 0; j < ar.Length; j++)
					ar[j] = RandomClientMessage();
			}
			return rs;
		}

		public static EntityID RandomEntityID()
		{
			return new EntityID(Guid.NewGuid(), random.NextVec3(0, 1));
		}

		public static EntityMessage RandomEntityMessage()
		{
			return new EntityMessage(random.NextBool() ? new Actor(Guid.NewGuid()) : new Actor(RandomEntityID()), random.NextBool(), random.Next(3), EntityChangeSetTests.RandomByteArray(true));
		}

		public static ClientMessage RandomClientMessage()
		{
			return new ClientMessage(
				new ClientMessageID(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), random.Next(3), random.Next(100)),
				new ClientMessageBody(EntityChangeSetTests.RandomByteArray(true), random.Next(1000), random.Next(1000), random.Next(3), false)
				);
		}

		private static RCS[] RandomOutboundRCSs(SimulationContext ctx, int count)
		{
			RCS[] rs = new RCS[count];
			for (int i = 0; i < count; i++)
				rs[i] = RandomOutboundRCS(ctx);
			return rs;
		}

		private static RCS RandomOutboundRCS(SimulationContext ctx, bool forceConsistent = false)
		{
			return new RCS(EntityChangeSetTests.RandomSet(ctx), forceConsistent ? InconsistencyCoverage.NewCommon() : BitCubeTests.RandomIC());
		}

		public static IntermediateSDS RandomIntermediate(EntityChange.ExecutionContext ctx)
		{
			IntermediateSDS rs = new IntermediateSDS();
			rs.entities = EntityPoolTests.RandomPool(random.Next(16),ctx);
			rs.ic = BitCubeTests.RandomIC();
			rs.inputConsistent = random.NextBool();
			rs.localChangeSet = EntityChangeSetTests.RandomSet(ctx.Ranges);
			return rs;
		}


		[TestMethod()]
		public void SerializeTest()
		{
			DB.Serializer serializer = new DB.Serializer();
			var ctx = EntityChangeSetTests.RandomContext();

			for (int i = 0; i < 100; i++)
			{
				RCS[] rcs;
				SerialRCSStack stack = SerialRCSStackTests.RandomStack(ctx,out rcs);
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
				SDS sds = RandomSDS(ctx);
				var s = new SerialSDS( sds, Simulation.ID.XYZ );
				string json = serializer.Serialize(s);
				var reverse = serializer.Deserialize<SerialSDS>(json);
				Assert.AreEqual(s, reverse);
				SDS rev2 = reverse.Deserialize();
				Assert.IsTrue(sds.ICMessagesAndEntitiesAreEqual(rev2));
			}
		}


	}
}
