using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	class Program
	{

		class SDSFactory
		{
			private Box box;
			private EntityPool pool = new EntityPool();

			public SDSFactory(Box box)
			{
				this.box = box;
			}

			public void Include(Entity e)
			{
				Debug.Assert(box.Contains(e.ID.Position));
				if (!pool.Insert(e))
					throw new Exception("Failed to insert "+e);
			}

			public SDS.Serial Finish()
			{
				SDS sds = new SDS(0, pool.ToArray(), InconsistencyCoverage.NewCommon(), new SDS.IntermediateData(), null,null);
				return sds.Export();
			}
		}

		static void SetupScenario(Host dbHost, DB.ConfigContainer cfg, IEnumerable<Entity> entities)
		{
			DB.Connect(dbHost);
			DB.PutConfig(cfg);

			SDSFactory[,,] grid = new SDSFactory[cfg.extent.X, cfg.extent.Y, cfg.extent.Z];
			cfg.extent.XYZ.Cover(at =>
				{
					grid[at.X, at.Y, at.Z] = new SDSFactory(Box.OffsetSize(new Vec3(at), Vec3.One, at+1 >= cfg.extent.XYZ));
				}
			);
			var gridBox = IntBox.FromMinAndMax(Int3.Zero, cfg.extent.XYZ, Bool3.False);
			var simulationSpace = Box.FromMinAndMax(Vec3.Zero, new Vec3(cfg.extent.XYZ), Bool3.True);
			foreach (var e in entities)
			{
				if (!simulationSpace.Contains(e.ID.Position))
					throw new Exception("Scenario entity "+e+" is located outside simulation space");
				var cell = gridBox.Clamp(e.ID.Position.FloorInt3);
				grid[cell.X, cell.Y, cell.Z].Include(e);
			}

			Task[] tasks = new Task[cfg.extent.XYZ.Product];
			int idx = 0;
			foreach (var factory in grid)
			{
				tasks[idx++] = DB.PutAsyncTask(factory.Finish(),true);
			}

			Task.WaitAll(tasks);

		}


		static Dictionary<string, CSLogicProvider> providers = new Dictionary<string, CSLogicProvider>();
		static CSLogicProvider GetProvider(string name)
		{
			CSLogicProvider provider;
			if (providers.TryGetValue(name, out provider))
				return provider;
			provider = new CSLogicProvider(name, File.ReadAllText(Path.Combine("scenario", "Logic", name+ ".cs")));
			providers[name] = provider;
			return provider;
		}

		static Random random = new Random();
		static IEnumerable<Entity> Translate(IEnumerable<ScenarioEntity> e)
		{
			foreach (var se in e)
			{
				for (int i = 0; i < se.instances; i++)
				{
					Vec3 pos = Vec3.Zero;
					if (se.position.Length == 3)
					{
						pos = new Vec3(se.position, 0);
					}
					else if (se.position.Length == 6)
					{
						pos = random.NextVec3(Box.FromMinAndMax(new Vec3(se.position, 0), new Vec3(se.position, 3), Bool3.True));
					}
					else
						throw new Exception("Invalid position declaration: " + se.position);

					EntityAppearanceCollection appearances = new EntityAppearanceCollection();
					

					yield return new Entity(new EntityID(pos), new DynamicCSLogic(GetProvider(se.logic), null,null), appearances);
				}
				
			}
		}


		public class ScenarioConfig
		{
			public int[] worldSize = null;
			public float R=0, M=0;
			public ScenarioEntity[] entities = null;
		}

		public class ScenarioEntity
		{
			public float[] position = null;
			public string logic = null;
			public dynamic[] appearances = null;
			public int instances = 1;
		}

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



		static void Main(string[] args)
		{







			int gridRes = 100;   //2d resolution
								 //each grid cell can 'see' +- 4 cells in all direction. All 'motion' is done via communication
								 //hence R = 4 / gridRes
			float r = 4.5f / gridRes;

			DB.ConfigContainer config = new DB.ConfigContainer() { extent = new ShardID(new Int3(1), 1), r = r, m = r * 0.5f };
			Simulation.Configure(new ShardID(Int3.Zero, 0), config, true);
			Vec3 outlierCoords = Simulation.MySpace.Min;

			SDS.IntermediateData intermediate0 = new SDS.IntermediateData();
			intermediate0.entities = new EntityPool(MakeGrid2D(gridRes));
			//EntityTest.RandomDefaultPool(100);
			intermediate0.ic = InconsistencyCoverage.NewCommon();
			intermediate0.inputConsistent = true;
			intermediate0.localChangeSet = new EntityChangeSet();

			SDS root = new SDS(0, intermediate0.entities.ToArray(), intermediate0.ic, intermediate0, null, null);
			//Assert.IsTrue(root.IsFullyConsistent);

			SDSStack stack = Simulation.Stack;
			stack.ResetToRoot(root);

			for (int i = 0; i < 13; i++)
			{
				//Assert.IsNotNull(stack.NewestSDS.FinalEntities, i.ToString());
				SDS temp = stack.AllocateGeneration(i + 1);
				SDS.Computation comp = new SDS.Computation(i + 1, null, TimeSpan.FromMilliseconds(10));
				//ComputationTests.AssertNoErrors(comp, "comp");
				//Assert.IsTrue(comp.Intermediate.inputConsistent);

				SDS sds = comp.Complete();
				stack.Insert(sds);
				//Assert.IsTrue(sds.IsFullyConsistent);

				//Assert.AreEqual(sds.FinalEntities.Length, gridRes * gridRes);

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

				Console.WriteLine("Population: b=" + numBugs + ", p=" + numPredators + ", c=" + numConflicts + "; Food=" + totalFood);

			}











			return;




			string json = File.ReadAllText("scenario/test0.json");
			ScenarioConfig scenario = JsonConvert.DeserializeObject<ScenarioConfig>(json);

			SetupScenario(new Host("localhost", 1024),
						new DB.ConfigContainer()
						{
							extent = new ShardID(scenario.worldSize[0],scenario.worldSize[1],scenario.worldSize[2],1),
							m = scenario.M,
							r = scenario.R,
							start = (DateTime.Now.ToUniversalTime() + TimeSpan.FromHours(1)).ToString()
						},
						Translate(scenario.entities));
			//			GenerateEntities(1000));


			if (args.Length != 2)
			{
				Console.Error.WriteLine("Expected 2 parameters: [db url] [my addr], found "+args.Length);
				return;
			}

			try
			{
				int at = 0;
				var dbHost = new Host(args[at++]);
				DB.Connect(dbHost);
				DB.PullConfig();
				ShardID addr = ShardID.Decode(args[at++]);

				if ((addr >= DB.Config.extent).Any)
					throw new ArgumentOutOfRangeException("addr", addr, "Exceeds extent: " + DB.Config.extent);
				if ((addr < ShardID.Zero).Any)
					throw new ArgumentOutOfRangeException("addr", addr, "Is (partially) negative");

				Simulation.Init(addr);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
	}
}
