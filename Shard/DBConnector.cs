using MyCouch;
using System;

namespace Shard
{
	public class DBConnector
	{
		private IServerConnection server;

		private readonly string url;


		public DBConnector(Host host, string username, string password)
		{
			url = "http://" + Uri.EscapeDataString(username) + ":" + Uri.EscapeDataString(password) + "@" + host;
			MyCouchStore cfg = new MyCouchStore(url,"config");
			cfg.
			//"http://someuser:p%40ssword@
			var cfg = new MyCouchClient(, "config");
			cfg.
			server = new CouchServer(host.URL, host.Port);
			config = server.GetDatabase("config");
			sds = server.GetDatabase("sds");
			rcs = server.GetDatabase("rcs");

			var docs = config.GetAllDocuments();

			

		}
	}
}