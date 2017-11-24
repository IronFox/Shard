using MyCouch;
using MyCouch.Requests;
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

		public class ConfigContainer : Entity
		{
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

		public class Entity
		{
			public string _id, _rev;
		}

		class ContinuousPoller<T> where T : Entity
		{
			private MyCouchStore store;
			private string id;
			T lastValue;
			private Action<T> onChange;
			SpinLock sl = new SpinLock();
			CancellationTokenSource cancellation;

			public ContinuousPoller()
			{
			}


			public T Latest
			{
				get
				{
					return lastValue;
				}
			}

			public void Start(MyCouchStore store, string id, Action<T> onChange)
			{
				this.onChange = onChange;
				this.store = store;
				this.id = id;

				PollAsync();

				var getChangesRequest = new GetChangesRequest
				{
					Feed = ChangesFeed.Continuous,
					Heartbeat = 3000 //Optional: LET COUCHDB SEND A I AM ALIVE BLANK ROW EACH ms
				};


				var serial = store.Client.Entities.Serializer;
				cancellation = new CancellationTokenSource();
				store.Client.Changes.GetAsync(
					getChangesRequest,
					data =>
					{
						var d = serial.Deserialize<T>(data);
						sl.DoLocked(() =>
						{
							if (d._id == id && d._rev != lastValue._rev)
							{
								lastValue = d;
								onChange?.Invoke(d);
							}
						});
					},
					cancellation.Token);

			}

			private async void PollAsync()
			{
				var header = await store.GetHeaderAsync(id);
				if (header.Rev != lastValue._rev)
				{
					var data = await store.GetByIdAsync<T>(id);
					sl.DoLocked(()=>
					{
						lastValue = data;
						onChange?.Invoke(data);
					});
				}
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


			public override Changes Evolve(Shard.Entity currentState, int generation, Random randomSource)
			{
				throw new NotImplementedException();
			}
		}

		private class MyLogic : EntityLogic
		{
			public class Serial : Entity
			{
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



		private static ConcurrentDictionary<RCS.ID, ContinuousPoller<SerialRCSStack>> rcsRequests = new ConcurrentDictionary<RCS.ID, ContinuousPoller<SerialRCSStack>>();
		private static ConcurrentDictionary<SDS.ID, ContinuousPoller<SDS.Serial>> sdsRequests = new ConcurrentDictionary<SDS.ID, ContinuousPoller<SDS.Serial>>();
		private static ConcurrentDictionary<string, ContinuousPoller<MyLogic.Serial>> logicRequests = new ConcurrentDictionary<string, ContinuousPoller<MyLogic.Serial>>();
		private static ConcurrentDictionary<string, MyLogic> loadedLogics = new ConcurrentDictionary<string, MyLogic>();

		public static void BeginFetch(RCS.ID id)
		{
			if (rcsRequests.ContainsKey(id))
				return;
			if (rcsStore == null)
				return;
			ContinuousPoller<SerialRCSStack> poller = new ContinuousPoller<SerialRCSStack>();
			if (!rcsRequests.TryAdd(id, poller))
				return;
			poller.Start(rcsStore, id.ToString(),null);
		}

		public static SerialRCSStack TryGet(RCS.ID id)
		{
			ContinuousPoller<SerialRCSStack> poller;
			if (rcsRequests.TryGetValue(id,out poller))
				return poller.Latest;
			if (rcsStore == null)
				return null;
			BeginFetch(id);
			return TryGet(id);
		}


		public static void BeginFetchLogic(string id)
		{
			if (logicRequests.ContainsKey(id))
				return;
			ContinuousPoller<MyLogic.Serial> poller = new ContinuousPoller<MyLogic.Serial>();
			if (!logicRequests.TryAdd(id, poller))
				return;
			poller.Start(rcsStore, id,null);
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
			MyLogic.Serial serial = poller.Latest;
			if (serial == null)
				return null;
			rs = new MyLogic(serial);
			if (!loadedLogics.TryAdd(id, rs))
				return TryGetLogic(id);
			return rs;
		}

		public static void BeginFetch(IEnumerable<RCS.ID> ids)
		{
			foreach (var id in ids)
				BeginFetch(id);
		}

		public static Action<SDS.Serial> OnPutSDS { get; set; } = null;



		public class RCSStack
		{
			SerialRCSStack last;
			Task rcsPutTask;
			readonly RCS.ID myID;

			public Action<RCS.SerialData, int> OnPutRCS { get; set; } = null;


			public RCSStack(RCS.ID id)
			{
				last = new SerialRCSStack();
				last._id = id.ToString();
				last.NumericID = id.IntArray;
				myID = id;
			}

			public void SignalOldestGenerationUpdate(int replicationIndex, int oldestGeneration, int simulationTopGeneration)
			{
				int oldGen = last.GetOldestGeneration();
				if (last.Destinations == null || last.Destinations.Length <= replicationIndex)
				{
					var nd = new SerialRCSStack.Destination[replicationIndex + 1];
					if (last.Destinations != null)
						for (int i = 0; i < last.Destinations.Length; i++)
							nd[i] = last.Destinations[i];
					last.Destinations = nd;
				}
				last.Destinations[replicationIndex].LastUpdateTimeStep = simulationTopGeneration;
				last.Destinations[replicationIndex].OldestGeneration = oldestGeneration;

				int newGen = last.GetOldestGeneration();
				if (oldGen > newGen)
					throw new IntegrityViolation("Local generation offset changed backwards");
				if (newGen > oldGen && last.Entries != null)
				{
					int offset = (newGen - oldGen);
					int remaining = last.Entries.Length - offset;
					if (remaining <= 0)
					{
						last.Entries = null;
					}
					else
					{
						var entries = new RCS.SerialData[remaining];
						for (int i = 0; i < remaining; i++)
							entries[i] = last.Entries[i + offset];
						last.Entries = entries;
					}
				}
			}

			public void Put(int generation, RCS data)
			{
				if (!data.IsFullyConsistent)
					throw new IntegrityViolation("Trying to upload inconsistent RCS " + myID + " at generation " + generation);
				if (rcsPutTask != null)
					rcsPutTask.Wait();
				rcsPutTask = PutAsync(generation, data.Export());
			}

			public async Task PutAsync(int generation, RCS.SerialData data)
			{
				int at = generation - last.GetOldestGeneration();
				if (last.Entries == null || last.Entries.Length <= at)
				{
					var entries = new RCS.SerialData[at + 1];
					if (last.Entries != null)
						for (int i = 0; i < last.Entries.Length; i++)
							entries[i] = last.Entries[i];
					entries[at] = data;
					last.Entries = entries;
				}

				OnPutRCS?.Invoke(last.Entries[at], generation);

				int cnt = 0;
				while (!await PutAsync())
				{
					cnt++;
					if (cnt > 10)
						throw new Exception("Failed to update local RCS "+cnt+" times");
				};
			}
			private async Task<bool> PutAsync()
			{
				try
				{
					last = await rcsStore.StoreAsync(last);
					return true;
				}
				catch (MyCouchResponseException ex)
				{
					Log.Error(ex);
					var existing = await rcsStore.GetByIdAsync<SerialRCSStack>(last._id);
					last.IncludeNewerVersion(existing);
					return false;
				}
			}
		}


		internal static void Put(SDS.Serial serial)
		{
			if (OnPutSDS != null)
				OnPutSDS(serial);
		}
	}
}