using MyCouch;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	public class DBConnector
	{
		//private IServerConnection server;

		private readonly string url;

		public struct Test
		{
			public float X, Y, Z;
			public int ReplicaLevel;

		}

		public class ConfigContainer
		{
			public string _id;  //"current"
			public string _rev;
			public ShardID extent;
			public float r,m;
			public int msPerTimeStep,recoverySteps;
			public string start;
		}

		public readonly ConfigContainer Config;

		private MyCouchStore sdsStore, rcsStore;


		public DBConnector(Host host, string username=null, string password = null)
		{
			url = "http://";
			if (username != null && password != null)
				url += Uri.EscapeDataString(username) + ":" + Uri.EscapeDataString(password) + "@";
			url += host;

			sdsStore = new MyCouchStore(url, "sds");
			rcsStore = new MyCouchStore(url, "rcs");
			var cfg = new MyCouchStore(url, "config");
			{
				for (int i = 0; i < 3; i++)
				{
					var job = cfg.GetByIdAsync<ConfigContainer>("current");
					Config = job.Result;
					if (Config != null)
						break;
					Thread.Sleep(5000);
				}
				if (Config == null)
					throw new Exception("Unable to fetch configuration");
			}
		}


		class ContinuousPoller<T, ID> where T : class
		{
			private Task<T> task;
			public ContinuousPoller()
			{
			}

			public void Start(ID id)
			{


			}

			public T TryGet()
			{
				if (task == null || !task.IsCompleted)
					return null;
				return task.Result;
			}
		}


		public SDS LoadLatest(Int3 myID)
		{
			return sdsStore.GetByIdAsync<SDS>(myID.Encoded).Result;
		}

		private ConcurrentDictionary<RCS.ID, ContinuousPoller<RCS,RCS.ID>> activeRequests = new ConcurrentDictionary<RCS.ID, ContinuousPoller<RCS, RCS.ID>>();

		public void BeginFetch(RCS.ID id)
		{
			if (activeRequests.ContainsKey(id))
				return;
			ContinuousPoller<RCS, RCS.ID> newPoller = new ContinuousPoller<RCS, RCS.ID>();
			if (!activeRequests.TryAdd(id, newPoller))
				return;
			newPoller.Start(id);
		}

		public void BeginFetch(IEnumerable<RCS.ID> ids)
		{
			foreach (var id in ids)
				BeginFetch(id);
		}
	}
}