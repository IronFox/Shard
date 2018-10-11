using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	/* To Do:
	 * Implement consensus protocol for multi-sibling client interaction.
	 * Change CS implementation to completely represent next generation (not copy existing entities), drop Remove-changes.
	 * Implement db-based shard location service.
	 * Implement db manager that can setup and link CouchDB instances.
	 * Implement manager that can determine and query status of shard network.
	 * Update recovery choice to improved algorithm.
	 * Replace RCS stack with individual RCSs identified by source/target shard and generation.
	 * Implement DB RCS cleanup procedure when all siblings are present according to their oldest generation.
	 */

	class Program
	{

		class SDSFactory
		{
			private Box box;
			private EntityPool pool;
			private Int3 sector;

			public SDSFactory(Int3 sector, EntityChange.ExecutionContext ctx, Int3 space)
			{
				box = Box.OffsetSize(new Vec3(sector), Vec3.One, sector + 1 >= space);
				pool = new EntityPool(ctx);
				this.sector = sector;
			}

			public void Include(Entity e)
			{
				Debug.Assert(box.Contains(e.ID.Position));
				if (!pool.Insert(e))
					throw new Exception("Failed to insert "+e);
			}

			public SerialSDS Finish()
			{
				SDS sds = new SDS(0, pool.ToArray(), InconsistencyCoverage.NewCommon(),false,null);
				return new SerialSDS(sds,sector);
			}
		}

		static void SetupScenario(DB.ConfigContainer cfg, IEnumerable<Entity> entities)
		{
			DB.PutConfigAsync(cfg).Wait();
			Simulation.Configure(new ShardID(), cfg, true);
			//if (DB.HasAdminAccess)
			var clearTask = DB.ClearSimulationDataAsync();

			SDSFactory[,,] grid = new SDSFactory[cfg.extent.X, cfg.extent.Y, cfg.extent.Z];
			cfg.extent.XYZ.Cover(at =>
				{
					grid[at.X, at.Y, at.Z] = new SDSFactory(at,new SimulationContext(false),cfg.extent.XYZ);
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

			clearTask.Wait();
			Task[] tasks = new Task[cfg.extent.XYZ.Product];
			int idx = 0;
			foreach (var factory in grid)
			{
				tasks[idx++] = DB.PutAsync(factory.Finish(),true);
			}

			Task.WaitAll(tasks);
			Simulation.Shutdown();
		}


		static Dictionary<string, CSLogicProvider> providers = new Dictionary<string, CSLogicProvider>();
		static CSLogicProvider GetProvider(string name)
		{
			CSLogicProvider provider;
			if (providers.TryGetValue(name, out provider))
				return provider;
			provider = CSLogicProvider.CompileAsync(name, File.ReadAllText(Path.Combine("scenario", "Logic", name + ".cs"))).Result;
			providers[name] = provider;
			return provider;
		}

		static Random random = new Random();
		static IEnumerable<Entity> Translate(IEnumerable<ScenarioEntity> e, Dictionary<string, Task<CSLogicProvider>> providerMap, Random random)
		{
			foreach (var se in e)
			{
				LogicInstantiation inst = new LogicInstantiation(se.logic);
				var provider = providerMap[inst.AssemblyName].Result;

				for (int i = 0; i < se.instances; i++)
				{
					Vec3 pos = Vec3.Zero;
					if (se.position.Length == 1)
					{
						pos = new Vec3(se.position[0], 0);
					}
					else if (se.position.Length == 2)
					{
						pos = random.NextVec3(Box.FromMinAndMax(new Vec3(se.position[0], 0), new Vec3(se.position[1], 0), Bool3.True));
					}
					else
						throw new Exception("Invalid position declaration: " + se.position);

#if STATE_ADV
					EntityAppearanceCollection appearances = new EntityAppearanceCollection();
#endif
					DynamicCSLogic logic = new DynamicCSLogic(provider, inst.LogicName, inst.Parameters);
					yield return new Entity(new EntityID(pos), Vec3.Zero, logic
#if STATE_ADV
						, appearances
#endif
						);
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
			public float[][] position = null;
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
							Vec3.Zero,
							new Habitat(random),   //check that this doesn't actually cause a fault (should get clamped)
							null);
		}



		static void Main(string[] args)
		{

			//RunStupidModel();
			//return;



			if (args.Length < 2)
			{
				Console.Error.WriteLine("Usage: shard <db url> <my addr> | shard <db url> --setup");
				return;
			}

			try
			{
				int at = 0;
				var dbHost = new PeerAddress(args[at++]);
				DB.Connect(dbHost);//,"admin","1234");

				if (args[at] == "setup")
				{
					CreateScenario();
					Log.Message("Scenario set up. Shutting down");
					return;
				}



				DB.PullConfig();

				Log.Message("Setting up clock");
				Clock.NTPHost = DB.Config.ntp;

				while (Clock.NumQueries < 1)
				{
					Thread.Sleep(100);
				}
				Log.Message("Starting up");


				ShardID addr = ShardID.Decode(args[at++]);


#if DRY_RUN
				if (addr == new ShardID())  //root
				{
					Log.Message("Resetting timer");
					while (true)
					{
						Thread.Sleep(100);
						var t = DB.Timing;
						if (t == null)
							continue;

						var n = Clock.Now + TimeSpan.FromSeconds(10);
						t.startTime = n.ToShortDateString()+" "+n.ToLongTimeString();
						t.msStep = 1000;	//make sure we compute slowly
						DB.Timing = t;
						break;
					}
				}
#endif


				if ((addr >= DB.Config.extent).Any)
					throw new ArgumentOutOfRangeException("addr", addr, "Exceeds extent: " + DB.Config.extent);
				if ((addr < ShardID.Zero).Any)
					throw new ArgumentOutOfRangeException("addr", addr, "Is (partially) negative");

				Simulation.Run(addr);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}

		private static void CreateScenario()
		{

			ScenarioConfig scenario = JsonConvert.DeserializeObject<ScenarioConfig>(File.ReadAllText("scenario/test0.json"));


			var providerMap = new Dictionary<string, Task<CSLogicProvider>>();

			foreach (var e in scenario.entities)
			{
				LogicInstantiation inst = new LogicInstantiation(e.logic);




				Task<CSLogicProvider> prov;
				if (!providerMap.TryGetValue(inst.AssemblyName, out prov))
				{
					string code = File.ReadAllText("scenario/Logic/" + inst.AssemblyName + ".cs");
					prov = DB.PutCompiledLogicProviderAsync(inst.AssemblyName, code);
					providerMap[inst.AssemblyName] = prov;
				}
			}

			Random rng = new Random(1024);
			SetupScenario(new DB.ConfigContainer()
						{
							extent = new ShardID(scenario.worldSize[0], scenario.worldSize[1], scenario.worldSize[2], 1),
							m = scenario.M,
							r = scenario.R
						},
						Translate(scenario.entities,providerMap,rng));
			//			GenerateEntities(1000));

		}

		private static void RunStupidModel()
		{





			int gridRes = 100;   //2d resolution
								 //each grid cell can 'see' +- 4 cells in all direction. All 'motion' is done via communication
								 //hence R = 4 / gridRes
			float r = 4.5f / gridRes;

			DB.ConfigContainer config = new DB.ConfigContainer() { extent = new ShardID(new Int3(1), 1), r = r, m = r * 0.5f };
			Simulation.Configure(new ShardID(Int3.Zero, 0), config, true);
			Vec3 outlierCoords = Simulation.MySpace.Min;

			var ctx = new SimulationContext(true);
			var intermediate0 = new IntermediateSDS();
			intermediate0.entities = new EntityPool(MakeGrid2D(gridRes),ctx);
			//EntityTest.RandomDefaultPool(100);
			intermediate0.ic = InconsistencyCoverage.NewCommon();
			intermediate0.inputConsistent = true;
			intermediate0.localChangeSet = new EntityChangeSet();

			SDSStack.Entry root = new SDSStack.Entry(
												new SDS(0, intermediate0.entities.ToArray(), intermediate0.ic, false,null),
												intermediate0);
			//Assert.IsTrue(root.IsFullyConsistent);

			SDSStack stack = Simulation.Stack;
			stack.ResetToRoot(root);

			for (int i = 0; i < 13; i++)
			{
				//Assert.IsNotNull(stack.NewestSDS.FinalEntities, i.ToString());
				var temp = stack.AllocateGeneration(i + 1);
				ctx.SetGeneration(i + 1);
				var comp = new SDSComputation(new DateTime(), null, TimeSpan.FromMilliseconds(10),ctx);
				//ComputationTests.AssertNoErrors(comp, "comp");
				//Assert.IsTrue(comp.Intermediate.inputConsistent);

				var sds = comp.Complete();
				stack.Insert(sds);
				//Assert.IsTrue(sds.IsFullyConsistent);

				//Assert.AreEqual(sds.FinalEntities.Length, gridRes * gridRes);

				int numBugs = 0;
				int numPredators = 0;
				int numConflicts = 0;
				float totalFood = 0;
				foreach (var e in sds.Item1.FinalEntities)
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
		}
	}

	internal struct LogicInstantiation
	{
		public readonly string AssemblyName;
		public readonly string LogicName;
		public readonly string[] Parameters;

		/// <summary>
		/// Constructs instantiation parameters in the form
		/// assembly[:logic][,p0[,p1[,...
		/// </summary>
		/// <param name="str">String to parse</param>
		public LogicInstantiation(string str)
		{
			string[] parts = str.Split(',');

			AssemblyName = parts[0];
			LogicName = null;



			int colonAt = AssemblyName.IndexOf(':');
			if (colonAt != -1)
			{
				LogicName = AssemblyName.Substring(colonAt + 1);
				AssemblyName = AssemblyName.Remove(colonAt);
			}
			Parameters = parts.Subarray(1);
		}
	}
}
