using Base;
using MyCouch;
using MyCouch.Requests;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DBType
{

	public class DataBase : MyCouchClient
	{
		public readonly string DBName;

		private class AllDocsValue
		{
			// ReSharper disable once UnusedAutoPropertyAccessor.Local
			public string Rev { get; set; }

			// ReSharper disable once UnusedAutoPropertyAccessor.Local
			public bool Deleted { get; set; }
		}



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


}