using Base;
using MyCouch;
using MyCouch.Requests;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	public static class BaseDB
	{
		private static string url;
		public static Address Host { get; private set; }
		public static MyCouchServerClient server;

		public static ConfigContainer Config { get; private set; }

		public static async Task RecreateDB(string name)
		{
			if (server != null)
			{
				var rs = await server.Databases.DeleteAsync(name);
				if (!rs.IsSuccess && rs.Error != "not_found")
					throw new Exception("Failed to delete '" + name + "' database: " + rs.Reason);
				rs = await server.Databases.PutAsync(name);
				if (!rs.IsSuccess)
					throw new Exception("Failed to recreate '" + name + "' database: " + rs.Reason);
				//var rs = await sdsStore.Database.DeleteAsync();
				//sdsStore.Dispose();
				//sdsStore = new DataBase(url, "sds");

			}
		}


		public class Entity
		{
			public string _id, _rev;
			//public string EntityId, _rev;
		}


		private static bool haveCredentials = false;
		public static bool HaveCredentials => haveCredentials;
		public static bool HasAdminAccess => server != null && haveCredentials;
		public static Action<FullShardAddress> OnPutLocalAddress { get; set; } = null;

		private class AllDocsValue
		{
			// ReSharper disable once UnusedAutoPropertyAccessor.Local
			public string Rev { get; set; }

			// ReSharper disable once UnusedAutoPropertyAccessor.Local
			public bool Deleted { get; set; }
		}


		public class DataBase : MyCouchClient
		{
			public readonly string DBName;

			public DataBase(string url, string dbName) : base(url, dbName)
			{
				DBName = dbName;
			}

			public override string ToString()
			{
				return DBName;
			}

			public async Task ClearAsync(bool compact)
			{
				Log.Message("Clearing out " + this + "...");
				var request = new QueryViewRequest(SystemViewIdentity.AllDocs);
				var response = await Views.QueryAsync<AllDocsValue>(request);
				if (!response.IsSuccess)
					throw new Exception("Failed to clear local store " + this + ": " + response.Reason);
				var headers = new List<DocumentHeader>();
				for (long i = 0; i < response.RowCount; i++)
				{
					if (!response.Rows[i].Value.Deleted)
						headers.Add(new DocumentHeader(response.Rows[i].Id, response.Rows[i].Value.Rev));
				}
				if (headers.Count > 0)
				{
					var rs = await Documents.BulkAsync(new BulkRequest().Delete(headers.ToArray()));
					if (!rs.IsSuccess)
						throw new Exception("Unable to delete all documents in " + this + ": " + rs.Reason);
					if (compact)
					{
						var rs2 = await Database.CompactAsync();
						if (!rs2.IsSuccess)
							throw new Exception("Unable to compact documents in " + this + ": " + rs2.Reason);
					}
				}
				if (compact)
					Log.Message(this + " is clear and compacted");
				else
					Log.Message(this + " is clear");
			}
		}


		class RevisionChange
		{
			public string rev = null;
		}

		class Change
		{
			public string seq = "";
			public string id = null;
			public RevisionChange[] changes = null;
		}


		private static async Task<T> GetConflictResolvedAsync<T>(DataBase store, string id, Func<ICollection<T>, T> merger) where T : Entity
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
					return null;    //assume connection lost or competing merge
			}
			T merged = merger(conflicting);
			if (merged == null)
			{
				Log.Error("Error trying to fetch conflicting data from " + store + ":" + id + " with no merger set. Fixed. Result will be arbitrary");
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
			return null;    //assume connection lost or asynchronous update
		}

		public static async Task<bool> TryAsync(Func<Task<bool>> f, int numTries, int msBetweenRetries = 5000)
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
					Log.Message(reason + "; Sleeping " + msBetweenRetries + " mseconds...");
					await Task.Delay(msBetweenRetries);
				}
			}
			return false;
		}

		public static Func<TimingContainer> FallbackTimingFetch { get; set; }

		public static TimingContainer Timing
		{
			get
			{
				return timingPoller != null ? timingPoller.Latest : (FallbackTimingFetch?.Invoke());
			}
			set
			{
				value._id = timingPoller.ID;
				Put(ControlStore, value).Wait();
			}
		}

		public static void Try(Func<bool> f, int numTries, int msBetweenRetries = 5000)
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
					Log.Message(reason + "; Sleeping " + msBetweenRetries + " mseconds...");
					Thread.Sleep(msBetweenRetries);
				}
			}
		}


		public static void PutNow(FullShardAddress addr)
		{
			if (OnPutLocalAddress != null)
				OnPutLocalAddress(addr);
			Try(() =>
			{
				Put(HostsStore, new AddressEntry(addr)).Wait();
				return true;
			}, 3);
		}

		public static void PullConfig(int numTries = 3)
		{
			TryAsync(async () =>
			{
				Log.Message("Fetching simulation configuration from " + Host + " ...");
				var rc = await ControlStore.Entities.GetAsync<ConfigContainer>("config");
				if (!rc.IsSuccess)
					return false;
				Config = rc.Content;
				return Config != null;
			}, numTries).Wait();

			timingPoller = new ContinuousPoller<TimingContainer>(new TimingContainer(), (collection) => null);
			timingPoller.Start(ControlStore, "timing");
		}


		/// <summary>
		/// Repeats insertion attempts until successful or the number of tries has been reached
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="store"></param>
		/// <param name="item"></param>
		/// <returns></returns>
		public static async Task Put<T>(DataBase store, T item, int numTries = 3) where T : Entity
		{
			while (true)
			{
				try
				{
					for (int i = 0; i < numTries; i++)
					{
						var inserted = await TryForceReplace(store, item);
						if (inserted != null)
							return;
					}
					//await store.SetAsync(e._id, doc);
					return;
				}
				catch (MyCouchResponseException)
				{ }
			}
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
		public static async Task<T> TryForceReplace<T>(DataBase store, T item) where T : Entity
		{
			var header = await store.Entities.PutAsync(item._id, item);
			if (!header.IsSuccess /*&& header.StatusCode == System.Net.HttpStatusCode.Conflict*/)
			{
				var head = await store.Documents.HeadAsync(item._id);
				if (head.IsSuccess)
					item._rev = head.Rev;
				return null;
			}
			var echoRequest = new GetEntityRequest(header.Id);
			echoRequest.Conflicts = true;
			var echo = await store.Entities.GetAsync<T>(header.Id);
			if (!echo.IsSuccess)
				return null;
			if (echo.Conflicts != null && echo.Conflicts.Length > 0)
			{
				//we do have conflicts and don't quite know whether the new version is our version
				var deleteHeaders = new DocumentHeader[echo.Conflicts.Length];
				for (int i = 0; i < echo.Conflicts.Length; i++)
					deleteHeaders[i] = new DocumentHeader(header.Id, echo.Conflicts[i]);
				BulkRequest breq = new BulkRequest().Delete(deleteHeaders); //delete all conflicts
				await store.Documents.BulkAsync(breq);   //ignore result, but wait for it
				item._rev = echo.Rev;   //replace again. we must win
				return await TryForceReplace(store, item);
			}
			return echo.Content;    //all good
		}










		private static ContinuousPoller<TimingContainer> timingPoller;


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
			string doc = ControlStore.Serializer.Serialize(Config);
			bool success = await TryAsync(async () =>
			{
				Log.Minor("Storing simulation configuration on " + Host + " ...");
				var newCfg = await TryForceReplace(ControlStore, Config);
				if (newCfg == null)
					return false;   //retry
				Config = newCfg;
				return true;
			}, numTries);
			if (success)
				Log.Minor("Done");
			else
				Log.Error("Failed to upload config to server");

		}



		public class ContinuousPoller<T> : IDisposable where T : Entity
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
					{ }
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



		public static string Connect(Address host, string username = null, string password = null)
		{
			url = "http://";
			if (username != null && password != null)
			{
				haveCredentials = true;
				url += Uri.EscapeDataString(username) + ":" + Uri.EscapeDataString(password) + "@";
			}
			else
				haveCredentials = false;
			url += host;
			Host = host;

			server = new MyCouchServerClient(url);
			SDSStore = new DataBase(url, "sds");
			CCSStore = new DataBase(url, "ccs");	//client messages (and other changes, possibly; not right now)
			RCSStore = new DataBase(url, "rcs");
			LogicStore = new DataBase(url, "logic");
			ControlStore = new DataBase(url, "control");
			HostsStore = new DataBase(url, "hosts");

			return url;
		}

		public static DataBase SDSStore { get; private set; }
		public static DataBase CCSStore { get; private set; }
		public static DataBase RCSStore { get; private set; }
		public static DataBase LogicStore { get; private set; }
		public static DataBase ControlStore { get; private set; }
		public static DataBase HostsStore { get; private set; }


		private static MappedContinuousLookup<ShardID, AddressEntry> hostRequests = new MappedContinuousLookup<ShardID, AddressEntry>((collection) => null);



		public class MappedContinuousLookup<K, D> where D : Entity
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
				return TryGet(store, id);
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


		public static void BeginFetch(ShardID id)
		{
			hostRequests.BeginFetch(HostsStore, id);
		}


		public static FullShardAddress TryGetAddress(ShardID id)
		{
			var h = hostRequests.TryGet(HostsStore, id);
			if (h == null)
				return new FullShardAddress();
			return h.GetFullAddress(id);
		}

		public static Address TryGetPeerAddress(ShardID id)
		{
			var h = hostRequests.TryGet(HostsStore, id);
			if (h == null)
				return new Address();
			return h.PeerAddress;
		}

		public static Address TryGetConsensusAddress(ShardID id)
		{
			var h = hostRequests.TryGet(HostsStore, id);
			if (h == null)
				return new Address();
			return h.ConsensusAddress;
		}


		public static async Task ClearSimulationDataAsync()
		{
			if (haveCredentials)
			{
				await BaseDB.RecreateDB("sds");
				await BaseDB.RecreateDB("rcs");
				await BaseDB.RecreateDB("ccs");
			}
			else
			{
				if (SDSStore != null)
					await SDSStore.ClearAsync(false);
				if (RCSStore != null)
					await RCSStore.ClearAsync(false);
				if (CCSStore != null)
					await CCSStore.ClearAsync(false);
			}
		}




		public class ConfigContainer : Entity
		{
			public ShardID extent = new ShardID(Int3.One, 1);
			public float r = 0.5f, m = -1;
			public string ntp = "uhr.uni-trier.de";
		}

		public class AddressEntry : Entity
		{
			public string host;
			public int peerPort, consensusPort;

			public AddressEntry(FullShardAddress addr)
			{
				_id = addr.ShardID.ToString();
				host = addr.Host;
				peerPort = addr.PeerPort;
				consensusPort = addr.ConsensusPort;
			}

			[JsonIgnore]
			public Address PeerAddress => new Address(host,peerPort);

			[JsonIgnore]
			public Address ConsensusAddress => new Address(host, consensusPort);

			internal FullShardAddress GetFullAddress(ShardID id)
			{
				return new FullShardAddress(id, host, peerPort, consensusPort);
			}
		}

		public class TimingContainer : Entity
		{
			public int msGenerationBudget = 3000;   //total milliseconds per generation, split among (1+recoverySteps) steps
			public int msComputation = 500; //total milliseconds per step dedicated to computation. Must be less than msStep. Any extra time is used for communication
			public int msApplication = 400;   //time allocated for CS application
			public string startTime = DateTime.Now.ToString();  //starting time of the computation of generation #0
			public int recoverySteps = 2;  //number of steps per generation dedicated to recovery. The total number of steps per top level generation equals 1+recoverySteps
			public int maxGeneration = -1;  //maximum top level generation. Negative when disabled
			public int startGeneration = 0; //generation effective at startTime
		}

	}
}
