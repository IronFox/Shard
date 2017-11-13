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
	public class EntityPoolTests
	{
		[TestMethod()]
		public void AddToTest()
		{
			Random random = new Random();
			List<Entity> testEntities = CreateEntities(100, random);

			Hasher h0 = new Hasher();
			EntityPool original = new EntityPool();
			foreach (var e in testEntities)
				Assert.IsTrue(original.Insert(e));
			original.VerifyIntegrity();
			h0.Add(original);
			var hash0 = h0.Finish();

			for (int i = 0; i < 100; i++)
			{
				EntityPool compare = new EntityPool();
				random.Shuffle(testEntities);
				foreach (var e in testEntities)
					Assert.IsTrue(compare.Insert(e));
				compare.VerifyIntegrity();
				Hasher h1 = new Hasher();
				h1.Add(compare);
				var hash1 = h1.Finish();
				Assert.AreEqual(hash0, hash1);
			}


		}

		public static List<Entity> CreateEntities(int count, Random random, EntityLogic logic = null)
		{
			var rs = new List<Entity>();
			for (int i = 0; i < count; i++)
				rs.Add(new Entity(new EntityID(Guid.NewGuid(), random.NextVec3(Simulation.MySpace)), logic != null ? logic.Instantiate(null) : null, null, null, null));
			return rs;
		}

		[TestMethod()]
		public void UpdateEntityTest()
		{
			Random random = new Random();
			EntityPool pool = new EntityPool();
			var entities = CreateEntities(3, random);
			foreach (var e in entities)
				Assert.IsTrue(pool.Insert(e));

			for (int i = 0; i < 10; i++)
			{
				Entity old = entities[0];
				Entity moved = new Entity(old.ID.Relocate( random.NextVec3(0, 1000)), old.LogicState, old.Appearance, null, null);
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
	}
}