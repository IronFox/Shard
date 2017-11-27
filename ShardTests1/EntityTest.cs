using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using Shard.Tests;
using System.Collections.Generic;

namespace ShardTests1
{
	[TestClass]
	public class EntityTest
	{
		static Random random = new Random();


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

			public override void Hash(Hasher h)
			{
				h.Add(GetType());
				h.Add(IsConsistent.GetHashCode());
			}
		}



		private class ConsistentAppearance : EntityLogic
		{
			public static bool IsConsistent(EntityAppearanceCollection app)
			{
				if (app == null)
					return false;
				var a = app.Get<ConsistencyAppearance>();
				return a != null && a.IsConsistent;
			}

			public override int CompareTo(EntityLogic other)
			{
				return 0;
			}

			public override Changes Evolve(Entity currentState, int generation, Random randomSource)
			{
				Changes ch = new Changes();

				ch.newState = this; //no changes

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
				ch.Add(new ConsistencyAppearance(isConsistent));	//is consistent?

				float M = Simulation.M;
				do
				{
					ch.newPosition = Simulation.MySpace.Clamp(currentState.ID.Position + random.NextVec3(-M, M));
				}
				while (ch.newPosition == currentState.ID.Position);
				return ch;
			}

			public override void Hash(Hasher h)
			{
				h.Add(GetType());
			}
		}

		public class FaultLogic : EntityLogic
		{
			public override int CompareTo(EntityLogic other)
			{
				return 0;
			}

			public override Changes Evolve(Entity currentState, int generation, Random randomSource)
			{
				throw new NotImplementedException();
			}

			public override void Hash(Hasher h)
			{
				h.Add(GetType());
			}
		}
		

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
				new RandomLogic(new Type[] { typeof(ConsistentAppearance), typeof(FaultLogic) }).Instantiate));

			InconsistencyCoverage ic = InconsistencyCoverage.NewCommon();
			int any = -1;
			for (int i = 0; i < 8; i++)
			{
				EntityChangeSet set = new EntityChangeSet();
				int numErrors = pool.Evolve(set,ic,i);
				if (numErrors != 0)
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

				EntityPool pool = new EntityPool(EntityPoolTests.CreateEntities(100, i =>  i > 0 ? new ConsistentAppearance() : (EntityLogic)(new FaultLogic())));

				int faultyCount = 0;
				Entity faulty = new Entity();
				foreach (var e in pool.ToArray())
					if (e.LogicState is FaultLogic)
					{
						faultyCount++;
						faulty = e;
					}
				Assert.AreEqual(faultyCount, 1);

				bool doGrow = k % 2 != 0;
				for (int i = 0; i < InconsistencyCoverage.CommonResolution; i++)	//no point going further than current resolution
				{
					EntityChangeSet set = new EntityChangeSet();
					int numErrors = pool.Evolve(set, ic, i);
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
						bool isFaultOrigin = (e.LogicState is FaultLogic);
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
						bool isFaultOrigin = (e.LogicState is FaultLogic);
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
						bool consistent = ConsistentAppearance.IsConsistent(e.Appearances);
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
			return new EntityPool(EntityPoolTests.CreateEntities(100,  i => new ConsistentAppearance()));
		}

		[TestMethod]
		public void StateAdvertisementTest()
		{
			EntityPool pool = RandomDefaultPool(100);
			for (int i = 0; i < 100; i++)
			{
				EntityChangeSet set = new EntityChangeSet();
				pool.Evolve(set,null,i);
				Assert.AreEqual(0, set.Execute(pool));

				HashSet<Guid> env = new HashSet<Guid>();
				var state = pool.ToArray();
				foreach (var e in state)
				{

					env.Clear();
					foreach (var e1 in state)
						if (e1.ID.Guid != e.ID.Guid && Simulation.GetDistance(e.ID.Position, e1.ID.Position) <= Simulation.SensorRange)
						{
							env.Add(e1.ID.Guid);
						}
					Assert.AreEqual(env.Count, e.Contacts.Length, e.ToString());

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
			EntityPool pool = new EntityPool(EntityPoolTests.CreateEntities(100,i => new ConsistentAppearance()));

			for (int i = 0; i < 100; i++)
			{
				var old = pool.ToArray();
				EntityChangeSet set = new EntityChangeSet();
				pool.Evolve(set,null,i);
				Assert.AreEqual(0, set.Execute(pool));
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
