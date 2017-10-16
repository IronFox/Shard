using System;
using VectorMath;
using System.Diagnostics;
using Divan;

namespace Shard
{
	public class Simulation
	{
		public readonly ShardID ID;
		private readonly ShardID ext;
		private readonly float r;
		private readonly float m;
		private readonly DBConnector db;

		private class Config : CouchDocument
		{
			public string Make;
			public string Model;
			public int HorsePowers;

			public Config(string id, string rev) : base(id, rev)
			{
			}

			public Config(string id) : base(id)
			{
			}

			public Config()
			{
			}

			public Config(System.Collections.Generic.IDictionary<string, global::Newtonsoft.Json.Linq.JToken> doc) : base(doc)
			{
			}

			public Car()
			{
				// This constructor is needed by Divan
			}

			public Car(string make, string model, int hps)
			{
				Make = make;
				Model = model;
				HorsePowers = hps;
			}
			#region CouchDocument Members

			public override void WriteJson(JsonWriter writer)
			{
				// This will write id and rev
				base.WriteJson(writer);

				writer.WritePropertyName("docType");
				writer.WriteValue("car");
				writer.WritePropertyName("Make");
				writer.WriteValue(Make);
				writer.WritePropertyName("Model");
				writer.WriteValue(Model);
				writer.WritePropertyName("Hps");
				writer.WriteValue(HorsePowers);
			}

			public override void ReadJson(JObject obj)
			{
				// This will read id and rev
				base.ReadJson(obj);

				Make = obj["Make"].Value<string>();
				Model = obj["Model"].Value<string>();
				HorsePowers = obj["Hps"].Value<int>();
			}

			#endregion
		}
		
		private Link[] neighbors = new Link[26];
		private Link[] siblings;
		private Listener listener;

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

		public Simulation(string domain, ShardID addr, ShardID ext, float r, float m, DBConnector db)
		{
			Host.Domain = domain;

			ID = addr;
			this.ext = ext;
			this.r = r;
			this.m = m;
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
				int at = 0;
				for (int x = addr.X - 1; x <= addr.X + 1; x++)
					for (int y = addr.Y - 1; y <= addr.Y + 1; y++)
						for (int z = addr.Z - 1; z <= addr.Z + 1; z++)
						{
							Int3 a = new Int3(x, y, z);
							if (a == addr.XYZ)
								continue;

							if ((a >= Int3.Zero).All && (a < ext.XYZ).All)
								neighbors[at] = new Link(new ShardID(a, addr.ReplicaLevel), this, a.OrthographicCompare(addr.XYZ) > 0);
							at++;
						}
				Debug.Assert(at == neighbors.Length);
			}

			listener = new Listener(this);

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