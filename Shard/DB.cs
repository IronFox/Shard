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
	public static class DB
	{
		//private IServerConnection server;

		private static string url;

		public class ConfigContainer
		{
			public string _id;  //"current"
			public string _rev;
			public ShardID extent;
			public float r, m;
			public int msPerTimeStep,   //total milliseconds per timestep
						recoverySteps;  //number of sub-steps per timestep (effectively splitting msPerTimeStep)
			public string start;
		}

		public static ConfigContainer Config { get; private set; }


		private static MyCouchStore sdsStore, rcsStore, logicStore;


		public static void Start(Host host, string username=null, string password = null)
		{
			url = "http://";
			if (username != null && password != null)
				url += Uri.EscapeDataString(username) + ":" + Uri.EscapeDataString(password) + "@";
			url += host;

			sdsStore = new MyCouchStore(url, "sds");
			rcsStore = new MyCouchStore(url, "rcs");
			logicStore = new MyCouchStore(url, "logic");

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


		class ContinuousPoller<T> where T : class
		{
			public Task<T> Task { get; private set; }
			private Thread pollThread;
			private MyCouchStore store;
			private string id;

			public ContinuousPoller()
			{
			}

			public Task<T> Start(MyCouchStore store, string id)
			{
				this.store = store;
				this.id = id;
				Task = store.GetByIdAsync<T>(id);
				if (Task.IsCompleted)
					return Task;

				pollThread = new Thread(new ThreadStart(ThreadedPoll));
				return Task;
			}

			private void ThreadedPoll()
			{
				while (true)
				{
					try
					{
						if (Task.Result != null)
							return;
					}
					catch (Exception ex)
					{
						Console.Error.WriteLine(ex);
						Thread.Sleep(1000);
						Task = store.GetByIdAsync<T>(id);
					}
				}
			}

			public T TryGet()
			{
				if (Task == null || !Task.IsCompleted)
					return null;
				return Task.Result;
			}
		}


		public static SDS.Serial LoadLatest(Int3 myID)
		{
			//sdsStore.QueryAsync(new Query(new ViewIdentity()))
			return sdsStore.GetByIdAsync<SDS.Serial>(myID.Encoded).Result;
		}

		private class MyLogicState : EntityLogic.State
		{
			readonly MyLogic logic;
			public MyLogicState(MyLogic logic, byte[] binaryState)
			{
				this.logic = logic;
			}

			public override byte[] BinaryState => throw new NotImplementedException();

			public override string LogicID => logic.ID;


			public override Changes Evolve(Entity currentState)
			{
				throw new NotImplementedException();
			}
		}

		private class MyLogic : EntityLogic
		{
			public class Serial
			{
				public string _id;  //"current"
				public string _rev;
			}

			public readonly string ID;

			public MyLogic(Serial serial)
			{
				ID = serial._id;
				throw new NotImplementedException();
			}

			public override State Instantiate(byte[] binaryState)
			{
				return new MyLogicState(this,binaryState);
			}
		}



		private static ConcurrentDictionary<RCS.GenID, ContinuousPoller<RCS.Serial>> rcsRequests = new ConcurrentDictionary<RCS.GenID, ContinuousPoller<RCS.Serial>>();
		private static ConcurrentDictionary<SDS.ID, ContinuousPoller<SDS.Serial>> sdsRequests = new ConcurrentDictionary<SDS.ID, ContinuousPoller<SDS.Serial>>();
		private static ConcurrentDictionary<string, ContinuousPoller<MyLogic.Serial>> logicRequests = new ConcurrentDictionary<string, ContinuousPoller<MyLogic.Serial>>();
		private static ConcurrentDictionary<string, MyLogic> loadedLogics = new ConcurrentDictionary<string, MyLogic>();

		public static void BeginFetch(RCS.GenID id)
		{
			if (rcsRequests.ContainsKey(id))
				return;
			ContinuousPoller<RCS.Serial> poller = new ContinuousPoller<RCS.Serial>();
			if (!rcsRequests.TryAdd(id, poller))
				return;
			poller.Start(rcsStore, id.ToString());
		}

		public static RCS.Serial TryGet(RCS.GenID id)
		{
			ContinuousPoller<RCS.Serial> poller;
			if (rcsRequests.TryGetValue(id,out poller))
				return poller.TryGet();
			BeginFetch(id);
			return TryGet(id); ;
		}


		public static void BeginFetchLogic(string id)
		{
			if (logicRequests.ContainsKey(id))
				return;
			ContinuousPoller<MyLogic.Serial> poller = new ContinuousPoller<MyLogic.Serial>();
			if (!logicRequests.TryAdd(id, poller))
				return;
			poller.Start(rcsStore, id);
		}

		public static EntityLogic TryGetLogic(string id)
		{
			MyLogic rs;
			if (loadedLogics.TryGetValue(id, out rs))
				return rs;
			ContinuousPoller<MyLogic.Serial> poller;
			if (!logicRequests.TryGetValue(id, out poller))
			{
				BeginFetchLogic(id);
				return TryGetLogic(id);
			}
			MyLogic.Serial serial = poller.TryGet();
			if (serial == null)
				return null;
			rs = new MyLogic(serial);
			if (!loadedLogics.TryAdd(id, rs))
				return TryGetLogic(id);
			return rs;
		}

		public static void BeginFetch(IEnumerable<RCS.GenID> ids)
		{
			foreach (var id in ids)
				BeginFetch(id);
		}

		internal static void Put(RCS.Serial serial)
		{
			throw new NotImplementedException();
		}
	}
}