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

		[Serializable]
		class ExceedingMovementLogic : EntityLogic
		{

			public override int CompareTo(EntityLogic other)
			{
				return 0;
			}

			public override Changes Evolve(Entity currentState, int generation, Random randomSource)
			{
				Changes rs = new Changes();
				rs.newPosition = currentState.ID.Position + new Vec3(Simulation.R);
				rs.newState = this;
				return rs;
			}

			public override void Hash(Hasher h)
			{
				h.Add(GetType());
			}
		}
		
		[Serializable]
		class MovingLogic : EntityLogic
		{
			public readonly Vec3 Motion;
			
			public MovingLogic(Vec3 direction)
			{
				Motion = direction / direction.Length * Simulation.M;
				Assert.IsTrue(Simulation.GetDistance(Motion, Vec3.Zero) <= Simulation.M);
			}

			public override Changes Evolve(Entity currentState, int generation, Random randomSource)
			{
				Changes rs = new Changes();
				rs.newPosition = currentState.ID.Position + Motion;
				rs.newState = this;
				return rs;
			}

			public override void Hash(Hasher h)
			{
				h.Add(GetType());
				h.Add(Motion);
			}

			public override int CompareTo(EntityLogic cmp)
			{
				MovingLogic other = cmp as MovingLogic;
				if (other == null)
					return -1;
				return Motion.CompareTo(other.Motion);
			}
		}

		[Serializable]
		class StationaryLogic : EntityLogic
		{

			public override int CompareTo(EntityLogic other)
			{
				return 0;
			}

			public override Changes Evolve(Entity currentState, int generation, Random randomSource)
			{
				Changes rs = new Changes();
				rs.newState = this;
				return rs;
			}

			public override void Hash(Hasher h)
			{
				h.Add(GetType());
			}
		}


		[TestMethod()]
		public void NestedComputationTest()
		{
			int numRCS = 0;


			DB.ConfigContainer config = new DB.ConfigContainer() { extent = new ShardID(new Int3(3), 1), r = 1f / 8, m = 1f / 16 };
			Simulation.Configure(new ShardID(Int3.One, 0), config,true);
			Vec3 outlierCoords = Simulation.MySpace.Min;

			var crossingLogic = new MovingLogic(new Vec3(-1, 0, 0));
			Entity crosser = new Entity(new EntityID(Guid.NewGuid(), Simulation.MySpace.Min), crossingLogic, null, null, null);
			Vec3 crossingTarget = crosser.ID.Position + crossingLogic.Motion;

			foreach (var n in Simulation.Neighbors)
				n.OutStack.OnPutRCS = (rcs,gen) =>
				{
					numRCS++;

					Assert.AreEqual(gen, 1);


					RCS decoded = new RCS(rcs);

					Assert.IsTrue(decoded.IsFullyConsistent);
					//RCS.GenID id = new RCS.GenID(rcs.NumericID,0);
					//Link lnk = Simulation.Neighbors.Find(id.ID.ToShard);
					//Assert.IsNotNull(lnk);

					if (n.WorldSpace.Grow(Simulation.SensorRange).Contains(outlierCoords))
						Assert.IsFalse(decoded.CS.IsEmpty);
					else
						Assert.IsTrue(decoded.CS.IsEmpty);

					if (n.WorldSpace.Contains(crossingTarget))
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

			SDS root = new SDS( 0, intermediate.entities.ToArray(), intermediate.ic, intermediate, null);
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
				int expected = p.Key == "motions" || p.Key == "advertisements" ? 3 : 0;
				Assert.AreEqual(expected, p.Value.Size);
			}

			Assert.AreEqual(comp.Generation, 1);
			Assert.AreEqual(Simulation.NeighborCount, numRCS);
			//comp.

			{
				Link inbound = Simulation.Neighbors.Find(new Int3(0,1,1));
				RCS inRCS = new RCS(new EntityChangeSet(), new InconsistencyCoverage(inbound.ICExportRegion.Size));
				temp.FetchNeighborUpdate(inbound, inRCS.Export());
			}

			SDS sds = comp.Complete();

			Assert.IsTrue(sds.Intermediate == comp.Intermediate);

			//check if most outer cells are 1 (one full-edge incoming RCS):
			var core = sds.IC.Sub(Int3.One, sds.IC.Size - 2);
			int edgeSize = InconsistencyCoverage.CommonResolution - 2;
			Assert.AreEqual(sds.IC.OneCount , sds.IC.Size.Product - core.Size.Product - edgeSize * edgeSize,edgeSize.ToString());
			Assert.IsTrue(sds.IC.OneCount > 0);
			Assert.IsTrue(core.OneCount == 0);


			Assert.AreEqual(sds.Generation, 1);
			Assert.AreEqual(sds.FinalEntities.Length, 2);
			Assert.IsFalse(sds.HasEntity(crosser.ID.Guid));

			var check = sds.CheckMissingRCS();
			Assert.IsFalse(check.AllThere);
			Assert.IsFalse(check.AnyAvailableFromNeighbors);
			Assert.AreEqual(check.missingRCS, numRCS-1);
			Assert.IsTrue(check.predecessorIsConsistent);
			Assert.AreEqual(check.rcsAvailableFromNeighbor, 0);
			Assert.AreEqual(check.rcsRestoredFromDB, 0);


		}



		[TestMethod()]
		public void IsolatedComputationTest()
		{

			DB.ConfigContainer config = new DB.ConfigContainer() { extent = new ShardID(new Int3(1), 1), r = 1f / 8, m = 1f / 16 };
			Simulation.Configure(new ShardID(Int3.Zero, 0), config,true);
			Vec3 outlierCoords = Simulation.MySpace.Min;

			foreach (var n in Simulation.Neighbors)
				n.OutStack.OnPutRCS = (rcs, gen) =>
				{
					Assert.Fail("This test generates no simulation neighbors. Should not generate RCSs");
				};

			DB.OnPutSDS = dbSDS =>
			{
				Assert.AreEqual(Entity.Import( dbSDS.SerialEntities).Length, 2);
				Assert.AreEqual(dbSDS.Generation, 1);
				Assert.AreEqual(new InconsistencyCoverage(dbSDS.IC).OneCount, 0);
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
				}
			);
			//EntityTest.RandomDefaultPool(100);
			intermediate.ic = InconsistencyCoverage.NewCommon();
			intermediate.inputConsistent = true;
			intermediate.localChangeSet = new EntityChangeSet();

			SDS root = new SDS( 0, intermediate.entities.ToArray(), intermediate.ic, intermediate, null);
			Assert.IsTrue(root.IsFullyConsistent);

			SDSStack stack = Simulation.Stack;
			stack.Insert(root);
			SDS temp = stack.AllocateGeneration(1);
			Assert.AreEqual(temp.Generation, 1);
			Assert.IsNotNull(stack.FindGeneration(1));
			SDS.Computation comp = new SDS.Computation(1);
			Assert.AreEqual(comp.Intermediate.entities.Count, 2);
			Assert.AreEqual(comp.Intermediate.ic.OneCount, 0);
			Assert.IsTrue(comp.Intermediate.inputConsistent);

			foreach (var p in comp.Intermediate.localChangeSet.NamedSets)
			{
				int expected = p.Key == "motions" || p.Key == "advertisements" ? 2 : 0;
				Assert.AreEqual(expected, p.Value.Size);
			}

			Assert.AreEqual(comp.Generation, 1);


			SDS sds = comp.Complete();
			Assert.IsTrue(sds.Intermediate == comp.Intermediate);

			Assert.IsTrue(sds.IsFullyConsistent);
			Assert.IsTrue(sds.IC.OneCount == 0);
			Assert.AreEqual(sds.Generation, 1);
			Assert.AreEqual(sds.FinalEntities.Length, 2);
		}


	}
}