using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShardTests1;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard.Tests
{
	[TestClass()]
	public class ComputationTests
	{
		static Random random = new Random();


		class ExceedingMovementLogic : EntityLogic.State
		{
			public override byte[] BinaryState => null;

			public override string LogicID => "ExceedingMovement.Logic";

			public override Changes Evolve(Entity currentState, int generation, Random randomSource)
			{
				Changes rs = new Changes();
				rs.newPosition = currentState.ID.Position + new Vec3(Simulation.R);
				rs.newState = this;
				return rs;
			}
		}

		class MovingLogic : EntityLogic.State
		{
			public override byte[] BinaryState => null;

			public readonly Vec3 Motion;
			
			public MovingLogic(Vec3 direction)
			{
				Motion = direction / direction.Length * Simulation.M;
				Assert.IsTrue(Simulation.GetDistance(Motion, Vec3.Zero) <= Simulation.M);
			}
			public override string LogicID => "Moving.Logic";

			public override Changes Evolve(Entity currentState, int generation, Random randomSource)
			{
				Changes rs = new Changes();
				rs.newPosition = currentState.ID.Position + Motion;
				rs.newState = this;
				return rs;
			}
		}

		class StationaryLogic : EntityLogic.State
		{
			public override byte[] BinaryState => null;

			public override string LogicID => "Stationary.Logic";

			public override Changes Evolve(Entity currentState, int generation, Random randomSource)
			{
				Changes rs = new Changes();
				rs.newState = this;
				return rs;
			}
		}


		[TestMethod()]
		public void ComputationTest()
		{
			int numRCS = 0;


			DB.ConfigContainer config = new DB.ConfigContainer() { extent = new ShardID(new Int3(3), 1), r = 1f / 8, m = 1f / 16 };
			Simulation.Configure(new ShardID(Int3.One, 0), config);
			Vec3 outlierCoords = Simulation.MySpace.Min;

			var crossingLogic = new MovingLogic(new Vec3(-1, 0, 0));
			Entity crosser = new Entity(new EntityID(Guid.NewGuid(), Simulation.MySpace.Min), crossingLogic, null, null, null);
			Vec3 crossingTarget = crosser.ID.Position + crossingLogic.Motion;

			DB.OnPutRCS = rcs =>
			{
				numRCS++;

				Assert.AreEqual(rcs.Generation, 1);


				RCS decoded = new RCS(rcs);

				Assert.IsTrue(decoded.IsFullyConsistent);
				Assert.IsNotNull(rcs.NumericID);
				RCS.GenID id = new RCS.GenID(rcs.NumericID,0);
				Link lnk = Simulation.Neighbors.Find(id.ID.ToShard);
				Assert.IsNotNull(lnk);

				if (lnk.WorldSpace.Grow(Simulation.SensorRange).Contains(outlierCoords))
					Assert.IsFalse(decoded.CS.IsEmpty);
				else
					Assert.IsTrue(decoded.CS.IsEmpty);

				if (lnk.WorldSpace.Contains(crossingTarget))
					Assert.IsNotNull(decoded.CS.FindMotionOf(crosser.ID.Guid));
			};

			SDS.IntermediateData intermediate = new SDS.IntermediateData();
			intermediate.entities = new EntityPool(
				new Entity[] 
				{
					new Entity(
						new EntityID(Guid.NewGuid(), Simulation.MySpace.Center),
						new ExceedingMovementLogic(),
						//new EntityTest.FaultLogic.State(),
						null,null,null),

					new Entity(
						new EntityID(Guid.NewGuid(), outlierCoords),
						new StationaryLogic(),
						//new EntityTest.FaultLogic.State(),
						null,null,null),
					crosser
				}
			);
			//EntityTest.RandomDefaultPool(100);
			intermediate.ic = InconsistencyCoverage.NewCommon();
			intermediate.inputConsistent = true;
			intermediate.localChangeSet = new EntityChangeSet();

			SDS root = new SDS(null, 0, intermediate.entities.ToArray(), intermediate.ic, intermediate, null);
			Assert.IsTrue(root.IsFullyConsistent);

			SDSStack stack = Simulation.Stack;
			stack.Insert(root);
			SDS temp = stack.AllocateGeneration(1);
			Assert.AreEqual(temp.Generation, 1);
			Assert.IsNotNull(stack.FindGeneration(1));
			SDS.Computation comp = new SDS.Computation(1);
			Assert.AreEqual(comp.Intermediate.entities.Count, 3);
			Assert.AreEqual(comp.Intermediate.ic.OneCount, 0);
			Assert.IsTrue(comp.Intermediate.inputConsistent);

			foreach (var p in comp.Intermediate.localChangeSet.NamedSets)
			{
				int expected = p.Key == "motions" ? 2 : (p.Key == "advertisements" ? 3 : 0 );
				Assert.AreEqual(expected, p.Value.Size);
			}

			Assert.AreEqual(comp.Generation, 1);
			Assert.AreEqual(Simulation.NeighborCount, numRCS);
			//comp.

		}

		[TestMethod()]
		public void CompleteTest()
		{
			Assert.Fail();
		}
	}
}