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

			if (args.Length != 6)
			{
				Console.Error.WriteLine("Expected 6 parameters: [db url] [domain] [my addr] [grid size] [R] [M], found "+args.Length);
				return;
			}

			Simulation sim;
			DBConnector db;
			{
				int at = 0;
				db = new DBConnector(args[at++]);
				string domain = args[at++];
				ShardID addr = ShardID.Decode(args[at++]);
				ShardID ext = ShardID.Decode(args[at++]);
				float r = float.Parse(args[at++]);
				float m = float.Parse(args[at++]);

				if ((addr >= ext).Any)
					throw new ArgumentOutOfRangeException("addr",addr, "Exceeds extent: "+ext);
				if ((addr < ShardID.Zero).Any)
					throw new ArgumentOutOfRangeException("addr", addr, "Is (partially) negative");

				if (m < 0)
					throw new ArgumentOutOfRangeException("M", m, "Is negative");
				if (m > r)
					throw new ArgumentOutOfRangeException("M", m, "Exceeds R ("+r+")");

				sim = new Simulation(domain, addr, ext, r, m, db);
			}

			sim.Run();

		}
	}
}
