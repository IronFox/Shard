using Base;
using MyCouch;
using MyCouch.Requests;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DBType
{



	public class ContinuousPoller<T> : IPollable<T>, IDisposable where T : Entity
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

		private class RevisionChange
		{
			public string rev = null;
		}


		private class Change
		{
			public string seq = "";
			public string id = null;
			public RevisionChange[] changes = null;
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
			Log.Message("Starting "+this);

			Resume();
		}

		private static async Task<T> GetConflictResolvedAsync(DataBase store, string id, Func<ICollection<T>, T> merger, CancellationToken cancellationToken = default(CancellationToken))
		{
			var req = new GetDocumentRequest(id);
			req.Conflicts = true;
			var data = await store.Documents.GetAsync(req, cancellationToken);
			//await store.Entities.GetAsync<T>(req, cancellationToken);
			if (!data.IsSuccess)
				return null;
			var mainContent = JsonConvert.DeserializeObject<T>(data.Content);
			if (data.Conflicts == null || data.Conflicts.Length == 0)
				return mainContent;

			List<T> conflicting = new List<T>();
			List<DocumentHeader> toDelete = new List<DocumentHeader>();
			conflicting.Add(mainContent);
			foreach (var fetch in data.Conflicts)
			{
				var item = await store.Documents.GetAsync(id, fetch,cancellationToken);
				if (item.IsSuccess)
				{
					conflicting.Add(JsonConvert.DeserializeObject<T>(item.Content));
					toDelete.Add(new DocumentHeader(item.Id, item.Rev));
				}
				else
					return null;    //assume connection lost or competing merge
			}
			T merged = merger(conflicting);
			if (merged == null)
			{
				Log.Error("Error trying to fetch conflicting data from " + store + ":" + id + " with no merger set. Fixed. Result will be arbitrary");
				merged = mainContent;
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


		public override string ToString()
		{
			return store.DBName + "['" + ID + "']";
		}

		private async Task PollAsync(CancellationToken cancellationToken = default(CancellationToken))
		{
			for (int i = 0; i < 3; i++)
			{
				try
				{
					var header = await store.Documents.HeadAsync(ID,null, cancellationToken);
					if (cancellationToken.IsCancellationRequested || !header.IsSuccess)
						return;
					if (Latest == null || header.Rev != Latest._rev)
					{
						var data = await GetConflictResolvedAsync(store, ID, Merger, cancellationToken);
						if (cancellationToken.IsCancellationRequested || data == null)
							return; //try again later
						sl.DoLocked(() =>
						{
							Log.Minor("Got new data on "+this+": rev " + header.Rev);
							//if (lastQueriedValue == null)
							//	anyValueAsync.SetResult(data);
							Latest = data;
							OnChange?.Invoke(data);
						});
					}
					return;
				}
				catch (TaskCanceledException)
				{
					return;
				}
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


		public bool Suspend()
		{
			if (cancellation == null)
				return false;
			cancellation.Cancel();
			try
			{
				pollThread.Join();
			}
			catch { }
			cancellation = null;
			Log.Message("Suspended " + this);
			return true;
		}

		private Thread pollThread;
		private void ThreadedPoll()
		{
			try
			{
				while (!cancellation.IsCancellationRequested)
				{
					var t = PollAsync(cancellation.Token);
					if (cancellation.IsCancellationRequested)
						return;
					if (!t.Wait(500, cancellation.Token))
					{
						if (cancellation.IsCancellationRequested)
							return;
						Log.Error("Failed poll data on " + this + " after 500 ms");
					}
					Thread.Sleep(500);
				}
			}
			catch (Exception ex)
			{
				Log.Error("Failed poll data on " + this + ": "+ex);
			}
		}

		public void Resume()
		{
			if (cancellation != null && !cancellation.IsCancellationRequested)
				return;
			try
			{
				if (pollThread != null)
					pollThread.Join();
			}
			catch
			{ }
			cancellation = new CancellationTokenSource();
			pollThread = new Thread(new ThreadStart(ThreadedPoll));
			pollThread.Start();

			/*


			Log.Message("Pulling " + this);
			PollAsync().Wait();
			Log.Message("Resuming " + this);

			var getChangesRequest = new GetChangesRequest
			{
				Feed = ChangesFeed.Continuous
				,Heartbeat = 3000 //Optional: LET COUCHDB SEND A I AM ALIVE BLANK ROW EACH ms
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
					if (d != null && d.id == ID)
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
							Log.Minor("Update detected on " + this+ ":" + revs + ". Polling");
							PollAsync().Wait();
						}
					}
				},
				cancellation.Token);
			*/
		}
		#endregion
	}


}