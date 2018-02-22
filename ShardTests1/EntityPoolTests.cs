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


		/// <summary>
		/// Tests whether conflicting motions result in the same final motion, regardless of order
		/// </summary>
		[TestMethod()]
		public void MotionConflictOrderingTest()
		{
			var ctx = new SimulationContext(true);

			for (int i = 0; i < 10; i++)
			{
				var entities = CreateEntities(1);
				var e = entities[0];
				Vec3 original = e.ID.Position;

				EntityChange.Motion[] motions = new EntityChange.Motion[10];
				for (int j = 0; j < motions.Length; j++)
					motions[j] = new EntityChange.Motion(new EntityID(e.ID.Guid, random.NextVec3(0, 1)), ctx.LocalSpace.Clamp( original + random.NextVec3(-ctx.Ranges.R,ctx.Ranges.R) ), e.MyLogic, null);

				Entity e0 = null;

				for (int j = 0; j < motions.Length; j++)
				{
					var motions2 = motions.OrderBy(x => random.Next()).ToArray();
					EntityPool pool = new EntityPool(entities, ctx);
					EntityChangeSet set = new EntityChangeSet();
					foreach (var m in motions2)
						set.Add(m);

					int errors = set.Execute(pool, InconsistencyCoverage.NewAllOne(), ctx);
					Assert.AreEqual(motions.Length - 1, errors);	//motions-1 get rejected, one is accepted. must always be the same

					var e1 = pool.First();
					Assert.AreNotEqual(e1, e);	//must have moved
					if (e0 == null)
						e0 = e1;
					else
						Assert.AreEqual(e0, e1); //must have moved to the same location
				}

			}
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