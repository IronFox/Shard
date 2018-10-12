using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace Consensus
{
	[Serializable]
	internal class AddressInfo : IDispatchable
	{
		public readonly Address Address;

		public AddressInfo(Address addr)
		{
			Address = addr;
		}

		public void OnArrive(Member receiver, Connection sender)
		{
			sender.LogEvent("Changing remote address to " + Address);
			sender.Address = Address;
		}
	}

	internal class Connection : Identity, IDisposable
	{
		private TcpClient client;
		private volatile bool disposed = false;
		private bool active = false;
		public bool Established => IsConnected;
		private Thread activeThread;
		private readonly Member owner;
		private BinaryFormatter formatter = new BinaryFormatter();

		/// <summary>
		/// index of highest log entry known to be replicated on server
		/// (initialized to 0, increases monotonically)
		/// </summary>
		public int MatchIndex { get; set; } = 0;
		/// <summary>
		/// index of the next log entry to send to that server
		/// (initialized to leader last log index+1).
		/// </summary>
		public int NextIndex { get; set; } = -1;
		public int CommitIndex { get; set; } = 0;
		public long AppendTimeout { get; set; } = -1;


		public bool IsConnected => !disposed && client != null && client.Connected;


		public Connection(Member owner, Address addr):base(owner,addr)
		{
			this.owner = owner;
			Connect(addr);
		}

		public Connection(Member owner, Address addr, TcpClient client):base(owner, addr)
		{
			this.owner = owner;
			Assign(client);
		}

		public delegate void Event(Connection connection);
		public delegate void DataEvent<T>(Connection connection, T obj);

		//private event Event onConnect = new Event();


		private Connection Connect(Address addr)
		{
			active = true;
			try
			{
				Assign(new TcpClient(addr.HostName, addr.Port));
				Dispatch(new AddressInfo(((Member)Parent).Address));
			}
			catch
			{
				Assign(null);
			}
			return this;
		}

		private void Assign(TcpClient newClient)
		{
			client = newClient;
			activeThread = new Thread(new ThreadStart(ActiveThread));
			activeThread.Start();
		}

		public void Dispose()
		{
			if (disposed)
				return;
			disposed = true;
			if (client != null)
				client.Dispose();
			if (activeThread != null)
				activeThread.Join();
		}


		private void ActiveThread()
		{
			while (!disposed)
			{
				if (client != null)
				{
					try
					{
						var stream = client.GetStream();
						LogEvent("Begin stream read");

						while (!disposed && client.Connected)
						{
							var item = formatter.Deserialize(stream) as IDispatchable;
							try
							{
								//LogEvent("Deserialized inbound " + item);
								item.OnArrive(owner, this);
							}
							catch (Exception ex)
							{
								LogError("On implement: " + ex);
							}
						}
					}
					catch (ObjectDisposedException ex)
					{
						LogError(ex.Message);
					}
					catch (IOException ex)
					{
						LogError(ex.Message + " Closing link");
						client.Close();
					}
					catch (ArgumentException ex)
					{
						LogError(ex.Message + " Closing link");
						client.Close();
					}
					catch (SerializationException ex)
					{
						LogError(ex.Message + " Closing link");
						client.Close();
					}
					catch (SocketException)
					{
						LogError("Socket exception. Closing link");
						client.Close();
					}
					catch (Exception ex)
					{
						LogError(ex);
						LogError("Closing link");
						client.Close();
					}
					finally
					{
						LogEvent("End stream read");
					}
				}
				if (active)
				{
					while (!disposed && (client == null || !client.Connected))
					{
						try
						{
							LogEvent("Attempting to re-establish connection...");
							client = new TcpClient(Address.HostName, Address.Port);
						}
						catch (Exception)
						{
							Thread.Sleep(10);
						}
					}
					Dispatch(new AddressInfo(((Member)Parent).Address));
				}
				else
				{
					LogEvent("End read");
					return;	//about to be disposed anyway
				}
			}
			LogEvent("End read");
		}


	

		public void Dispatch(IDispatchable p)
		{
			if (!IsConnected)
				return;
			//LogEvent("Dispatching " + p);
			formatter.Serialize(client.GetStream(), p);
		}

		internal void ResetConsensusState()
		{
			MatchIndex = 0;
			NextIndex = -1;
			CommitIndex = 0;
			AppendTimeout = -1;
		}
	}
}
