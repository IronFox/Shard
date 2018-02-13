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
		private int port = PeerAddress.DefaultPort;

		public Action<InteractionLink> OnNewInteractionLink { get; set; }

		public int Port => port;

		public Listener(Func<ShardID, Link> linkLookup)
		{
			this.linkLookup = linkLookup;
			for (port = PeerAddress.DefaultPort; port < PeerAddress.DefaultPort + 1024; port++)
			{
				try
				{
					server = new TcpListener(IPAddress.Any, port);
					server.Start();
					break;
				}
				catch (SocketException)
				{
					try {server.Server.Dispose();} catch { };
					server = null;
				}
			}
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
						PeerAddress host = new PeerAddress(Dns.GetHostEntry(addr.Address).HostName, addr.Port);
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