using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using Shard.Tests;
using System.Collections.Generic;
using VectorMath;
using Base;

namespace ShardTests1
{
	[TestClass]
	public class EntityTest
	{
		static Random random = new Random();


#if STATE_ADV
		[Serializable]
		private class ConsistencyAppearance : EntityAppearance
		{
			public ConsistencyAppearance(bool isConsistent)
			{
				IsConsistent = isConsistent;
			}

			public bool IsConsistent { get; set; }

			public override int CompareTo(EntityAppearance other)
			{
				ConsistencyAppearance that = other as ConsistencyAppearance;
				return other != null ? IsConsistent.CompareTo(that.IsConsistent) : -1;
			}

			public override int GetHashCode()
			{
				return IsConsistent.GetHashCode();
			}
		}

		[Serializable]
		private class ConsistentLogic : EntityLogic
		{
			public static bool IsConsistent(EntityAppearanceCollection app)
			{
				if (app == null)
					return false;
				var a = app.Get<ConsistencyAppearance>();
				return a != null && a.IsConsistent;
			}

			public static void CheckRandom(EntityRandom random, string at)
			{
				int i0 = random.Next(), i1 = random.Next(), i2 = random.Next();
				if (i0 == i1 && i1 == i2)
					throw new Exception("Random generator is out of whack: " + at);

			}

			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom random, Shard.EntityRanges ranges)
			{
				CheckRandom(random,"started");
		
				if (isConsistent && currentState.Contacts != null)
				{
					foreach (var c in currentState.Contacts)
					{
						var dist = Vec3.GetChebyshevDistance(c.ID.Position, currentState.ID.Position);
						Assert.IsTrue(dist <= Simulation.SensorRange);
						if (!IsConsistent(c.Appearances))
						{
							isConsistent = false;
							break;
						}
					}

				}
				newState.AddOrReplace(new ConsistencyAppearance(isConsistent)); //is consistent?

				if (!Simulation.MySpace.Contains(currentState.ID.Position))
					throw new IntegrityViolation(currentState+": not located in simulation space "+ Simulation.MySpace);

				int cnt = 0;
				do
				{
					Vec3 draw = random.NextVec3(-ranges.M, ranges.M);
					newState.NewPosition = ranges.World.Clamp(currentState.ID.Position + draw);
					if (++cnt > 1000)
						throw new Exception("Exceeded 1000 tries, going from " + currentState.ID.Position + ", by " + M + "->"+draw+" in " + Simulation.MySpace+"; "+random.Next()+", "+random.Next()+", "+random.Next());
				}
				while (newState.NewPosition == currentState.ID.Position);
			}
		}
#else
		[Serializable]
		private class ConsistentLogic : EntityLogic
		{
			public bool isConsistent = true;
			public bool wasDeclaredInconsistent = false;

			public static bool GrowingIC { get; internal set; }

			public static void CheckRandom(EntityRandom random, string at)
			{
				int i0 = random.Next(), i1 = random.Next(), i2 = random.Next();
				if (i0 == i1 && i1 == i2)
					throw new Exception("Random generator is out of whack: " + at);

			}

			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom random, Shard.EntityRanges ranges, bool isInconsistent)
			{
				CheckRandom(random, "started");

				wasDeclaredInconsistent = isInconsistent;

				if (Helper.Length(currentState.InboundMessages) > 0)
				{
					foreach (var msg in currentState.InboundMessages)
					{
						var remote = (EntityID) Helper.Deserialize(msg.Payload);
						float dist = Vec3.GetChebyshevDistance(remote.Position, currentState.ID.Position);
						Assert.IsTrue(dist <= ranges.R, dist +"<="+ ranges.R);
					}
					isConsistent = false;
				}
				Assert.IsTrue(isConsistent || !GrowingIC || isInconsistent);

				if (!isConsistent)
					newState.Broadcast(0, Helper.SerializeToArray(currentState.ID));

				if (!Simulation.MySpace.Contains(currentState.ID.Position))
					throw new IntegrityViolation(currentState + ": not located in simulation space " + Simulation.MySpace);

				int cnt = 0;
				do
				{
					Vec3 draw = random.NextVec3(-ranges.Motion, ranges.Motion);
					newState.NewPosition = ranges.World.Clamp(currentState.ID.Position + draw);
					if (++cnt > 1000)
						throw new Exception("Exceeded 1000 tries, going from " + currentState.ID.Position + ", by " + ranges.Motion + "->" + draw + " in " + ranges.World + "; " + random.Next() + ", " + random.Next() + ", " + random.Next());
				}
				while (newState.NewPosition == currentState.ID.Position);
			}
		}
