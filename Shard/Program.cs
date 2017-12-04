using System;
using System.Collections.Generic;
using System.Diagnostics;
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


		static void Main(string[] args)
		{

			//SetupScenario(new Host("localhost", 1024),
			//			new DB.ConfigContainer() { m = 0.05f, start = (DateTime.Now.ToUniversalTime() + TimeSpan.FromHours(1)).ToString() },
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
