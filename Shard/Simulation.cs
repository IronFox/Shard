using System;
using VectorMath;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Globalization;

namespace Shard
{
	public class Simulation
	{
		public readonly ShardID ID;
		private readonly ShardID ext;
		private readonly float r;
		private readonly float m;
		private readonly DBConnector db;
		
		public List<Link> neighbors = new List<Link>();
		private Link[] siblings;
		private Listener listener;


		private List<SDS> sdsList = new List<SDS>();


		public IEnumerable<Link> Neighbors { get { return neighbors; } }

		public Link FindLink(ShardID id)
		{
			if (id.XYZ == ID.XYZ)
			{
				foreach (var s in siblings)
					if (s.ID == id)
						return s;
				throw new Exception("Unable to find sibling shard with ID "+id);
			}
			foreach (var n in neighbors)
				if (n.ID == id)
					return n;
			throw new Exception("Unable to find neighbor shard with ID " + id);
		}

		public Simulation(ShardID addr, DBConnector db)
		{
			//Host.Domain = ;

			ID = addr;
			this.ext = db.Config.extent;
			this.r = db.Config.r;
			this.m = db.Config.m;
			this.db = db;


			if (ext.ReplicaLevel > 1)
			{
				int at = 0;
				siblings = new Link[ext.ReplicaLevel - 1];
				for (int i = 0; i < ext.ReplicaLevel; i++)
					if (i != addr.ReplicaLevel)
					{
						siblings[at] = new Link(new ShardID(addr.XYZ, i), this, i > addr.ReplicaLevel);
						at++;
					}
			}

			{
				for (int x = addr.X - 1; x <= addr.X + 1; x++)
					for (int y = addr.Y - 1; y <= addr.Y + 1; y++)
						for (int z = addr.Z - 1; z <= addr.Z + 1; z++)
						{
							Int3 a = new Int3(x, y, z);
							if (a == addr.XYZ)
								continue;

							if ((a >= Int3.Zero).All && (a < ext.XYZ).All)
								neighbors.Add( new Link(new ShardID(a, addr.ReplicaLevel), this, a.OrthographicCompare(addr.XYZ) > 0) );
						}
			}

			listener = new Listener(this);

			Console.Write("Polling SDS state...");

			SDS sds;
			while (true)
			{
				sds = db.LoadLatest(addr.XYZ);
				if (sds != null)
					break;
				Thread.Sleep(1000);
				Console.Write('.');
			}
			Console.WriteLine(" done");
			sdsList.Add(sds);

			DateTime startDate = DateTime.Parse(db.Config.start,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal);
			Console.WriteLine("Start Date="+startDate);
			int timeStep = (int) ( (DateTime.Now - startDate).TotalMilliseconds / db.Config.msPerTimeStep );
			Console.WriteLine("Time Step="+timeStep);

			List<RCS.ID> queryRCS = new List<RCS.ID>();
			foreach (var link in neighbors)
				for (int i = sds.Generation + 1; i < timeStep; i++)
				{
					queryRCS.Add(new RCS.ID(link.ID.XYZ, addr.XYZ, i));
				}
			db.BeginFetch(queryRCS);


			Debug.Fail("done");

		}

		internal void Run()
		{
			throw new NotImplementedException();
		}

		internal void FetchIncoming(object obj)
		{
			if (obj is SDS)
			{

			}
		}
	}
}