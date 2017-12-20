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

	[Serializable]
	public class Animal
	{
		public float Size;

		public Animal(float initialSize)
		{
			Size = initialSize;
		}

		public Animal(byte[] payload) : this(BitConverter.ToSingle(payload, 0))
		{ }

		public byte[] Export()
		{
			return BitConverter.GetBytes(Size);
		}
	}

	[Serializable]
	public class GridLogic
	{
		Animal animal;

		readonly int channel;

		public GridLogic(int channel, float animalSize)
		{
			this.channel = channel;
			if (animalSize > 0)
				animal = new Animal(animalSize);
		}

		public bool HasAnimal
		{
			get
			{
				return animal != null;
			}
		}
		public float AnimalSize
		{
			get
			{
				return animal != null ? animal.Size : 0;
			}
			set
			{
				animal.Size = value;
			}
		}


		public bool ExecuteMotion(ref EntityLogic.Actions actions, Entity currentState, int generation, EntityRandom random, float myYield, ref int spawn)
		{
			bool rs = false;
			switch (generation % 3)
			{
				case 0:
					//receive unicast, send broadcast
					if (animal == null)
					{
						foreach (var msg in currentState.EnumInboundEntityMessages(channel))
						{
							if (!msg.IsBroadcast) //coming around
							{
								if (animal != null)
									throw new ExecutionException(currentState.ID, "Trying to import multiple animals");
								animal = new Animal(msg.Payload);
								rs = true;
							}
						}
					}

					if (animal != null)
						actions.Broadcast(channel, null);   //can i go there?
					break;
				case 1:
					//receive broadcast, send unicast
					if (animal == null)
					{
						LazyList<Actor> competitors = new LazyList<Actor>();
						foreach (var msg in currentState.EnumInboundEntityMessages(channel))
						{
							if (msg.IsBroadcast && msg.Payload == null) //can i go here?
								competitors.Add(msg.Sender);
						}
						if (competitors.IsNotEmpty)
							actions.Send(competitors[random.Next(competitors.Count)], channel, BitConverter.GetBytes(myYield)); //you can go here
					}
					break;
				case 2:
					//receive unicast, send unicast
					if (animal != null)
					{
						LazyList<Tuple<Actor, float>> options = new LazyList<Tuple<Actor, float>>();
						foreach (var msg in currentState.EnumInboundEntityMessages(channel))
						{
							if (!msg.IsBroadcast && Helper.Length(msg.Payload) == 4) //i can go there
								options.Add(new Tuple<Actor, float>(msg.Sender, BitConverter.ToSingle(msg.Payload, 0)));
						}
						options.Sort((a, b) => { return a.Item2.CompareTo(b.Item2); });
						if (options.IsNotEmpty)
						{
							if (spawn > 0)
							{
								foreach (var o in options)
								{
									actions.Send(o.Item1, channel, new Animal(0).Export()); //coming around
									if (--spawn <= 0)
										break;
								}
								spawn = 0;
							}
							else
								actions.Send(options.Last.Item1, channel, animal.Export()); //coming around
							animal = null;  //in transit
						}
					}
					break;
			}

			return rs;
		}

		public void Consume(GridLogic bug)
		{
			Consume(bug.animal.Size);
			bug.animal = null;
		}

		public void Consume(float size)
		{
			animal.Size += size;
		}
	};



	[Serializable]
	public class Habitat : EntityLogic
	{
		public readonly GridLogic	bug,
							predator;
		public float food = 0f;
		public float maxFoodProduction = 0.01f;
		int spawn = 0;

		public Habitat(EntityRandom random)
		{
			bool putBug = random.NextBool(0.01f);
			bool putPredator = random.NextBool(0.02f);

			bug = new GridLogic(0, putBug ? 0.01f : 0);
			predator = new GridLogic(1, putPredator ? 0.01f : 0);
		}

		public override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom random)
		{
			int zero = 0;
			bool killBug = predator.ExecuteMotion(ref newState, currentState, generation - 1, random, bug.AnimalSize,ref zero);

			food += random.NextFloat(maxFoodProduction);

			if (bug.HasAnimal)
			{
				if (killBug)
					predator.Consume(bug);
				else
				{
					float delta = Math.Min(food, 1f);
					bug.Consume(delta);
					food -= delta;
					food = Math.Max(0, food);
					if (bug.AnimalSize > 10f)
					{
						bug.AnimalSize = 0.001f;
						spawn = 5;
					}
				}
			}

			bug.ExecuteMotion(ref newState, currentState, generation, random, food,ref spawn);
		}
	}

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

			for (int i = 0; i < 12; i++)
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

