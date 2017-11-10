using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;

namespace ShardTests1
{
	[TestClass]
	public class EntityTest
	{
		static Random random = new Random();

		private class LogicState : EntityLogic.State
		{
			public override byte[] BinaryState => null;

			public override string LogicID => "EntityTest.Logic";

			public override Changes Evolve(Entity currentState)
			{
				throw new NotImplementedException();
			}
		}

		[TestMethod]
		public void EntityEvolveTest()
		{
			Entity e = new Entity(new EntityID(Guid.NewGuid(), random.NextVec3(Simulation.Space)), new LogicState(), new EntityAppearance(), null, null);

			for (int i = 0; i < 100; i++)
			{
				EntityChangeSet set = new EntityChangeSet(i);
				e.Evolve(set);

				
			}

		}
	}
}
