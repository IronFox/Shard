using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Shard
{
	internal class Listener
	{
		private readonly Simulation simulation;
		private readonly TcpListener server;
		private readonly Thread listenerThread;

		public Listener(Simulation simulation)
		{
			this.simulation = simulation;

			server = new TcpListener(IPAddress.Any, new Host(simulation.ID).Port);
			server.Start();
			listenerThread = new Thread(new ThreadStart(Listen));
			listenerThread.Start();
		}

		private void Listen()
		{
			try
			{
				while (true)
				{
					Console.WriteLine("Waiting for a connection... ");
					TcpClient client = server.AcceptTcpClient();
					try
					{
						IPEndPoint addr = (IPEndPoint)client.Client.RemoteEndPoint;
						Host host = new Host(Dns.GetHostEntry(addr.Address).HostName, addr.Port);
						Link link = simulation.FindLink(host.ID);
						link.SetPassiveClient(client);
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