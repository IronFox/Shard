using Base;
using Newtonsoft.Json;
using Shard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace ScenarioSetup
{
	class Program
	{
		public class ScenarioConfig
		{
			public int[] worldSize = null;
			public float R = 0, M = 0;
			public ScenarioEntity[] entities = null;
		}


		public class ScenarioEntity
		{
			public float[][] position = null;
			public string logic = null;
			public dynamic[] appearances = null;
			public int instances = 1;
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

		class SDSFactory
		{
			private Box box;
			private EntityPool pool;
			private Int3 sector;

			public SDSFactory(Int3 sector, Shard.EntityChange.ExecutionContext ctx, Int3 space)
			{
				box = Box.OffsetSize(new Vec3(sector), Vec3.One, sector + 1 >= space);
				pool = new EntityPool(ctx);
				this.sector = sector;
			}

			public void Include(Entity e)
			{
				Debug.Assert(box.Contains(e.ID.Position));
				if (!pool.Insert(e))
					throw new Exception("Failed to insert " + e);
			}

			public SerialSDS Finish()
			{
				SDS sds = new SDS(0, pool.ToArray(), InconsistencyCoverage.NewCommon());
				return new SerialSDS(sds, sector);
			}
		}



		static void SetupScenario(BaseDB.ConfigContainer cfg, IEnumerable<Entity> entities)
		{
			BaseDB.PutConfigAsync(cfg).Wait();
			//Simulation.Configure(new ShardID(), cfg, true);
			//if (DB.HasAdminAccess)
			var clearTask = BaseDB.ClearSimulationDataAsync();

			SDSFactory[,,] grid = new SDSFactory[cfg.extent.X, cfg.extent.Y, cfg.extent.Z];
			cfg.extent.Cover(at =>
			{
				grid[at.X, at.Y, at.Z] = new SDSFactory(at, null/*new SimulationContext(false)*/, cfg.extent);
			}
			);
			var gridBox = IntBox.FromMinAndMax(Int3.Zero, cfg.extent, Bool3.False);
			var simulationSpace = Box.FromMinAndMax(Vec3.Zero, new Vec3(cfg.extent), Bool3.True);
			foreach (var e in entities)
			{
				if (!simulationSpace.Contains(e.ID.Position))
					throw new Exception("Scenario entity " + e + " is located outside simulation space");
				var cell = gridBox.Clamp(e.ID.Position.FloorInt3);
				grid[cell.X, cell.Y, cell.Z].Include(e);
			}

			clearTask.Wait();
			Task[] tasks = new Task[cfg.extent.Product];
			int idx = 0;
			foreach (var factory in grid)
			{
				tasks[idx++] = DB.PutAsync(factory.Finish(), true);
			}

			Task.WaitAll(tasks);
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
			SetupScenario(new BaseDB.ConfigContainer()
			{
				extent = new Int3(scenario.worldSize[0], scenario.worldSize[1], scenario.worldSize[2]),
				m = scenario.M,
				r = scenario.R
			},
						Translate(scenario.entities, providerMap, rng));
			//			GenerateEntities(1000));

		}

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


		static void Main(string[] args)
		{
			try
			{
				int at = 0;
				var dbHost = new Address(args[at++]);
				BaseDB.Connect(dbHost);//,"admin","1234");
				bool success = BaseDB.TryPullGlobalConfig(3);
				CreateScenario();
				Log.Message("Scenario set up. Shutting down");
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine(ex);
				Environment.Exit(-1);
			}
		}
	}
}
