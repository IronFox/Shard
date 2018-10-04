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
			public string ntp = "uhr.uni-trier.de";
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
			public int msMessageProcessing = 100;	//time allocated for message dispatch across all siblings
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
			//string serial = logicStore.Serializer.Serialize(data);
			await logicStore.Entities.PutAsync(data._id, data);
		}

		private static ConcurrentDictionary<string, Task<CSLogicProvider>> directProviderMap = new ConcurrentDictionary<string, Task<CSLogicProvider>>();

		private static async Task<CSLogicProvider> LoadLogicProviderAsync(string scriptName)
		{

			if (LogicLoader != null)
			{
				var logic = await LogicLoader(scriptName);
				if (logic != null)
					return logic;
			}

			var script = await logicStore.Entities.GetAsync<SerialCSLogicProvider>(scriptName);
			if (!script.IsSuccess)
				throw new Exception("CouchDB failure whilte querying logic provider '" + scriptName + "': " + script.Error);
			var prov = await script.Content.DeserializeAsync();
			return prov;
		}

		private static int inboundRCSsRemoved = -1;

		public static async Task PutAsync(SerialRCS serialRCS)
		{
			if (rcsStore == null)
				return; //tests
			try
			{
				Log.Minor("Storing serial RCS in DB: " + serialRCS.ID);
				await PutAsync(null, rcsStore, serialRCS, false);
				Log.Minor("Stored serial RCS in DB: " + serialRCS.ID);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
				Log.Error("Failed to store serial SDS in DB: " + serialRCS.ID);
			}

		}


		private static SemaphoreSlim rcsRemovalLock = new SemaphoreSlim(1); 

		public static async Task RemoveInboundRCSsAsync(IEnumerable<RCS.StackID> enumerable, int generationOrOlder)
		{
			await rcsRemovalLock.DoLockedAsync(async ()=>
			{
				if (inboundRCSsRemoved >= generationOrOlder)
					return;
				if (generationOrOlder > inboundRCSsRemoved + 10)
					inboundRCSsRemoved = generationOrOlder - 10;
				Log.Minor("Trimming inbound RCSs g" + (inboundRCSsRemoved+1)+" .. g"+generationOrOlder);

				List<DocumentHeader> bulkRemove = new List<DocumentHeader>();
				for (int g = inboundRCSsRemoved + 1; g <= generationOrOlder; g++)
				{
					foreach (var s in enumerable)
						bulkRemove.Add(new DocumentHeader(new RCS.GenID(s, g).ToString(), null));
				}
				/*var result =*/
				await rcsStore.Documents.BulkAsync(new BulkRequest().Delete(bulkRemove.ToArray()));
				inboundRCSsRemoved = generationOrOlder;
			});
		}

		public static Task<CSLogicProvider> GetLogicProviderAsync(string scriptName)
		{
			Task<CSLogicProvider> prov;
			if (directProviderMap.TryGetValue(scriptName, out prov))
				return prov;

			prov = LoadLogicProviderAsync(scriptName);

			directProviderMap.TryAdd(scriptName, prov);
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
			string reason;
			for (int i = 0; i < numTries; i++)
			{
				try
				{
					if (f())
						return;
					else
						reason = "Rejected";
				}
				catch (Exception ex)
				{
					while (ex.InnerException != null)
						ex = ex.InnerException;
					reason = ex.Message;
				}
				if (i + 1 < numTries)
				{
					Log.Message(reason+"; Sleeping "+ msBetweenRetries + " mseconds...");
					Thread.Sleep(msBetweenRetries);
				}
			}
		}

		private static async Task<bool> TryAsync(Func<Task<bool>> f, int numTries, int msBetweenRetries = 5000)
		{
			string reason;
			for (int i = 0; i < numTries; i++)
			{
				try
				{
					if (await f())
						return true;
					else
						reason = "Rejected";
				}
				catch (Exception ex)
				{
					while (ex.InnerException != null)
						ex = ex.InnerException;
					reason = ex.Message;
				}
				if (i + 1 < numTries)
				{
					Log.Message(reason+"; Sleeping " + msBetweenRetries + " mseconds...");
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
				return timingPoller?.Latest;
			}
			set
			{
				value._id = timingPoller.ID;
				PutAsync(null, controlStore, value, true).Wait();
			}
		}



		public static void PullConfig(int numTries = 3)
		{
			TryAsync(async () =>
			{
				Log.Message("Fetching simulation configuration from " + Host + " ...");
				var rc = await controlStore.Entities.GetAsync<ConfigContainer>("config");
				if (!rc.IsSuccess)
					return false;
				Config = rc.Content;
				return Config != null;
			}, numTries).Wait();

			timingPoller = new ContinuousPoller<TimingContainer>(new TimingContainer(),(collection) => null);
			timingPoller.Start(controlStore, "timing");
		}

		/// <summary>
		/// Attempts a forced replace or insert.
		/// If conflicts are detected, they are dropped, and the local version is again updated.
		/// Returns null if the method should be invoked again, otherwise the final version retrieved from the DB
		/// </summary>
		/// <typeparam name="T">Entity type</typeparam>
		/// <param name="store">DB to place the entity in</param>
		/// <param name="item">Entity to place. The _rev member may be updated in the process</param>
		/// <returns></returns>
		private static async Task<T> TryForceReplace<T>(DataBase store, T item) where T : Entity
		{
			var header = await controlStore.Entities.PutAsync(item._id, item);
			if (!header.IsSuccess /*&& header.StatusCode == System.Net.HttpStatusCode.Conflict*/)
			{
				if (!string.IsNullOrEmpty(header.Rev))
					item._rev = header.Rev;
				return null;
			}
			var echoRequest = new GetEntityRequest(header.Id);
			echoRequest.Conflicts = true;
			var echo = await controlStore.Entities.GetAsync<T>(header.Id);
			if (!echo.IsSuccess)
				return null;
			if (echo.Conflicts != null && echo.Conflicts.Length > 0)
			{
				//we do have conflicts and don't quite know whether the new version is our version
				var deleteHeaders = new DocumentHeader[echo.Conflicts.Length];
				for (int i = 0; i < echo.Conflicts.Length; i++)
					deleteHeaders[i] = new DocumentHeader(header.Id, echo.Conflicts[i]);
				BulkRequest breq = new BulkRequest().Delete(deleteHeaders);	//delete all conflicts
				await controlStore.Documents.BulkAsync(breq);   //ignore result, but wait for it
				item._rev = echo.Rev;   //replace again. we must win
				return await TryForceReplace(store, item);
			}
			return echo.Content;    //all good
		}

		/// <summary>
		/// Puts the specified container into the DB.
		/// </summary>
		/// <param name="container"></param>
		/// <param name="numTries"></param>
		/// <returns></returns>
		public static async Task PutConfigAsync(ConfigContainer container, int numTries = 3)
		{
			Config = container;
			Config._id = "config";
			//Config._rev = null;
			string doc = controlStore.Serializer.Serialize(Config);
			bool success = await TryAsync(async () =>
			{
				Log.Minor("Storing simulation configuration on " + Host + " ...");
				var newCfg = await TryForceReplace(controlStore, Config);
				if (newCfg == null)
					return false;	//retry
				Config = newCfg;
				return true;
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

		public class DataBase : MyCouchClient
		{
			public readonly string DBName;

			public DataBase(string url, string dbName) : base(url,dbName)
			{
				DBName = dbName;
			}
		}


		class ContinuousPoller<T>: IDisposable where T : Entity
		{
			private DataBase store;

			//TaskCompletionSource<T> anyValueAsync;

			public Action<T> OnChange { get; set; }
			public readonly Func<ICollection<T>, T> Merger;
			SpinLock sl = new SpinLock();
			CancellationTokenSource cancellation;

			public ContinuousPoller(T initial, Func<ICollection<T>, T> merger)
			{
				Latest = initial;
				Merger = merger;
			}


			public T Latest { get; private set; }

			public string ID { get; private set; }

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
				this.ID = id;

				PollAsync().Wait();

				var getChangesRequest = new GetChangesRequest
				{
					Feed = ChangesFeed.Continuous,
					Heartbeat = 3000 //Optional: LET COUCHDB SEND A I AM ALIVE BLANK ROW EACH ms
				};

				var serial = store.Entities.Serializer;
				cancellation = new CancellationTokenSource();
				store.Changes.GetAsync(
					getChangesRequest,
					data =>
					{
						if (disposedValue)
							return;
						var d = serial.Deserialize<Change>(data);
						if (d != null && d.id == id)
						{
							StringBuilder revs = new StringBuilder();
							bool actualChange = false;
							foreach (var ch in d.changes)
							{
								revs.Append(' ').Append(ch.rev);
								if (Latest == null || ch.rev != Latest._rev)
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
						var header = await store.Documents.HeadAsync(ID);
						if (!header.IsSuccess)
							return;
						if (Latest == null || header.Rev != Latest._rev)
						{
							var data = await GetConflictResolvedAsync(store, ID, Merger);
							if (data == null)
								return; //try again later
							sl.DoLocked(() =>
							{
								Log.Minor("Got new data on " + store.DBName + "['" + ID + "']: rev " + header.Rev);
								//if (lastQueriedValue == null)
								//	anyValueAsync.SetResult(data);
								Latest = data;
								OnChange?.Invoke(data);
							});
						}
						return;
					}
					catch (TaskCanceledException)
					{}
				}
			}

			#region IDisposable Support
			private bool disposedValue = false; // To detect redundant calls

			protected virtual void Dispose(bool disposing)
			{
				if (!disposedValue)
				{
					if (disposing)
					{
						cancellation.Cancel();
					}

					// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
					// TODO: set large fields to null.

					disposedValue = true;
				}
			}

			// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
			// ~ContinuousPoller() {
			//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			//   Dispose(false);
			// }

			// This code added to correctly implement the disposable pattern.
			public void Dispose()
			{
				// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
				Dispose(true);
				// TODO: uncomment the following line if the finalizer is overridden above.
				// GC.SuppressFinalize(this);
			}
			#endregion
		}


		private static async Task<T> GetConflictResolvedAsync<T>(DataBase store, string id, Func<ICollection<T>, T> merger) where T:Entity
		{
			GetEntityRequest req = new GetEntityRequest(id);
			req.Conflicts = true;
			var data = await store.Entities.GetAsync<T>(req);
			if (!data.IsSuccess)
				return null;
			if (data.Conflicts == null || data.Conflicts.Length == 0)
				return data.Content;

			List<T> conflicting = new List<T>();
			List<DocumentHeader> toDelete = new List<DocumentHeader>();
			conflicting.Add(data.Content);
			foreach (var fetch in data.Conflicts)
			{
				var item = await store.Entities.GetAsync<T>(id, fetch);
				if (item.IsSuccess)
				{
					conflicting.Add(item.Content);
					toDelete.Add(new DocumentHeader(item.Id, item.Rev));
				}
				else
					return null;	//assume connection lost or competing merge
			}
			T merged = merger(conflicting);
			if (merged == null)
			{
				Log.Error("Error trying to fetch conflicting data from "+store+":"+id+" with no merger set. Fixed. Result will be arbitrary");
				merged = data.Content;
			}
			merged._id = id;
			merged._rev = data.Rev;
			var putResult = await store.Entities.PutAsync(merged);
			if (putResult.IsSuccess)
			{
				var deleteRequest = new BulkRequest().Delete(toDelete.ToArray());
				deleteRequest.AllOrNothing = true;
				await store.Documents.BulkAsync(deleteRequest);//await, ignore result, let others clean it up if failed
				return putResult.Content;
			}
			return null;	//assume connection lost or asynchronous update
		}



		public static SerialSDS Begin(Int3 myID)
		{
			sdsPoller = new ContinuousPoller<SerialSDS>(null, (collection)=>
			{
				SerialSDS latest = null;
				foreach (var candidate in collection)
				{
					if (latest == null || latest.Generation < candidate.Generation)
						latest = candidate;
					else if (latest.Generation == candidate.Generation)
					{
						//weird, but let's check
						if (!candidate.IC.IsEmpty)
							throw new IntegrityViolation("SDS candidate at g=" + candidate.Generation + " is not consistent");
						if (candidate.Generation != latest.Generation)
							throw new IntegrityViolation("SDS candidate at g=" + candidate.Generation + " mismatch with favorite at g="+latest.Generation);
						var comp = new Helper.Comparator()
							.Append(candidate.SerialEntities, latest.SerialEntities)
							.Append(candidate.SerialMessages,latest.SerialMessages)
							.Finish();
						if (comp != 0)
						{
							Log.Error("Persistent SDS data mismatch at g="+candidate.Generation);
							if (comp < 0)
								latest = candidate;
						}
					}
				}
				if (!latest.IC.IsEmpty)
					throw new IntegrityViolation("Chosen persistent SDS candidate at g="+latest.Generation+" is not consistent");
				return latest;
			});
			sdsPoller.Start(sdsStore, myID.Encoded);
			sdsPoller.OnChange = serial => Simulation.FetchIncoming(null, serial);
			return sdsPoller.Latest;
		}


		public class MappedContinuousLookup<K,D> where D: Entity
		{
			private ConcurrentDictionary<K, ContinuousPoller<D>> requests = new ConcurrentDictionary<K, ContinuousPoller<D>>();

			private readonly Func<ICollection<D>, D> m;
			public MappedContinuousLookup(Func<ICollection<D>, D> merger)
			{
				m = merger;
			}
			public void BeginFetch(DataBase store, K id)
			{
				if (requests.ContainsKey(id))
					return;
				if (store == null)
					return;
				ContinuousPoller<D> poller = new ContinuousPoller<D>(null, m);
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

			public void FilterRequests(Func<K, bool> filterFunction)
			{
				foreach (var pair in requests)
				{
					if (!filterFunction(pair.Key))
					{
						ContinuousPoller<D> p;
						requests.ForceRemove(pair.Key, out p);
						p.Dispose();
					}
				}
			}
		}


		//private static MappedContinuousLookup<RCS.StackID, SerialRCSStack> rcsRequests = new MappedContinuousLookup<RCS.StackID, SerialRCSStack>(stacks => SerialRCSStack.Merge(stacks));

		private static MappedContinuousLookup<RCS.GenID, SerialRCS> rcsRequests = new MappedContinuousLookup<RCS.GenID, SerialRCS>(rcss => null);
		private static MappedContinuousLookup<ShardID, AddressEntry> hostRequests = new MappedContinuousLookup<ShardID, AddressEntry>((collection)=>null);

		public static void BeginFetch(RCS.GenID id)
		{
			rcsRequests.BeginFetch(rcsStore, id);
		}

		public static SerialRCS TryGetInbound(RCS.GenID id)
		{
			if (id.Generation <= inboundRCSsRemoved)
				throw new IntegrityViolation("Trying to query removed inbound RCS");
			return rcsRequests.TryGet(rcsStore, id);
		}

		public static void StopFetchingRCSs(int generationOrOlder)
		{
			rcsRequests.FilterRequests(id => id.Generation > generationOrOlder);
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


	

		public static Action<SerialSDS> OnPutSDS { get; set; } = null;
		public static Action<ShardPeerAddress> OnPutLocalAddress { get; set; } = null;


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

		private static async Task PutAsync<T>(Link lnk, DataBase store, T e, bool forceReplace) where T: Entity
		{
			//string doc = store.DocumentSerializer.Serialize(e);

			if (forceReplace)
			{
				while (true)
				{
					try
					{
						for (int i = 0; i < 3; i++)
						{
							var inserted = await TryForceReplace(store, e);
							if (inserted != null)
								return;
						}
						//await store.SetAsync(e._id, doc);
						return;
					}
					catch (MyCouchResponseException)
					{ }
				}
				//await store.DeleteAsync(e._id);
			}
			try
			{
				var header = await store.Entities.PutAsync(e._id, e);
				if (!header.IsSuccess)
					throw new MyCouchResponseException(header);
				e._rev = header.Rev;
			}
			catch (MyCouchResponseException)
			{
				if (lnk != null)
				{
					var e2 = store.Entities.GetAsync<T>(e._id); //if we get here, then the copy script has rejected our data => read data must be newer
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