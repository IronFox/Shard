using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using Shard.Tests;
using System.Collections.Generic;
using VectorMath;

namespace ShardTests1
{
	[TestClass]
	public class EntityTest
	{
		static Random random = new Random();


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

			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom random)
			{
				CheckRandom(random,"started");
				bool isConsistent = true;
				var old = currentState.GetAppearance<ConsistencyAppearance>();
				if (old != null)
				{
					isConsistent = old.IsConsistent;
				}


				if (isConsistent && currentState.Contacts != null)
				{
					foreach (var c in currentState.Contacts)
					{
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

				CheckRandom(random, "loop prior");
				int cnt = 0;
				float M = Simulation.M;
				do
				{
					CheckRandom(random, "loop "+cnt);
					Vec3 draw = random.NextVec3(-M, M);
					newState.NewPosition = Simulation.MySpace.Clamp(currentState.ID.Position + draw);
					if (++cnt > 1000)
						throw new Exception("Exceeded 1000 tries, going from " + currentState.ID.Position + ", by " + M + "->"+draw+" in " + Simulation.MySpace+"; "+random.Next()+", "+random.Next()+", "+random.Next());
				}
				while (newState.NewPosition == currentState.ID.Position);
			}
		}

		[Serializable]
		public class RandomMotion : EntityLogic
		{
			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource)
			{
				while (newState.NewPosition == currentState.ID.Position)
				{
					newState.NewPosition = currentState.ID.Position + randomSource.NextVec3(-Simulation.M, Simulation.M);
					if (!Simulation.CheckDistance("Motion", newState.NewPosition, currentState, Simulation.M))
						throw new Exception("WTF");

					newState.NewPosition = Simulation.FullSimulationSpace.Clamp(newState.NewPosition);
					if (!Simulation.CheckDistance("Motion", newState.NewPosition, currentState, Simulation.M))
						throw new Exception("WTF2");

				}
			}
		}

		[Serializable]
		public class FaultLogic : EntityLogic
		{
			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource)
			{
				throw new NotImplementedException();
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
			EntityPool pool = new EntityPool(EntityPoolTests.CreateEntities(100, 
				new RandomLogic(new Type[] { typeof(ConsistentLogic), typeof(FaultLogic) }).Instantiate));

			InconsistencyCoverage ic = InconsistencyCoverage.NewCommon();
			int any = -1;
			for (int i = 0; i < 8; i++)
			{
				EntityChangeSet set = new EntityChangeSet();
				var errors = pool.TestEvolve(set,ic,i,false,TimeSpan.FromSeconds(1));
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

			for (int k = 0; k < 20; k++)
			{
				InconsistencyCoverage ic = InconsistencyCoverage.NewCommon();

				EntityPool pool = new EntityPool(EntityPoolTests.CreateEntities(100, i =>  i > 0 ? new ConsistentLogic() : (EntityLogic)(new FaultLogic())));

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
				for (int i = 0; i < InconsistencyCoverage.CommonResolution; i++)	//no point going further than current resolution
				{
					EntityChangeSet set = new EntityChangeSet();
					var errors = pool.TestEvolve(set, ic, i, false,TimeSpan.FromSeconds(1));
					Assert.AreEqual(1, errors.Count, Helper.Concat(",",errors));
					Assert.AreEqual(0, set.Execute(pool));
					//Assert.AreEqual(ic.OneCount, 1);

					if (doGrow)
						ic = ic.Grow(true);
					else
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
						if (!isFaultOrigin)
						{
							if (Simulation.GetDistance(e.ID.Position, faulty.ID.Position) <= Simulation.SensorRange)
							{
								Assert.IsTrue(e.HasContact(faulty.ID.Guid));
								Assert.IsTrue(e.HasContact(faulty.ID));
							}

							var adv = set.FindAdvertisementFor(e.ID);
							Assert.IsNotNull(e.Appearances);
						}
						bool consistent = ConsistentLogic.IsConsistent(e.Appearances);
						bool icIsInc = ic.IsInconsistentR(Simulation.MySpace.Relativate(e.ID.Position));

						if (!consistent && !icIsInc)
						{
							if (doGrow)
								Assert.Fail("Inconsistent entity located outside IC area: " + e);
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


		public static EntityPool RandomDefaultPool(int numEntities)
		{
			return new EntityPool(EntityPoolTests.CreateEntities(100,  i => new ConsistentLogic()));
		}

		[TestMethod]
		public void StateAdvertisementTest()
		{
			EntityPool pool = RandomDefaultPool(100);
			for (int i = 0; i < 100; i++)
			{
				EntityChangeSet set = new EntityChangeSet();
				var errors = pool.TestEvolve(set,InconsistencyCoverage.NewCommon(),i, false,TimeSpan.FromSeconds(5));
				Assert.IsNull(errors, errors != null ? errors[0].ToString() : "");
				Assert.AreEqual(0, set.Execute(pool));

				HashSet<Guid> env = new HashSet<Guid>();
				var state = pool.ToArray();
				int it = 0;
				foreach (var e in state)
				{
					env.Clear();
					foreach (var e1 in state)
					{
						float dist = Simulation.GetDistance(e.ID.Position, e1.ID.Position);
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
						float dist = Simulation.GetDistance(c.ID.Position, e.ID.Position);
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

		[TestMethod]
		public void EntityMotionTest()
		{
			int numEntities = 100;
			EntityPool pool = new EntityPool(EntityPoolTests.CreateEntities(numEntities, i => new RandomMotion()));

			for (int i = 0; i < 100; i++)
			{
				var old = pool.ToArray();
				EntityChangeSet set = new EntityChangeSet();
				var errors = pool.TestEvolve(set,InconsistencyCoverage.NewCommon(),i, false,TimeSpan.FromSeconds(1));
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

				Assert.AreEqual(0, set.Execute(pool));
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
