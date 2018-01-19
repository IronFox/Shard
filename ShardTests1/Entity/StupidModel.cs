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
	using static Shard.Tests.ComputationTests;

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

			SimulationRun run = new SimulationRun(
				new DB.ConfigContainer() { extent = new ShardID(new Int3(1), 1), r = r, m = r * 0.5f },
				new ShardID(Int3.Zero, 0),
				MakeGrid2D(gridRes));



			for (int i = 0; i < 13; i++)
			{
				var sds = run.AdvanceTLG(true, true);

				Assert.AreEqual(sds.SDS.FinalEntities.Length, gridRes * gridRes);

				int numBugs = 0;
				int numPredators = 0;
				int numConflicts = 0;
				float totalFood = 0;
				foreach (var e in sds.SDS.FinalEntities)
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

