using Base;
using MyCouch;
using MyCouch.Requests;
using MyCouch.Responses;
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
using static Shard.BaseDB;

namespace Shard
{
	public static class DB
	{






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
			if (LogicStore == null)
				return;// Task.FromResult<object>(null);
			//string serial = logicStore.Serializer.Serialize(data);
			await LogicStore.Entities.PutAsync(data._id, data);
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

			var script = await LogicStore.Entities.GetAsync<SerialCSLogicProvider>(scriptName);
			if (!script.IsSuccess)
				throw new Exception("CouchDB failure whilte querying logic provider '" + scriptName + "': " + script.Error);
			var prov = await script.Content.DeserializeAsync();
			return prov;
		}

		private static int inboundRCSsRemoved = -1;

		public static async Task PutAsync(SerialRCS serialRCS)
		{
			if (RCSStore == null)
				return; //tests
			try
			{
				Log.Minor("Storing serial RCS in DB: " + serialRCS.ID);
				await PutAsync(null, RCSStore, serialRCS, false);
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

				List<Task<DocumentHeaderResponse>> tasks = new List<Task<DocumentHeaderResponse>>();
				List<DocumentHeader> bulkRemove = new List<DocumentHeader>();
				for (int g = inboundRCSsRemoved + 1; g <= generationOrOlder; g++)
				{
					foreach (var s in enumerable)
					{
						HeadDocumentRequest req = new HeadDocumentRequest(new RCS.GenID(s, g).ToString());
						tasks.Add(RCSStore.Documents.HeadAsync(req));
					}
				}
				foreach (var t in tasks)
				{
					var rs = await t;
					if (rs.IsSuccess)
						bulkRemove.Add(new DocumentHeader(rs.Id,rs.Rev));
				}
				if (bulkRemove.Count > 0)
				{
					var result =
						await RCSStore.Documents.BulkAsync(new BulkRequest().Delete(bulkRemove.ToArray()));
					if (!result.IsSuccess)
						Log.Error("Failed to remove some inbound RCSs: "+result.Error);
				}
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



		private static ContinuousPoller<SerialSDS> sdsPoller;


		


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
			sdsPoller.Start(SDSStore, myID.Encoded);
			sdsPoller.OnChange = serial => Simulation.FetchIncoming(null, serial);
			return sdsPoller.Latest;
		}


		//private static MappedContinuousLookup<RCS.StackID, SerialRCSStack> rcsRequests = new MappedContinuousLookup<RCS.StackID, SerialRCSStack>(stacks => SerialRCSStack.Merge(stacks));

		private static MappedContinuousLookup<RCS.GenID, SerialRCS> rcsRequests = new MappedContinuousLookup<RCS.GenID, SerialRCS>(rcss => null);

		public static void BeginFetch(RCS.GenID id)
		{
			rcsRequests.BeginFetch(RCSStore, id);
		}

		public static SerialRCS TryGetInbound(RCS.GenID id)
		{
			if (id.Generation <= inboundRCSsRemoved)
				throw new IntegrityViolation("Trying to query removed inbound RCS");
			return rcsRequests.TryGet(RCSStore, id);
		}

		public static void StopFetchingRCSs(int generationOrOlder)
		{
			rcsRequests.FilterRequests(id => id.Generation > generationOrOlder);
		}

		

	

		public static Action<SerialSDS> OnPutSDS { get; set; } = null;

		internal static void PutNow(SerialSDS serial, bool forceReplace)
		{
			if (OnPutSDS != null)
				OnPutSDS(serial);
			Try(() =>
			{
				PutAsync(null,SDSStore,serial, forceReplace).Wait();
				return true;
			}, 3);
		}




		private static SerialSDS latestPut = null;
		public static async Task PutAsync(SerialSDS serial, bool forceReplace)
		{
			if (SDSStore == null)
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
				await PutAsync(null, SDSStore, serial, forceReplace);
				latestPut = serial;
				Log.Minor("Stored serial SDS in DB: g" + serial.Generation);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
				Log.Error("Failed to store serial SDS in DB: g" + serial.Generation);
			}
		}

		private static async Task PutAsync<T>(Link lnk, DataBase store, T e, bool forceReplace) where T: BaseDB.Entity
		{
			//string doc = store.DocumentSerializer.Serialize(e);

			if (forceReplace)
			{
				await Put(store, e,10);
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