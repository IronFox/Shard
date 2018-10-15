using Base;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Shard
{
	public class Listener : IDisposable
	{
		private readonly TcpListener server;
		private readonly Thread listenerThread;
		private readonly Func<ShardID, Link> linkLookup;


		public const int DefaultPort = 15211;

		private int port = DefaultPort;

		public Action<InteractionLink> OnNewInteractionLink { get; set; }

		public int Port => port;

		public Listener(Func<ShardID, Link> linkLookup, int port=0)
		{
			this.linkLookup = linkLookup;
			server = new TcpListener(IPAddress.Any, port);
			server.Start();
			port = ((IPEndPoint)server.LocalEndpoint).Port;
			listenerThread = new Thread(new ThreadStart(Listen));
			listenerThread.Start();
		}

		public void Dispose()
		{
			server.Stop();
			listenerThread.Join();
		}

		private void Listen()
		{
			try
			{
				while (true)
				{
					Console.WriteLine("Listener: Waiting for next connection... ");
					TcpClient client = server.AcceptTcpClient();
					try
					{
						IPEndPoint addr = (IPEndPoint)client.Client.RemoteEndPoint;
						Address host = new Address(Dns.GetHostEntry(addr.Address).HostName, addr.Port);
						//Link link = linkLookup?.Invoke(host);
						//if (link == null)
						{
							var lnk = InteractionLink.Establish(client,linkLookup);
							OnNewInteractionLink?.Invoke(lnk);
							continue;
						}
							//Simulation.FindLink(host.ID);
						//link.SetPassiveClient(client);
					}
					catch (Exception ex)
					{
						Console.Error.WriteLine(ex);
					}
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine(ex);
			}
			return;
		}

	}
}