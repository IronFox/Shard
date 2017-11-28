using MyCouch;
using MyCouch.Requests;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
			public int msPerTimeStep = 1000,   //total milliseconds per timestep
						recoverySteps = 2;  //number of sub-steps per timestep (effectively splitting msPerTimeStep)
			public string start = DateTime.Now.ToString();
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

		private class MyLogicState : EntityLogic
		{
			public MyLogicState()
			{
			}

			public override int CompareTo(EntityLogic other)
			{
				return 0;
			}

			public override void Evolve(ref NewState newState, Shard.Entity currentState, int generation, Random randomSource)
			{
			}

			public override void Hash(Hasher h)
			{
			}
		}



		private static ConcurrentDictionary<RCS.ID, ContinuousPoller<SerialRCSStack>> rcsRequests = new ConcurrentDictionary<RCS.ID, ContinuousPoller<SerialRCSStack>>();
		private static ConcurrentDictionary<SDS.ID, ContinuousPoller<SDS.Serial>> sdsRequests = new ConcurrentDictionary<SDS.ID, ContinuousPoller<SDS.Serial>>();

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

		

		public static void BeginFetch(IEnumerable<RCS.ID> ids)
		{
			foreach (var id in ids)
				BeginFetch(id);
		}

		public static Action<SDS.Serial> OnPutSDS { get; set; } = null;

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
						entries[at] = data;
						stack.Entries = entries;
					}

					OnPutRCS?.Invoke(stack.Entries[at], generation);

					for (int i = 0; i < 10; i++)
					{
						try
						{
							/* Tests (and ONLY tests) may trigger this:
							 * If rcsStore is null (DB was not initialized), the following line triggers a null-
							 * reference exception that fails the whole method.
							 * The stack (by reference) will still have changed, but ReplaceAsync will fail, not
							 * replacing the reference. The resulting null-reference exception is stored for
							 * rcsPutTask.Wait() to collect (if ever).
							 */
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


		internal static void Put(SDS.Serial serial)
		{
			if (OnPutSDS != null)
				OnPutSDS(serial);
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