using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard.Tests
{
	using VectorMath;
	using Shard;
	using System;
	using Microsoft.VisualStudio.TestTools.UnitTesting;


	[TestClass()]
	public class StupidModel
	{

		static IEnumerable<Entity> MakeGrid2D(int horizontalResolution)
		{
			EntityRandom random = new EntityRandom(1024);
			for (int x = 0; x < horizontalResolution; x++)
				for (int y = 0; y < horizontalResolution; y++)

					yield return new Entity(
							new EntityID(Guid.NewGuid(), Simulation.MySpace.DeRelativate(new Vec3(0.5f + x, 0.5f + y, 0) / horizontalResolution)),
							new Habitat(random),   //check that this doesn't actually cause a fault (should get clamped)
							null);
		}

		[TestMethod()]
		public void StupidModelTest2D()
		{
			int gridRes = 100;   //2d resolution
								//each grid cell can 'see' +- 4 cells in all direction. All 'motion' is done via communication
								//hence R = 4 / gridRes
			float r = 4.5f / gridRes;

			DB.ConfigContainer config = new DB.ConfigContainer() { extent = new ShardID(new Int3(1), 1), r = r, m = r*0.5f };
			Simulation.Configure(new ShardID(Int3.Zero, 0), config, true);
			Vec3 outlierCoords = Simulation.MySpace.Min;

			SDS.IntermediateData intermediate0 = new SDS.IntermediateData();
			intermediate0.entities = new EntityPool(MakeGrid2D(gridRes));
			//EntityTest.RandomDefaultPool(100);
			intermediate0.ic = InconsistencyCoverage.NewCommon();
			intermediate0.inputConsistent = true;
			intermediate0.localChangeSet = new EntityChangeSet();

			SDS root = new SDS(0, intermediate0.entities.ToArray(), intermediate0.ic, intermediate0, null, null);
			Assert.IsTrue(root.IsFullyConsistent);

			SDSStack stack = Simulation.Stack;
			stack.ResetToRoot(root);

			for (int i = 0; i < 13; i++)
			{
				Assert.IsNotNull(stack.NewestSDS.FinalEntities,i.ToString());
				SDS temp = stack.AllocateGeneration(i+1);
				SDS.Computation comp = new SDS.Computation(i+1, null, TimeSpan.FromMilliseconds(10));
				ComputationTests.AssertNoErrors(comp, "comp");
				Assert.IsTrue(comp.Intermediate.inputConsistent);

				SDS sds = comp.Complete();
				stack.Insert(sds);
				Assert.IsTrue(sds.IsFullyConsistent);

				Assert.AreEqual(sds.FinalEntities.Length, gridRes * gridRes);

				int numBugs = 0;
				int numPredators = 0;
				int numConflicts = 0;
				float totalFood = 0;
				foreach (var e in sds.FinalEntities)
				{
					Habitat h = (Habitat)Helper.Deserialize(e.SerialLogicState);
					if (h.bug.HasAnimal)
						numBugs++;
					if (h.predator.HasAnimal)
					{
						numPredators++;
						if (h.bug.HasAnimal)
							numConflicts++;
					}
					totalFood += h.food;
				}

				Console.WriteLine("Population: b=" + numBugs + ", p=" + numPredators + ", c=" + numConflicts+"; Food="+totalFood);

			}
		}

	}
}

