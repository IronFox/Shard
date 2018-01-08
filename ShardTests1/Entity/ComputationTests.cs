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

		/// <summary>
		/// Logic that intentionally moves too fast, thus triggering forceful clamping. Should not cause a fault
		/// </summary>
		[Serializable]
		class ExceedingMovementLogic : EntityLogic
		{
			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource)
			{
				newState.NewPosition = currentState.ID.Position + new Vec3(Simulation.R);
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

			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource)
			{
				newState.NewPosition = currentState.ID.Position + Motion;
			}
		}

		[Serializable]
		class StationaryLogic : EntityLogic
		{
			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource)
			{}
		}

		[Serializable]
		public class PingPacket
		{
			public readonly int Counter;

			public PingPacket(int counter)
			{
				this.Counter = counter;
			}

			public PingPacket Increment()
			{
				return new PingPacket(Counter + 1);
			}
		}


		[Serializable]
		class RoundState : EntityLogic
		{
			public int state = 0;
			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource)
			{
				state++;
			}
		}


		[Serializable]
		class PingLogic : EntityLogic
		{
			PingPacket p;
			bool amPong = false;

			public int CounterState { get { return p.Counter; } }

			public PingLogic(PingPacket packet, bool amPong)
			{
				p = packet;
				this.amPong = amPong;
			}
			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource)
			{
				if (!amPong)
				{
					newState.Broadcast(0,Helper.SerializeToArray(p.Increment()));
					amPong = true;

					p = p.Increment();
				}
				else
				{
					PingPacket data = null;
					if (currentState.InboundMessages != null)
						foreach (var m in currentState.InboundMessages)
						{
							if (m.Sender == currentState.ID)
								continue;
							data = Helper.Deserialize(m.Payload) as PingPacket;
							if (data != null)
								break;
						}
					if (data == null)
						return;
					newState.Broadcast(0,Helper.SerializeToArray(data.Increment()));
					p = data;

				}
			}
		}


		public static void AssertNoErrors(SDS.Computation comp, string task)
		{
			var errors = comp.Errors;
			if (errors == null)
				return;

			//Exception ex = errors[0];
			//while (ex.InnerException != null && !(ex is ExecutionException))
			//	ex = ex.InnerException;

			Assert.Fail(task+": "+errors[0].ToString());
		}


		[TestMethod()]
		public void PingPongTest()
		{
			DB.ConfigContainer config = new DB.ConfigContainer() { extent = new ShardID(new Int3(1), 1), r = 1f / 8, m = 1f / 16 };
			Simulation.Configure(new ShardID(Int3.Zero, 0), config, true);

			SDS.IntermediateData intermediate = new SDS.IntermediateData();
			intermediate.entities = new EntityPool(
				new Entity[]
				{
					new Entity(
						new EntityID(Guid.NewGuid(), Simulation.MySpace.Center),
						new PingLogic(new PingPacket(0),false),	//check that this doesn't actually cause a fault (should get clamped)
						null),

					new Entity(
						new EntityID(Guid.NewGuid(), Simulation.MySpace.Center + new Vec3(Simulation.R)),
						new PingLogic(new PingPacket(0),true),
						//new EntityTest.FaultLogic.State(),
						null),
				}
			);
			//EntityTest.RandomDefaultPool(100);
			intermediate.ic = InconsistencyCoverage.NewCommon();
			intermediate.inputConsistent = true;
			intermediate.localChangeSet = new EntityChangeSet();

			SDS root = new SDS(0, intermediate.entities.ToArray(), intermediate.ic, intermediate, null,null);
			Assert.IsTrue(root.IsFullyConsistent);

			SDSStack stack = Simulation.Stack;
			stack.ResetToRoot(root);

			const int NumIterations = 10;

			for (int i = 0; i < NumIterations; i++)
			{
				SDS temp = stack.AllocateGeneration(i + 1);
				Assert.AreEqual(temp.Generation, i + 1);
				Assert.IsNotNull(stack.FindGeneration(i + 1));
				SDS.Computation comp = new SDS.Computation(i + 1, new DateTime(), null, TimeSpan.FromMilliseconds(10));
				AssertNoErrors(comp,i.ToString());
				Assert.AreEqual(comp.Intermediate.entities.Count, 2);
				Assert.AreEqual(comp.Intermediate.ic.OneCount, 0);
				Assert.IsTrue(comp.Intermediate.inputConsistent);
				Assert.AreEqual(comp.Generation, i + 1);
				SDS sds = comp.Complete();
				Assert.IsTrue(sds.Intermediate == comp.Intermediate);
				Assert.IsTrue(sds.IsFullyConsistent);
				Assert.IsTrue(sds.IC.OneCount == 0);
				Assert.AreEqual(sds.Generation, i+1);
				Assert.AreEqual(sds.FinalEntities.Length, 2);
				stack.Insert(sds);
			}
			Assert.AreEqual(1, stack.Size);
			int sum = 0;
			foreach (var e in stack.Last().FinalEntities)
			{
				var state = Helper.Deserialize(e.SerialLogicState) as PingLogic;
				if (state != null)
				{
					int cs = state.CounterState;
					Assert.IsTrue(cs == NumIterations-2 || cs == NumIterations-1, cs.ToString());
					sum += cs;	//one should be at counter 8, one at 9
				}
			}
			Assert.AreEqual(NumIterations*2-3, sum);
		}


		[TestMethod()]
		public void ConsistentStateTest()
		{
			DB.ConfigContainer config = new DB.ConfigContainer() { extent = new ShardID(new Int3(1), 1), r = 1f / 8, m = 1f / 16 };
			Simulation.Configure(new ShardID(Int3.Zero, 0), config, true);

			SDS.IntermediateData intermediate = new SDS.IntermediateData();
			intermediate.entities = new EntityPool(
				new Entity[]
				{
					new Entity(
						new EntityID(Guid.NewGuid(), Simulation.MySpace.Center),
						new RoundState(),
						null),
				}
			);
			//EntityTest.RandomDefaultPool(100);
			intermediate.ic = InconsistencyCoverage.NewCommon();
			intermediate.inputConsistent = true;
			intermediate.localChangeSet = new EntityChangeSet();

			SDS root = new SDS(0, intermediate.entities.ToArray(), intermediate.ic, intermediate, null, null);
			Assert.IsTrue(root.IsFullyConsistent);

			SDSStack stack = Simulation.Stack;
			stack.ResetToRoot(root);

			foreach (var s in stack)
			{
				foreach (var e in s.FinalEntities)
				{
					RoundState st = e.MyLogic as RoundState;
					Assert.IsNotNull(st);
					Assert.AreEqual(s.Generation, st.state, "-1: " + s.Generation);
				}
			}


			const int NumIterations = 10;

			StringBuilder ks = new StringBuilder();
			for (int i = 0; i < NumIterations; i++)
			{
				{
					SDS temp = stack.AllocateGeneration(i + 1);
					SDS.Computation comp = new SDS.Computation(i + 1, new DateTime(), null, TimeSpan.FromMilliseconds(10));
					AssertNoErrors(comp, i.ToString()+".evolve ("+ks+")");
					SDS sds = comp.Complete();
					stack.Insert(sds, false);
				}
				if (i > 1)
				{
					int k = random.Next(1, i - 1);
					ks.Append(',').Append(k);
					Console.WriteLine(k);
					SDS.Computation comp = new SDS.Computation(k, new DateTime(), null, TimeSpan.FromMilliseconds(10));
					AssertNoErrors(comp, i + ".revisit (" + ks + ")");
					SDS sds = comp.Complete();
					stack.Insert(sds, false);
				}

				foreach (var s in stack)
				{
					foreach (var e in s.FinalEntities)
					{
						RoundState st = e.MyLogic as RoundState;
						Assert.IsNotNull(st);
						Assert.AreEqual(s.Generation, st.state,i+": "+s.Generation);
					}
				}

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
			Entity crosser = new Entity(new EntityID(Guid.NewGuid(), Simulation.MySpace.Min), crossingLogic, null);
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
						null),

					new Entity(
						new EntityID(Guid.NewGuid(), outlierCoords),
						new StationaryLogic(),
						//new EntityTest.FaultLogic.State(),
						null),
					crosser
				}
			);
			//EntityTest.RandomDefaultPool(100);
			intermediate.ic = InconsistencyCoverage.NewCommon();
			intermediate.inputConsistent = true;
			intermediate.localChangeSet = new EntityChangeSet();

			SDS root = new SDS( 0, intermediate.entities.ToArray(), intermediate.ic, intermediate, null, null);
			Assert.IsTrue(root.IsFullyConsistent);

			SDSStack stack = Simulation.Stack;
			stack.ResetToRoot(root);
			SDS temp = stack.AllocateGeneration(1);
			Assert.AreEqual(temp.Generation, 1);
			Assert.IsNotNull(stack.FindGeneration(1));
			SDS.Computation comp = new SDS.Computation(1, new DateTime(), null, TimeSpan.FromMilliseconds(10));
			AssertNoErrors(comp, "comp");
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
						new ExceedingMovementLogic(),	//check that this doesn't actually cause a fault (should get clamped)
						null),

					new Entity(
						new EntityID(Guid.NewGuid(), outlierCoords),
						new StationaryLogic(),
						//new EntityTest.FaultLogic.State(),
						null),
				}
			);
			//EntityTest.RandomDefaultPool(100);
			intermediate.ic = InconsistencyCoverage.NewCommon();
			intermediate.inputConsistent = true;
			intermediate.localChangeSet = new EntityChangeSet();

			SDS root = new SDS( 0, intermediate.entities.ToArray(), intermediate.ic, intermediate, null, null);
			Assert.IsTrue(root.IsFullyConsistent);

			SDSStack stack = Simulation.Stack;
			stack.ResetToRoot(root);
			SDS temp = stack.AllocateGeneration(1);
			Assert.AreEqual(temp.Generation, 1);
			Assert.IsNotNull(stack.FindGeneration(1));
			SDS.Computation comp = new SDS.Computation(1, new DateTime(), null, TimeSpan.FromMilliseconds(10));
			AssertNoErrors(comp, "comp");
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