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
				SDS sds = new SDS(0, pool.ToArray(), InconsistencyCoverage.NewCommon(), new SDS.IntermediateData(), null);
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
			foreach (var e in entities)
			{
				var cell = Int3.Min( e.ID.Position.FloorInt3, cfg.extent.XYZ);
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
					

					yield return new Entity(new EntityID(pos), new DynamicCSLogic(GetProvider(se.logic), null), appearances, null, null);
				}
				
			}
		}


		public class ScenarioConfig
		{
			public int[] worldSize;
			public float R, M;
			public ScenarioEntity[] entities;
		}

		public class ScenarioEntity
		{
			public float[] position;
			public string logic;
			public dynamic[] appearances;
			public int instances = 1;
		}




		static void Main(string[] args)
		{

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
