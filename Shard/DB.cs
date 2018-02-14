using MyCouch;
using MyCouch.Requests;
using Newtonsoft.Json;
using Shard.EntityChange;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
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
			public ShardID extent = new ShardID(Int3.One,1);
			public float r = 0.5f, m=0.25f;
		}

		public class AddressEntry : Entity
		{
			public string address;
			public int port;

			public AddressEntry(ShardPeerAddress addr)
			{
				_id = addr.ShardID.ToString();
				address = addr.Address.Address;
				port = addr.Address.Port;
			}

			[JsonIgnore]
			public PeerAddress PeerAddress
			{
				get
				{
					return new PeerAddress(address, port);
				}
			}
		}

		public class TimingContainer : Entity
		{
			public int msStep = 1000;   //total milliseconds per step, where (1+recoverySteps) step comprise a top level generation
			public int msComputation = 500; //total milliseconds per step dedicated to computation. Must be less than msStep. Any extra time is used for communication
			public string startTime = DateTime.Now.ToString();  //starting time of the computation of generation #0
			public int recoverySteps = 2;  //number of steps per generation dedicated to recovery. The total number of steps per top level generation equals 1+recoverySteps
			public int maxGeneration = -1;  //maximum top level generation. Negative when disabled
			public int startGeneration = 0; //generation effective at startTime
		}



		public static ConfigContainer Config { get; private set; }


		public static PeerAddress Host { get; private set; }

		private static DataBase sdsStore, rcsStore, logicStore,controlStore, hostsStore;

		public static Func<string, Task<CSLogicProvider>> LogicLoader { get; set; }


		public static async Task<CSLogicProvider> PutCompiledLogicProviderAsync(string name, string sourceCode)
		{
			var provider = await CSLogicProvider.CompileAsync(name, sourceCode);
			await PutLogicProviderAsync(provider);
			return provider;
		}

		public static Task PutLogicProviderAsync(CSLogicProvider provider)
		{
			return PutLogicProviderAsync(new SerialCSLogicProvider( provider ));
		}

		public static Task PutLogicProviderAsync(string name, string sourceCode)
		{
			var serial = new SerialCSLogicProvider(name,sourceCode);

			return PutLogicProviderAsync(serial);
		}

		public static async Task PutLogicProviderAsync(SerialCSLogicProvider data)
		{
			if (logicStore == null)
				return;// Task.FromResult<object>(null);
			string serial = logicStore.Client.Serializer.Serialize(data);
			await logicStore.SetAsync(data._id, serial);
		}

		private static ConcurrentDictionary<string, CSLogicProvider> directProviderMap = new ConcurrentDictionary<string, CSLogicProvider>();

		public static async Task<CSLogicProvider> GetLogicProviderAsync(string scriptName)
		{
			CSLogicProvider prov;
			if (directProviderMap.TryGetValue(scriptName, out prov))
				return prov;


			if (LogicLoader != null)
			{
				var logic = await LogicLoader(scriptName);
				if (logic != null)
					return logic;
			}

			var script = await logicStore.GetByIdAsync<SerialCSLogicProvider>(scriptName);
			prov = await script.DeserializeAsync();
#if !DRY_RUN
			directProviderMap.TryAdd(scriptName, prov);
#endif
			return prov;
		}

		public static void Connect(PeerAddress host, string username = null, string password = null)
		{
			url = "http://";
			if (username != null && password != null)
				url += Uri.EscapeDataString(username) + ":" + Uri.EscapeDataString(password) + "@";
			url += host;
			Host = host;

			sdsStore = new DataBase(url, "sds");
			rcsStore = new DataBase(url, "rcs");
			logicStore = new DataBase(url, "logic");
			controlStore = new DataBase(url, "control");
			hostsStore = new DataBase(url, "hosts");
		}


		private static void Try(Func<bool> f, int numTries, int msBetweenRetries = 5000)
		{
			for (int i = 0; i < numTries; i++)
			{
				try
				{
					if (f())
						return;
				}
				catch
				{ }
				if (i + 1 < numTries)
				{
					Log.Message("No luck. Sleeping "+ msBetweenRetries + " mseconds...");
					Thread.Sleep(msBetweenRetries);
				}
			}
		}

		private static async Task<bool> TryAsync(Func<Task<bool>> f, int numTries, int msBetweenRetries = 5000)
		{
			for (int i = 0; i < numTries; i++)
			{
				try
				{
					if (await f())
						return true;
				}
				catch
				{ }
				if (i + 1 < numTries)
				{
					Log.Message("No luck. Sleeping " + msBetweenRetries + " mseconds...");
					await Task.Delay(msBetweenRetries);
				}
			}
			return false;
		}


		private static ContinuousPoller<TimingContainer> timingPoller;
		private static ContinuousPoller<SerialSDS> sdsPoller;

		public static TimingContainer Timing
		{
			get
			{
				return timingPoller.Latest;
			}
			set
			{
				value._id = timingPoller.ID;
				PutAsync(null, controlStore, value, true).Wait();
			}
		}



		public static void PullConfig(int numTries = 3)
		{
			Try(() =>
			{
				Log.Message("Fetching simulation configuration from " + Host + " ...");
				var job = controlStore.GetByIdAsync<ConfigContainer>("config");
				Config = job.Result;
				return Config != null;
			}, numTries);

			timingPoller = new ContinuousPoller<TimingContainer>(new TimingContainer());
			timingPoller.Start(controlStore, "timing");
		}

		public static async Task PutConfigAsync(ConfigContainer container, int numTries = 3)
		{
			Config = container;
			Config._id = "config";
			Config._rev = null;
			string doc = controlStore.Client.Serializer.Serialize(Config);
			bool success = await TryAsync(async () =>
			{
				Log.Minor("Storing simulation configuration on " + Host + " ...");
				var header = await controlStore.SetAsync("config", doc);
				Config = await controlStore.GetByIdAsync<ConfigContainer>(header.Id);
				return Config != null;
			}, numTries);
			if (success)
				Log.Minor("Done");
			else
				Log.Error("Failed to upload config to server");

		}

		public class Entity
		{
			public string _id, _rev;
			//public string EntityId, _rev;
		}


		class RevisionChange
		{
			public string rev = null;
		}

		class Change
		{
			public int seq = 0;
			public string id = null;
			public RevisionChange[] changes = null;
		}

		public class DataBase : MyCouchStore
		{
			public readonly string DBName;

			public DataBase(string url, string dbName) : base(url,dbName)
			{
				DBName = dbName;
			}
		}


		class ContinuousPoller<T> where T : Entity
		{
			private DataBase store;
			private string id;
			T lastQueriedValue;
			//TaskCompletionSource<T> anyValueAsync;

			public Action<T> OnChange { get; set; }
			SpinLock sl = new SpinLock();
			CancellationTokenSource cancellation;

			public ContinuousPoller(T initial)
			{
				lastQueriedValue = initial;
				//anyValueAsync = new TaskCompletionSource<T>();
				//if (initial != null)
				//	anyValueAsync.SetResult(initial);
			}


			public T Latest
			{
				get
				{
					return lastQueriedValue;
				}
			}

			public string ID { get { return id; } }

			//public async Task<T> GetLatestAsync()
			//{
			//	if (lastQueriedValue != null)
			//		return lastQueriedValue;
			//	return await anyValueAsync.Task;
			//}

			public void Start(DataBase store, string id)
			{
				//this.onChange = onChange;
				this.store = store;
				this.id = id;

				PollAsync().Wait();

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
						var d = serial.Deserialize<Change>(data);
						if (d != null && d.id == id)
						{
							StringBuilder revs = new StringBuilder();
							bool actualChange = false;
							foreach (var ch in d.changes)
							{
								revs.Append(' ').Append(ch.rev);
								if (lastQueriedValue == null || ch.rev != lastQueriedValue._rev)
									actualChange = true;
							}
							if (actualChange)
							{
								Log.Minor("Update detected on " + store.DBName + "['" + id + "']:" + revs + ". Polling");
								PollAsync().Wait();
							}
						}
					},
					cancellation.Token);

			}

			private async Task PollAsync()
			{
				for (int i = 0; i < 3; i++)
				{
					try
					{
						var header = await store.GetHeaderAsync(id);
						if (header == null)
							return;
						if (lastQueriedValue == null || header.Rev != lastQueriedValue._rev)
						{
							var data = await store.GetByIdAsync<T>(id);
							sl.DoLocked(() =>
							{
								Log.Minor("Got new data on " + store.DBName + "['" + id + "']: rev " + header.Rev);
								//if (lastQueriedValue == null)
								//	anyValueAsync.SetResult(data);
								lastQueriedValue = data;
								OnChange?.Invoke(data);
							});
						}
						return;
					}
					catch (TaskCanceledException)
					{}
				}
			}
		}


		public static SerialSDS Begin(Int3 myID)
		{
			sdsPoller = new ContinuousPoller<SerialSDS>(null);
			sdsPoller.Start(sdsStore, myID.Encoded);
			sdsPoller.OnChange = serial => Simulation.FetchIncoming(null, serial);
			return sdsPoller.Latest;
		}


		public class MappedContinuousLookup<K,D> where D: Entity
		{
			private ConcurrentDictionary<K, ContinuousPoller<D>> requests = new ConcurrentDictionary<K, ContinuousPoller<D>>();

			public void BeginFetch(DataBase store, K id)
			{
				if (requests.ContainsKey(id))
					return;
				if (store == null)
					return;
				ContinuousPoller<D> poller = new ContinuousPoller<D>(null);
				if (!requests.TryAdd(id, poller))
					return;
				poller.Start(store, id.ToString());
			}

			public D TryGet(DataBase store, K id)
			{
				ContinuousPoller<D> poller;
				if (requests.TryGetValue(id, out poller))
					return poller.Latest;

				if (store == null)
					return null;
				BeginFetch(store, id);
				return TryGet(store,id);
			}
		}


		private static MappedContinuousLookup<RCS.ID, SerialRCSStack> rcsRequests = new MappedContinuousLookup<RCS.ID, SerialRCSStack>();
		private static MappedContinuousLookup<ShardID, AddressEntry> hostRequests = new MappedContinuousLookup<ShardID, AddressEntry>();

		public static void BeginFetch(RCS.ID id)
		{
			rcsRequests.BeginFetch(rcsStore, id);
		}

		public static SerialRCSStack TryGet(RCS.ID id)
		{
			return rcsRequests.TryGet(rcsStore, id);
		}

		public static void BeginFetch(ShardID id)
		{
			hostRequests.BeginFetch(hostsStore, id);
		}

		public static PeerAddress TryGet(ShardID id)
		{
			var h = hostRequests.TryGet(hostsStore, id);
			if (h == null)
				return new PeerAddress();
			return h.PeerAddress;
		}


		public static void BeginFetch(IEnumerable<RCS.ID> ids)
		{
			foreach (var id in ids)
				BeginFetch(id);
		}

		public static Action<SerialSDS> OnPutSDS { get; set; } = null;
		public static Action<ShardPeerAddress> OnPutLocalAddress { get; set; } = null;
		public static EntityRanges EntityRanges
		{
			get
			{
				var cfg = Config;
				return new EntityRanges(cfg.r, cfg.m, cfg.r - cfg.m, Simulation.ExtToWorld(cfg.extent.XYZ));

			}
		}

		public class AsyncRCSStack
		{
			SerialRCSStack state = new SerialRCSStack();  //protected by stateLock
			SemaphoreSlim stateLock = new SemaphoreSlim(1); //protects lastKnownState


			public AsyncRCSStack(RCS.ID id)
			{
				state._id = id.ToString();
				state.NumericID = id.IntArray;
			}

			public async Task ChangeAsync(Func<SerialRCSStack, Task> op)
			{
				await stateLock.DoLockedAsync(async () =>
				{
					await op(state);
				});
			}

			public async Task ChangeAsync(Action<SerialRCSStack> op)
			{
				await stateLock.DoLockedAsync(() =>
				{
					op(state);
				});
			}

			public async Task ReplaceAsync(Func<SerialRCSStack, Task<SerialRCSStack>> op)
			{
				await stateLock.DoLockedAsync(async () =>
				{
					state = await op(state);
				});
			}

			public async Task ReplaceAsync(Func<SerialRCSStack, SerialRCSStack> op)
			{
				await stateLock.DoLockedAsync(() =>
				{
					state = op(state);
				});
			}

		}

		public class RCSStack
		{
			AsyncRCSStack lastKnownState;
			Task rcsPutTask;
			readonly RCS.ID myID;

			public Action<RCS.SerialData, int> OnPutRCS { get; set; } = null;


			public RCSStack(RCS.ID id)
			{
				lastKnownState = new AsyncRCSStack(id);
				myID = id;
			}

			public async Task SignalOldestGenerationUpdateAsync(int replicationIndex, int oldestGeneration, int simulationTopGeneration)
			{
				await lastKnownState.ChangeAsync((stack) =>
				{
					int oldGen = stack.GetOldestGeneration();
					stack.Destinations.Set(replicationIndex, simulationTopGeneration, oldestGeneration);

					int newGen = stack.GetOldestGeneration();
					if (oldGen > newGen)
					{
						throw new IntegrityViolation("Local generation offset changed backwards");
					}
					if (newGen > oldGen && stack.Entries != null)
					{
						int offset = (newGen - oldGen);
						int remaining = stack.Entries.Length - offset;
						if (remaining <= 0)
						{
							stack.Entries = null;
						}
						else
						{
							var entries = new RCS.SerialData[remaining];
							for (int i = 0; i < remaining; i++)
								entries[i] = stack.Entries[i + offset];
							stack.Entries = entries;
						}
					}
				});
			}

			public void Put(int generation, RCS data)
			{
				if (!data.IsFullyConsistent)
					throw new IntegrityViolation("Trying to upload inconsistent RCS " + myID + " at generation " + generation);
				if (rcsPutTask != null)
					rcsPutTask.Wait();
				rcsPutTask = PutAsync(generation, data.Export());
			}

			public async Task ViewAsync(Action<SerialRCSStack> action)
			{
				await lastKnownState.ChangeAsync(action);
			}

			public async Task PutAsync(int generation, RCS.SerialData data)
			{
				await lastKnownState.ReplaceAsync(async (stack) =>
				{
					int at = generation - stack.GetOldestGeneration();
					if (stack.Entries == null || stack.Entries.Length <= at)
					{
						var entries = new RCS.SerialData[at + 1];
						if (stack.Entries != null)
							for (int i = 0; i < stack.Entries.Length; i++)
								entries[i] = stack.Entries[i];
						stack.Entries = entries;
					}
					stack.Entries[at] = data;

					OnPutRCS?.Invoke(stack.Entries[at], generation);

					if (rcsStore == null)	//for testing: DB not initialized. Return modified stack for replacement
						return stack;
					for (int i = 0; i < 10; i++)
					{
						try
						{
							return await rcsStore.StoreAsync(stack);	
						}
						catch (MyCouchResponseException ex)
						{
							Log.Error(ex);
							var existing = await rcsStore.GetByIdAsync<SerialRCSStack>(stack._id);
							stack.IncludeNewerVersion(existing);
						}
					};
					throw new Exception("Failed to update local RCS 10 times");
				});
			}
			
		}

		internal static void PutNow(SerialSDS serial, bool forceReplace)
		{
			if (OnPutSDS != null)
				OnPutSDS(serial);
			Try(() =>
			{
				PutAsync(null,sdsStore,serial, forceReplace).Wait();
				return true;
			}, 3);
		}

		internal static void PutNow(ShardPeerAddress addr, bool forceReplace)
		{
			if (OnPutLocalAddress != null)
				OnPutLocalAddress(addr);
			Try(() =>
			{
				PutAsync(null, hostsStore, new AddressEntry(addr), forceReplace).Wait();
				return true;
			}, 3);
		}


		private static SerialSDS latestPut = null;
		public static async Task PutAsync(SerialSDS serial, bool forceReplace)
		{
			if (sdsStore == null)
				return;	//tests
			try
			{
				Log.Minor("Storing serial SDS in DB: g" + serial.Generation);
				if (sdsPoller != null)
				{
					var latest = sdsPoller.Latest;
					if (latestPut != null && latestPut.Generation > latest.Generation)
						latest = latestPut;
					if (serial.Generation <= latest.Generation)
					{
						Log.Minor("Newer already online. Rejecting update");
						return; //nothing to do
					}
					serial._rev = latest._rev;
				}
				await PutAsync(null, sdsStore, serial, forceReplace);
				latestPut = serial;
				Log.Minor("Stored serial SDS in DB: g" + serial.Generation);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
				Log.Error("Failed to store serial SDS in DB: g" + serial.Generation);
			}
		}

		private static async Task PutAsync(Link lnk, MyCouchStore store, Entity e, bool forceReplace)
		{
			string doc = store.Client.DocumentSerializer.Serialize(e);

			if (forceReplace)
			{
				while (true)
				{
					try
					{
						await store.SetAsync(e._id, doc);
						return;
					}
					catch (MyCouchResponseException)
					{ }
				}
				//await store.DeleteAsync(e._id);
			}
			try
			{
				var header = e._rev != null?
					await store.StoreAsync(e._id, e._rev, doc):
					await store.StoreAsync(e._id, doc);

				e._rev = header.Rev;
			}
			catch (MyCouchResponseException)
			{
				if (lnk != null)
				{
					var e2 = store.GetByIdAsync(e._id); //if we get here, then the copy script has rejected our data => read data must be newer
					Simulation.FetchIncoming(lnk, e2);
				}
			}
		}

		public class Serializer
		{
			MyCouch.Serialization.ISerializer serializer;

			public Serializer()
			{
				//				serializer = new MyCouch.Serialization.DefaultSerializer(new MyCouch.Serialization.SerializationConfiguration();
				using (var cl = new MyCouch.MyCouchClient("https://127.0.0.1", "none"))
				{
					serializer = cl.Entities.Serializer;
				}
				Console.WriteLine(serializer.GetType());
			}

			public T Deserialize<T>(string data)
			{
				return serializer.Deserialize<T>(data);
			}

			public T Deserialize<T>(Stream data)
			{
				return serializer.Deserialize<T>(data);
			}

			public void Populate<T>(T item, Stream data) where T : class
			{
				serializer.Populate(item, data);
			}

			public void Populate<T>(T item, string json) where T : class
			{
				serializer.Populate(item, json);
			}

			public string Serialize<T>(T item) where T : class
			{
				return serializer.Serialize(item);
			}

		}

	}
}