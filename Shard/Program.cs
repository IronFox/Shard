using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	class Program
	{
		static void Main(string[] args)
		{
			//shard 
			//shard localhost:2018 1-2-3 16-16-4 0.1 0.05

			if (args.Length != 2)
			{
				Console.Error.WriteLine("Expected 2 parameters: [db url] [my addr], found "+args.Length);
				return;
			}

			{
				int at = 0;
				var dbHost = new Host(args[at++]);
				DB.Start(dbHost);
				ShardID addr = ShardID.Decode(args[at++]);

				if ((addr >= DB.Config.extent).Any)
					throw new ArgumentOutOfRangeException("addr", addr, "Exceeds extent: " + DB.Config.extent);
				if ((addr < ShardID.Zero).Any)
					throw new ArgumentOutOfRangeException("addr", addr, "Is (partially) negative");

				Simulation.Init(addr);
			}

		}
	}
}
