using Base;
using Consensus;
using DBType;
//using Consensus;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShardTests1;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges, bool isInconsistent)
			{
				newState.NewPosition = currentState.ID.Position + new Vec3(ranges.R);
			}
		}
		
		[Serializable]
		class MovingLogic : EntityLogic
		{
			public readonly Vec3 Motion;
			
			public MovingLogic(Vec3 direction)
			{
				Motion = direction / direction.Length * Simulation.Ranges.Motion;
				Assert.IsTrue(Vec3.GetChebyshevDistance(Motion, Vec3.Zero) <= Simulation.Ranges.Motion);
			}

			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges, bool isInconsistent)
			{
				newState.NewPosition = currentState.ID.Position + Motion;
			}
		}

		[Serializable]
		class StationaryLogic : EntityLogic
		{
			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges, bool isInconsistent)
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
			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges, bool isInconsistent)
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
			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges, bool isInconsistent)
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


		public static void AssertNoErrors(SDSComputation comp, string task)
		{
			var errors = comp.Errors;
			if (errors == null)
				return;

			//Exception ex = errors[0];
			//while (ex.InnerException != null && !(ex is ExecutionException))
			//	ex = ex.InnerException;

			Assert.Fail(task+": "+errors[0].ToString());
		}

		public class SimulationRun
		{
			SimulationContext ctx;
			public SDSStack stack;
			public Shard.MessageHistory messages;

			public SimulationRun(BaseDB.ConfigContainer config, ShardID localShardID, IEnumerable<Entity> entities, bool allLinksArePassive = true)
			{
				//BaseDB.ConfigContainer config = new BaseDB.ConfigContainer() { extent = Int3.One, r = 1f / 8, m = 1f / 16 };
				Simulation.Configure(localShardID, config, allLinksArePassive);
				ctx = new SimulationContext(config, Simulation.SDToBox(localShardID.XYZ, config.extent), allLinksArePassive);
				Notify = new MyNotify(this);

				FeedEntities(entities);
			}
			public SimulationRun(BaseDB.ConfigContainer config, ShardID localShardID, bool allLinksArePassive = true)
			{
				//BaseDB.ConfigContainer config = new BaseDB.ConfigContainer() { extent = Int3.One, r = 1f / 8, m = 1f / 16 };
				Simulation.Configure(localShardID, config, allLinksArePassive);
				ctx = new SimulationContext(config, Simulation.SDToBox(localShardID.XYZ, config.extent), allLinksArePassive);
				messages = new MessageHistory(0, new MessagePack[] { MessagePack.CompleteBlank });
				Notify = new MyNotify(this);
			}

			public void FeedEntities(IEnumerable<Entity> entities)
			{
				var intermediate = new IntermediateSDS();
				intermediate.entities = new EntityPool(entities, ctx);
				intermediate.ic = InconsistencyCoverage.NewCommon();
				intermediate.inputConsistent = true;
				intermediate.localChangeSet = new EntityChangeSet();

				var root =
					new SDSStack.Entry(
					new SDS(0, intermediate.entities.ToArray(), intermediate.ic),
					intermediate
					);
				Assert.IsTrue(root.IsFullyConsistent);
				stack = Simulation.Stack;
				stack.ResetToRoot(root);

				messages = new MessageHistory(0, new MessagePack[] { MessagePack.CompleteBlank  });
			}

			private SDSComputation tlgComp;
			private int tlgGen;
			public SDSStack.Entry tlgEntry;
			private Consensus.Interface[] consensus;

			//			public Action<Address,ClientMessage> 

			private class DummyNotify : Consensus.INotifiable
			{
				public void OnAddressMismatchConsensusLoss(Address locallyBound, Address globallyRegistered)
				{
					Assert.Fail("Configuration error. Mismatch between bound address "+locallyBound+" and public registration "+globallyRegistered);
				}

				public void OnGenerationEnd(int generation)
				{
					bool brk = true;
				}

				public void OnMessageCommit(Address clientAddress, ClientMessage message)
				{
					bool brk = true;

				}

				public void OnOutOfConfig(Configuration newConfig, Configuration.Member memberID)
				{
					Assert.Fail("Configuration error: Local member ID "+memberID+" not found in new configuration "+newConfig);
				}
			}
			private class MyNotify : Consensus.INotifiable
			{
				private SimulationRun simulationRun;

				public MyNotify(SimulationRun simulationRun)
				{
					this.simulationRun = simulationRun;
				}

				public void OnAddressMismatchConsensusLoss(Address locallyBound, Address globallyRegistered)
				{
					Assert.Fail("Configuration error. Mismatch between bound address " + locallyBound + " and public registration " + globallyRegistered);
				}
				public void OnOutOfConfig(Configuration newConfig, Configuration.Member memberID)
				{
					Assert.Fail("Configuration error: Local member ID " + memberID + " not found in new configuration " + newConfig);
				}

				public void OnGenerationEnd(int generation)
				{
					simulationRun.messages.EndGeneration(generation);
				}

				public void OnMessageCommit(Address clientAddress, ClientMessage message)
				{
					if (!clientAddress.IsEmpty)
						InteractionLink.OnMessageCommit(clientAddress, message.ID);
					simulationRun.messages.Add(message);
				}
			}

			public readonly Consensus.INotifiable Notify;

			public SDSComputation BeginAdvanceTLG(bool intermediateShouldBeConsistent)
			{
				int i = tlgGen = stack.NewestRegisteredSDSGeneration;
				tlgEntry = stack.AllocateGeneration(i + 1);
				Assert.AreEqual(tlgEntry.Generation, i + 1);
				Assert.IsNotNull(stack.FindGeneration(i + 1));
				ctx.SetGeneration(i + 1);
				tlgComp = new SDSComputation(new DateTime(), consensus != null ? messages.GetMessages(i) : ExtMessagePack.CompleteBlank, TimeSpan.FromMilliseconds(10), ctx);
				if (intermediateShouldBeConsistent)
					AssertNoErrors(tlgComp, i.ToString());
				if (intermediateShouldBeConsistent)
				{
					Assert.AreEqual(tlgComp.Intermediate.ic.OneCount, 0);
					Assert.IsTrue(tlgComp.Intermediate.inputConsistent);
				}
				Assert.AreEqual(tlgComp.Generation, i + 1);
				if (consensus != null)
					consensus[0].TrimOut(stack.NewestConsistentSDSGeneration);
				messages.TrimGenerations(stack.NewestConsistentSDSGeneration);

				return tlgComp;
			}

			public SDSStack.Entry AdvanceTLG(bool shouldBeConsistent, bool intermediateShouldBeConsistent, bool trim = true, bool awaitConsensusInterfaceProgression = true)
			{
				if (consensus != null && awaitConsensusInterfaceProgression)
				{
					AwaitConsensus();
					var begin = DateTime.Now;
					while (messages.CurrentGeneration <= stack.NewestRegisteredSDSGeneration)
					{
						if (DateTime.Now - begin > TimeSpan.FromSeconds(8))
						{
							bool brk = true;
							begin = DateTime.Now;
						}
						Thread.Sleep(100);
					}
				}
				BeginAdvanceTLG(intermediateShouldBeConsistent);
				return CompleteAdvanceTLG(shouldBeConsistent, trim);
			}

			public SDSStack.Entry CompleteAdvanceTLG(bool shouldBeConsistent, bool trim = true)
			{
				var sds = tlgComp.Complete();
				Assert.IsTrue(sds.Item2 == tlgComp.Intermediate);
				if (shouldBeConsistent)
				{
					Assert.IsTrue(sds.Item1.IsFullyConsistent);
					Assert.IsTrue(sds.Item1.IC.OneCount == 0);
				}
				Assert.AreEqual(sds.Item1.Generation, tlgGen + 1);
				var rs = stack.Insert(sds, trim);

				if (trim)
					this.messages.TrimGenerations(stack.NewestConsistentSDSGeneration - 1);
				return rs;
			}

			public SDSStack.Entry RecomputeGeneration(int generation, bool trim = true)
			{
				ctx.SetGeneration(generation);
				var comp = new SDSComputation(new DateTime(), ExtMessagePack.CompleteBlank, TimeSpan.FromMilliseconds(10),ctx);
				AssertNoErrors(comp, "Recompute (" + generation + ")");
				var sds = comp.Complete();
				return stack.Insert(sds, trim);
			}

			internal void InstallConsensusCluster(int size, int basePort, bool awaitFormation)
			{
				BaseDB.OverrideAddressRequestFunction = addr => new FullShardAddress(addr, "localhost", 0, basePort + addr.ReplicaLevel);
				BaseDB.SDConfigPoller = new ReplicaOnlySD(size);
				var dummyNotify = new DummyNotify();
				consensus = new Consensus.Interface[size];
				Consensus.SharedDebugState state = new Consensus.SharedDebugState();
				for (int i = 0; i < size; i++)
				{
					consensus[i] =
						new Consensus.Interface(
							new Consensus.Configuration.Member(i, true),
							new Address(basePort + i),
							Int3.Zero, i > 0 ? dummyNotify : Notify)
						{
							DebugState = state
						};
				}


				if (awaitFormation)
					AwaitConsensus();

			}

			private void AwaitConsensus()
			{
				while (!consensus.All(i => i.IsFullyConnected) || consensus.Count(i => i.IsLeader) != 1 || consensus.Count(i => i.KnowsRemoteLeader) != consensus.Length - 1)
				{
					Thread.Sleep(100);
				}
			}

			internal void RelayMessageToConsensus(ClientMessage msg, Address sender)
			{
				if (consensus != null)
					consensus[0].Dispatch(msg, sender);
			}
		}


		[TestMethod()]
		public void PingPongTest()
		{
			SimulationRun run = new SimulationRun(
				new BaseDB.ConfigContainer() { extent = Int3.One, r = 1f / 8, m = 1f / 16 },
				new ShardID(Int3.Zero, 0), 
				new Entity[]
				{
					new Entity(
						new EntityID(Guid.NewGuid(), Simulation.MySpace.Center),
						Vec3.Zero,
						new PingLogic(new PingPacket(0),false),	//check that this doesn't actually cause a fault (should get clamped)
						null),

					new Entity(
						new EntityID(Guid.NewGuid(), Simulation.MySpace.Center + new Vec3(Simulation.Ranges.R)),
						Vec3.Zero,
						new PingLogic(new PingPacket(0),true),
						//new EntityTest.FaultLogic.State(),
						null),
				}
			);


			//EntityTest.RandomDefaultPool(100);



			const int NumIterations = 10;

			for (int i = 0; i < NumIterations; i++)
			{
				var rs = run.AdvanceTLG(true,true);
				Assert.AreEqual(rs.SDS.FinalEntities.Length, 2);

			}
			Assert.AreEqual(1, run.stack.Size);
			int sum = 0;
			foreach (var e in run.stack.Last().SDS.FinalEntities)
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
			SimulationRun run = new SimulationRun(
				new BaseDB.ConfigContainer() { extent = Int3.One, r = 1f / 8, m = 1f / 16 },
				new ShardID(Int3.Zero, 0),
				new Entity[]
				{
					new Entity(
						new EntityID(Guid.NewGuid(), Simulation.MySpace.Center),
						Vec3.Zero,
						new RoundState(),
						null),
				}
			);


			foreach (var s in run.stack)
			{
				foreach (var e in s.SDS.FinalEntities)
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
				run.AdvanceTLG(true,true,false);
				if (i > 1)
				{
					int k = random.Next(1, i - 1);
					ks.Append(',').Append(k);
					Console.WriteLine(ks);
					run.RecomputeGeneration(k,false);
				}

				foreach (var s in run.stack)
				{
					foreach (var e in s.SDS.FinalEntities)
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


			SimulationRun run = new SimulationRun(
				new BaseDB.ConfigContainer() { extent = new Int3(3), r = 1f / 8, m = 1f / 16 },
				new ShardID(Int3.One, 0));

			Vec3 outlierCoords = Simulation.MySpace.Min;

			var crossingLogic = new MovingLogic(new Vec3(-1, 0, 0));
			Entity crosser = new Entity(new EntityID(Guid.NewGuid(), Simulation.MySpace.Min), Vec3.Zero, crossingLogic, null);
			Vec3 crossingTarget = crosser.ID.Position + crossingLogic.Motion;

			foreach (var n in Simulation.Neighbors)
				n.OnPutRCS = (decoded, gen) =>
				{
					numRCS++;

					Assert.AreEqual(gen, 1);


					//RCS decoded = new RCS(rcs);

					Assert.IsTrue(decoded.IsFullyConsistent);
					//RCS.GenID id = new RCS.GenID(rcs.NumericID,0);
					//Link lnk = Simulation.Neighbors.Find(id.ID.ToShard);
					//Assert.IsNotNull(lnk);

					if (n.WorldSpace.Grow(Simulation.Ranges.Transmission).Contains(outlierCoords))
						Assert.IsFalse(decoded.CS.IsEmpty);
					else
						Assert.IsTrue(decoded.CS.IsEmpty);

					if (n.WorldSpace.Contains(crossingTarget))
						Assert.IsNotNull(decoded.CS.FindMotionOf(crosser.ID.Guid));
				};


			run.FeedEntities(
				new Entity[]
				{
					new Entity(
						new EntityID(Guid.NewGuid(), Simulation.MySpace.Center),
						Vec3.Zero, 
						new ExceedingMovementLogic(),
						//new EntityTest.FaultLogic.State(),
						null),

					new Entity(
						new EntityID(Guid.NewGuid(), outlierCoords),
						Vec3.Zero, 
						new StationaryLogic(),
						//new EntityTest.FaultLogic.State(),
						null),
					crosser
				}
			);



			var inter = run.BeginAdvanceTLG(true);

			foreach (var p in inter.Intermediate.localChangeSet.NamedSets)
			{
				int expected = p.Key == "motions" || p.Key == "advertisements" ? 3 : 0;
				Assert.AreEqual(expected, p.Value.Size);
			}

			Assert.AreEqual(inter.Generation, 1);
			Assert.AreEqual(Simulation.NeighborCount, numRCS);
			//comp.

			{
				Link inbound = Simulation.Neighbors.Find(new Int3(0,1,1));
				RCS inRCS = new RCS(new EntityChangeSet(), new InconsistencyCoverage(inbound.ICExportRegion.Size));
				Simulation.FetchNeighborUpdate(run.tlgEntry, inbound, inRCS.Export());
			}

			var rs = run.CompleteAdvanceTLG(false);

			//check if most outer cells are 1 (one full-edge incoming RCS):
			var core = rs.SDS.IC.Sub(Int3.One, rs.SDS.IC.Size - 2);
			int edgeSize = InconsistencyCoverage.CommonResolution - 2;
			Assert.AreEqual(rs.SDS.IC.OneCount , rs.SDS.IC.Size.Product - core.Size.Product - edgeSize * edgeSize,edgeSize.ToString());
			Assert.IsTrue(rs.SDS.IC.OneCount > 0);
			Assert.IsTrue(core.OneCount == 0);


			Assert.AreEqual(rs.Generation, 1);
			Assert.AreEqual(rs.SDS.FinalEntities.Length, 2);
			Assert.IsFalse(rs.SDS.HasEntity(crosser.ID.Guid));

			var check = Simulation.CheckMissingRCS(rs);
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
			SimulationRun run = new SimulationRun(
				new BaseDB.ConfigContainer() { extent = Int3.One, r = 1f / 8, m = 1f / 16 },
				new ShardID(Int3.Zero, 0));

			Vec3 outlierCoords = Simulation.MySpace.Min;

			foreach (var n in Simulation.Neighbors)
				n.OnPutRCS = (rcs, gen) =>
				{
					Assert.Fail("This test generates no simulation neighbors. Should not generate RCSs");
				};

			DB.OnPutSDS = dbSDS =>
			{
				Assert.AreEqual(Entity.Import( dbSDS.SerialEntities).Length, 2);
				Assert.AreEqual(dbSDS.Generation, 1);
				Assert.AreEqual(new InconsistencyCoverage(dbSDS.IC).OneCount, 0);
			};

			run.FeedEntities(new Entity[]
				{
					new Entity(
						new EntityID(Guid.NewGuid(), Simulation.MySpace.Center),
						Vec3.Zero, 
						new ExceedingMovementLogic(),	//check that this doesn't actually cause a fault (should get clamped)
						null),

					new Entity(
						new EntityID(Guid.NewGuid(), outlierCoords),
						Vec3.Zero, 
						new StationaryLogic(),
						//new EntityTest.FaultLogic.State(),
						null),
				}
			);

			var comp = run.BeginAdvanceTLG(true);


			foreach (var p in comp.Intermediate.localChangeSet.NamedSets)
			{
				int expected = p.Key == "motions" || p.Key == "advertisements" ? 2 : 0;
				Assert.AreEqual(expected, p.Value.Size);
			}

			Assert.AreEqual(comp.Generation, 1);


			var sds = run.CompleteAdvanceTLG(true);
			Assert.AreEqual(sds.Generation, 1);
			Assert.AreEqual(sds.SDS.FinalEntities.Length, 2);
		}


		[TestMethod()]
		public void BroadcastRangeTest()
		{
			SimulationRun run = new SimulationRun(
				new BaseDB.ConfigContainer() { extent = Int3.One, r = 1f, m = 1f },
				new ShardID(Int3.Zero, 0));

			Entity[] entities = new Entity[16];
			entities[0] = new Entity(new EntityID(Guid.NewGuid(), Vec3.Zero), Vec3.Zero, new BroadcastLogic(entities.Length-1));
			for (int i = 1; i < entities.Length; i++)
				entities[i] = new Entity(new EntityID(Guid.NewGuid(), new Vec3(1f / (entities.Length-1) * i, 0, 0)), Vec3.Zero, new BroadcastReceiverLogic(entities.Length - i));


			run.FeedEntities(entities);
			run.AdvanceTLG(true, true);	//send messages here
			run.AdvanceTLG(true, true);	//receive messages here

			for (int i = 0; i < run.tlgEntry.SDS.FinalEntities.Length; i++)
			{
				var e = run.tlgEntry.SDS.FinalEntities[i];
				BroadcastReceiverLogic logic = e.MyLogic as BroadcastReceiverLogic;
				if (logic != null)
					Assert.AreEqual(logic.ShouldReceive,  logic.numReceived, i.ToString());
			}
		}


	}

	internal class ReplicaOnlySD : IPollable<BaseDB.SDConfigContainer>
	{
		public int Size { get; set; }

		public ReplicaOnlySD(int size)
		{
			Size = size;
		}

		public BaseDB.SDConfigContainer Latest => new BaseDB.SDConfigContainer() { gatewayCount = 0, replicaCount = Size };
	}

	[Serializable]
	internal class BroadcastReceiverLogic : EntityLogic
	{
		public int numReceived = 0;
		public readonly int ShouldReceive;
		public BroadcastReceiverLogic(int shouldReceive)
		{
			ShouldReceive = shouldReceive;
		}
		protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges, bool locationIsInconsistent)
		{
			numReceived += Helper.Length(currentState.InboundMessages);
		}
	}

	[Serializable]
	internal class BroadcastLogic : EntityLogic
	{
		private int numMessages;

		public BroadcastLogic(int numMessages)
		{
			this.numMessages = numMessages;
		}

		protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges, bool locationIsInconsistent)
		{
			for (int i = 0; i < numMessages; i++)
				newState.Broadcast(0, null, ranges.R / numMessages * (1.5f + i));
		}
	}
}