#endif

		[Serializable]
		public class RandomMotion : EntityLogic
		{
			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource, Shard.EntityRanges ranges, bool isInconsistent)
			{
				while (newState.NewPosition == currentState.ID.Position)
				{
					//newState.NewPosition = ranges.World.Clamp(currentState.ID.Position + random.NextVec3(-ranges.M, ranges.M));
					newState.NewPosition = currentState.ID.Position + randomSource.NextVec3(-ranges.Motion, ranges.Motion);
					if (Vec3.GetChebyshevDistance(newState.NewPosition, currentState.ID.Position) > ranges.Motion) 
						throw new Exception("Pre-clamp motion range exceeded");

					newState.NewPosition = ranges.World.Clamp(newState.NewPosition);
					if (Vec3.GetChebyshevDistance(newState.NewPosition, currentState.ID.Position) > ranges.Motion)
						throw new Exception("Post-clamp motion range exceeded");
				}
			}
		}

		[Serializable]
		public class FaultLogic : EntityLogic
		{
			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource, Shard.EntityRanges ranges, bool isInconsistent)
			{
#if STATE_ADV
				throw new NotImplementedException();
#else
				newState.Broadcast(0, Helper.SerializeToArray(currentState.ID));
				newState.FlagInconsistent();
#endif
			}
		}


		[Serializable]
		private class RandomLogic
		{
			private Type[] logics;

			public RandomLogic(IEnumerable<Type> logics)
			{
				this.logics = Helper.ToArray(logics);
			}

			public EntityLogic Instantiate(int i)
			{
				Type t = logics.PickRandom(random);
				return (EntityLogic)t.GetConstructor(Type.EmptyTypes).Invoke(null);
			}
		}


		[TestMethod]
		public void SimpleEntityFaultTest()
		{
			var ctx = new SimulationContext(true);
			EntityPool pool = new EntityPool(EntityPoolTests.CreateEntities(ctx.LocalSpace, 100, 
				new RandomLogic(new Type[] { typeof(ConsistentLogic), typeof(FaultLogic) }).Instantiate),ctx);

			InconsistencyCoverage ic = InconsistencyCoverage.NewCommon();
			int any = -1;
			for (int i = 0; i < 8; i++)
			{
				EntityChangeSet set = new EntityChangeSet();
				ctx.SetGeneration(i);
				var errors = pool.TestEvolve(set,ic,i,TimeSpan.FromSeconds(1));
				if (errors != null)
				{
					Assert.IsTrue(ic.OneCount > 0);
					if (any == -1)
						any = i;
				}
				ic = ic.Grow(true);
				Assert.IsTrue(ic.Size == InconsistencyCoverage.CommonResolution);
			}
			if (any == 0)
				Assert.AreEqual(ic.OneCount, ic.Size.Product);
		}

		[TestMethod]
		public void ExtensiveEntityFaultTest()
		{
			int mismatched = 0;

			var ctx = new SimulationContext(true);

			for (int k = 0; k < 20; k++)
			{
				InconsistencyCoverage ic = InconsistencyCoverage.NewCommon(),
										nextIC = ic;

				EntityPool pool = new EntityPool(EntityPoolTests.CreateEntities(ctx.LocalSpace, 100, i =>  i > 0 ? new ConsistentLogic() : (EntityLogic)(new FaultLogic())),ctx);

				int faultyCount = 0;
				Entity faulty = new Entity();
				foreach (var e in pool.ToArray())
				{
					if (Helper.Deserialize(e.SerialLogicState) is FaultLogic)
					{
						faultyCount++;
						faulty = e;
					}
				}
				Assert.AreEqual(faultyCount, 1);

				bool doGrow = k % 2 != 0;
				ConsistentLogic.GrowingIC = doGrow;

				for (int i = 0; i < InconsistencyCoverage.CommonResolution; i++)	//no point going further than current resolution
				{
					EntityChangeSet set = new EntityChangeSet();
					ctx.SetGeneration(i);

					var errors = pool.TestEvolve(set, ic, i, TimeSpan.FromSeconds(1));
					Assert.AreEqual(1, errors.Count, Helper.Concat(",",errors));
					Assert.AreEqual(0, set.Execute(pool,ic,ctx));
					//Assert.AreEqual(ic.OneCount, 1);
					if (doGrow)
						ic = ic.Grow(true);

					if (!doGrow)
						Assert.AreEqual(ic.OneCount, 1);


					Entity[] entities = pool.ToArray();
					bool hasFaulty = false;
					foreach (var e in entities)
					{
						bool isFaultOrigin = (Helper.Deserialize(e.SerialLogicState) is FaultLogic);
						if (isFaultOrigin)
						{
							faulty = e;
							Assert.IsFalse(hasFaulty);
							hasFaulty = true;
						}
					}
					Assert.IsTrue(hasFaulty);

					foreach (var e in entities)
					{
						bool isFaultOrigin = (Helper.Deserialize(e.SerialLogicState) is FaultLogic);
#if STATE_ADV
						if (!isFaultOrigin)
						{
							if (ctx.GetDistance(e.ID.Position, faulty.ID.Position) <= Simulation.SensorRange)
							{
								Assert.IsTrue(e.HasContact(faulty.ID.Guid));
								Assert.IsTrue(e.HasContact(faulty.ID));
							}

							var adv = set.FindAdvertisementFor(e.ID);
							Assert.IsNotNull(e.Appearances);
						}
						bool consistent = ConsistentLogic.IsConsistent(e.Appearances);
#else
						ConsistentLogic c = e.MyLogic as ConsistentLogic;
						bool consistent = c != null && c.isConsistent;
#endif
						bool icIsInc = ic.IsInconsistentR(Simulation.MySpace.Relativate(e.ID.Position));

						if (!consistent && !icIsInc)
						{
							if (doGrow)
							{
								Assert.Fail("Inconsistent entity located outside IC area: " + e + ", " + e.MyLogic);
							}
							else
							{
								mismatched++;
								break;
							}
						}
					}

					//Assert.AreEqual(ic.OneCount, 1);
				}


			}
			Assert.AreNotEqual(mismatched, 0);
		}


