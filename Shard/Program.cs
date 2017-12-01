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

		static void SetupScenario(Host dbHost, DB.ConfigContainer cfg, int numEntities, Func<Entity> entityFactory)
		{
			DB.Connect(dbHost);
			DB.PutConfig(cfg);

			SDSFactory[,,] grid = new SDSFactory[cfg.extent.X, cfg.extent.Y, cfg.extent.Z];
			cfg.extent.XYZ.Cover(at =>
				{
					grid[at.X, at.Y, at.Z] = new SDSFactory(Box.OffsetSize(new Vec3(at), Vec3.One, at+1 >= cfg.extent.XYZ));
				}
			);
			for (int i = 0; i < numEntities; i++)
			{
				Entity e = entityFactory();
				var cell = Int3.Min( e.ID.Position.FloorInt3, cfg.extent.XYZ);
				grid[cell.X, cell.Y, cell.Z].Include(e);
			}

			Task[] tasks = new Task[cfg.extent.XYZ.Product];
			int at = 0;
			foreach (var factory in grid)
			{
				tasks[at++] = DB.PutAsyncTask(factory.Finish(),true);
			}

			Task.WaitAll(tasks);

		}


		static void Main(string[] args)
		{
			//shard 
			//shard localhost:2018 1-2-3 16-16-4 0.1 0.05

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
