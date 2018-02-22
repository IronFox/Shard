using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard.Tests
{

	[TestClass()]
	public class EntityPoolTests
	{
		static Random random = new Random();

		[TestMethod()]
		public void AddToTest()
		{
			var ctx = new SimulationContext(true);

			Random random = new Random();
			List<Entity> testEntities = CreateEntities(100);

			EntityPool original = new EntityPool(ctx);
			foreach (var e in testEntities)
				Assert.IsTrue(original.Insert(e));
			original.VerifyIntegrity();

			var h0 = original.HashDigest;

			for (int i = 0; i < 100; i++)
			{
				EntityPool compare = new EntityPool(ctx);
				random.Shuffle(testEntities);
				foreach (var e in testEntities)
					Assert.IsTrue(compare.Insert(e));
				compare.VerifyIntegrity();
				var h1 = compare.HashDigest;
				Assert.AreEqual(h0, h1);
			}


		}

		public static List<Entity> CreateEntities(int count, Func<int,EntityLogic> logicFactory = null)
		{
			var rs = new List<Entity>();
			for (int i = 0; i < count; i++)
				rs.Add(new Entity(EntityChangeSetTests.RandomID(), Vec3.Zero, logicFactory != null ? logicFactory(i) : null));
			return rs;
		}

		public static EntityPool RandomPool(int numEntities, EntityChange.ExecutionContext ctx)
		{
			var rs = new EntityPool(ctx);
			var entities = CreateEntities(numEntities);
			foreach (var e in entities)
				Assert.IsTrue(rs.Insert(e));
			return rs;
		}

		[TestMethod()]
		public void UpdateEntityTest()
		{
			var ctx = new SimulationContext(true);
			Random random = new Random();
			EntityPool pool = new EntityPool(ctx);
			var entities = CreateEntities(3);
			foreach (var e in entities)
				Assert.IsTrue(pool.Insert(e));

			for (int i = 0; i < 10; i++)
			{
				Entity old = entities[0];
				Entity moved = Relocate(old);
				Assert.IsTrue(pool.Contains(old.ID));
				Assert.IsTrue(pool.UpdateEntity(entities[0], moved),"Update moved entity "+i);
				Assert.IsFalse(pool.Contains(old.ID));
				entities[0] = moved;
				Assert.AreEqual(pool.Count, entities.Count);
				foreach (var e in entities)
				{
					Assert.IsTrue(pool.Contains(e.ID.Guid));
					Assert.IsTrue(pool.Contains(e.ID));
				}
			}

		}

		private Entity Relocate(Entity entity)
		{
			EntityID rs;
			do
			{
				rs = entity.ID.Relocate(random.NextVec3(Simulation.FullSimulationSpace));
			}
			while (rs == entity.ID);

			return new Entity(rs, rs.Position - entity.ID.Position, entity.MyLogic
#if STATE_ADV
				, entity.Appearances
#endif
				);
		}
	}
}