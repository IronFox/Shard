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


		public static Host Host { get; private set; }

		private static DataBase sdsStore, rcsStore, logicStore,controlStore;

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

		public static async Task<CSLogicProvider> GetLogicProviderAsync(string scriptName)
		{
			if (LogicLoader != null)
			{
				var logic = await LogicLoader(scriptName);
				if (logic != null)
					return logic;
			}

			var script = await logicStore.GetByIdAsync<SerialCSLogicProvider>(scriptName);
			return script.Deserialize();
		}

		public static void Connect(Host host, string username = null, string password = null)
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
				Log.Message("Storing simulation configuration on " + Host + " ...");
				var header = await controlStore.SetAsync("config", doc);
				Config = await controlStore.GetByIdAsync<ConfigContainer>(header.Id);
				return Config != null;
			}, numTries);
			if (success)
				Log.Message("Done");
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

		class DataBase : MyCouchStore
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
			T lastValue;
			public Action<T> OnChange { get; set; }
			SpinLock sl = new SpinLock();
			CancellationTokenSource cancellation;

			public ContinuousPoller(T initial)
			{
				lastValue = initial;
			}


			public T Latest
			{
				get
				{
					return lastValue;
				}
			}

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
								if (lastValue == null || ch.rev != lastValue._rev)
									actualChange = true;
							}
							if (actualChange)
							{
								Log.Message("Update detected on " + store.DBName + "['" + id + "']:" + revs + ". Polling");
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
						if (lastValue == null || header.Rev != lastValue._rev)
						{
							var data = await store.GetByIdAsync<T>(id);
							sl.DoLocked(() =>
							{
								Log.Message("Got new data on " + store.DBName + "['" + id + "']: rev " + header.Rev);
								lastValue = data;
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



		private static ConcurrentDictionary<RCS.ID, ContinuousPoller<SerialRCSStack>> rcsRequests = new ConcurrentDictionary<RCS.ID, ContinuousPoller<SerialRCSStack>>();
		//private static ConcurrentDictionary<SDS.ID, ContinuousPoller<SDS.Serial>> sdsRequests = new ConcurrentDictionary<SDS.ID, ContinuousPoller<SDS.Serial>>();

		public static void BeginFetch(RCS.ID id)
		{
			if (rcsRequests.ContainsKey(id))
				return;
			if (rcsStore == null)
				return;
			ContinuousPoller<SerialRCSStack> poller = new ContinuousPoller<SerialRCSStack>(null);
			if (!rcsRequests.TryAdd(id, poller))
				return;
			poller.Start(rcsStore, id.ToString());
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

		

		public static void BeginFetch(IEnumerable<RCS.ID> ids)
		{
			foreach (var id in ids)
				BeginFetch(id);
		}

		public static Action<SerialSDS> OnPutSDS { get; set; } = null;
		public static EntityRanges EntityRanges
		{
			get
			{
				var cfg = Config;
				return new EntityRanges(cfg.r, cfg.m, cfg.r - cfg.m);

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

		private static SerialSDS latestPut = null;
		public static async Task PutAsync(SerialSDS serial, bool forceReplace)
		{
			if (sdsStore == null)
				return;	//tests
			try
			{
				Log.Message("Storing serial SDS in DB: g" + serial.Generation);
				if (sdsPoller != null)
				{
					var latest = sdsPoller.Latest;
					if (latestPut != null && latestPut.Generation > latest.Generation)
						latest = latestPut;
					if (serial.Generation <= latest.Generation)
					{
						Log.Message("Newer already online. Rejecting update");
						return; //nothing to do
					}
					serial._rev = latest._rev;
				}
				await PutAsync(null, sdsStore, serial, forceReplace);
				latestPut = serial;
				Log.Message("Stored serial SDS in DB: g" + serial.Generation);
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
				await store.SetAsync(e._id, doc);
				return;

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
				var e2 = store.GetByIdAsync(e._id);	//if we get here, then the copy script has rejected our data => read data must be newer
				Simulation.FetchIncoming(lnk, e2);
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