#if STATE_ADV
		
		public static EntityPool RandomDefaultPool(int numEntities, Shard.EntityChange.ExecutionContext ctx)
		{
			return new EntityPool(EntityPoolTests.CreateEntities( 100,  i => new ConsistentLogic()),ctx);
		}
		[TestMethod]
		public void StateAdvertisementTest()
		{
			var ctx = new SimulationContext();
			EntityPool pool = RandomDefaultPool(100,ctx);
			for (int i = 0; i < 100; i++)
			{
				EntityChangeSet set = new EntityChangeSet();
				ctx.SetGeneration(i);
				var errors = pool.TestEvolve(set,InconsistencyCoverage.NewCommon(),i, TimeSpan.FromSeconds(5));
				Assert.IsNull(errors, errors != null ? errors[0].ToString() : "");
				Assert.AreEqual(0, set.Execute(pool,ctx));

				HashSet<Guid> env = new HashSet<Guid>();
				var state = pool.ToArray();
				int it = 0;
				foreach (var e in state)
				{
					env.Clear();
					foreach (var e1 in state)
					{
						float dist = ctx.GetDistance(e.ID.Position, e1.ID.Position);
						if (e1.ID.Guid != e.ID.Guid && dist <= Simulation.SensorRange)
						{
							Console.WriteLine(dist+"/"+Simulation.SensorRange);
							env.Add(e1.ID.Guid);
						}
					}
					Assert.AreEqual(env.Count, e.Contacts.Length, i+"."+it+": "+e);
					if (env.Count > 0)
						it++;

					foreach (var c in e.Contacts)
					{
						float dist = ctx.GetDistance(c.ID.Position, e.ID.Position);
						Assert.IsTrue(dist <= Simulation.SensorRange, dist + " <= " + Simulation.SensorRange);
						Assert.AreNotEqual(c.ID.Guid, e.ID.Guid);
						Assert.IsTrue(env.Contains(c.ID.Guid));
					}

					var app = e.Appearances.Get<ConsistencyAppearance>();
					if (i > 0)
					{
						Assert.IsNotNull(e.Appearances);
						Assert.IsNotNull(app);
					}
					if (app != null)
						Assert.IsTrue(app.IsConsistent);
				}
			}
		}
#endif

		[TestMethod]
		public void EntityMotionTest()
		{
			int numEntities = 100;
			var ctx = new SimulationContext(true);
			EntityPool pool = new EntityPool(EntityPoolTests.CreateEntities(ctx.LocalSpace, numEntities, i => new RandomMotion()),ctx);

			InconsistencyCoverage ic = InconsistencyCoverage.NewCommon();

			for (int i = 0; i < 100; i++)
			{
				var old = pool.ToArray();
				EntityChangeSet set = new EntityChangeSet();
				ctx.SetGeneration(i);
				var errors = pool.TestEvolve(set,InconsistencyCoverage.NewCommon(),i, TimeSpan.FromSeconds(1));
				Assert.IsNull(errors,errors != null ? errors[0].ToString() : "");
				Assert.AreEqual(numEntities, set.FindNamedSet("motions").Size);
				foreach (var e in old)
				{
					var m = set.FindMotionOf(e.ID.Guid);
					Assert.IsNotNull(m);
					Assert.AreNotEqual(m.TargetLocation, m.Origin.Position, i.ToString());
					Assert.IsTrue(Simulation.FullSimulationSpace.Contains(m.TargetLocation));
					Assert.IsTrue(Simulation.MySpace.Contains(m.TargetLocation));
				}

				Assert.AreEqual(0, set.Execute(pool,ic,ctx));
				Assert.AreEqual(numEntities, pool.Count);
				Entity e1;
				foreach (var e in old)
				{
					Assert.IsTrue(pool.Find(e.ID.Guid, out e1));
					Assert.AreNotEqual(e.ID.Position, e1.ID.Position);
				}
			}

		}
	}
}
