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
	internal class Connection : Identity, IDisposable
	{
		private TcpClient client;
		private volatile bool disposed = false;
		private bool active = false;
		public bool Established => !disposed && client.Connected;
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

		public Connection(Member owner, Address addr):base(addr)
		{
			this.owner = owner;
			Connect(addr);
		}

		public Connection(Member owner, Address addr, TcpClient client):base(addr)
		{
			this.owner = owner;
			Assign(client);
		}

		public delegate void Event(Connection connection);
		public delegate void DataEvent<T>(Connection connection, T obj);

		private event Event onConnect;


		private Connection Connect(Address addr)
		{
			active = true;
			Assign(new TcpClient(addr.HostName, addr.Port));
			return this;
		}

		private void Assign(TcpClient newClient)
		{
			Debug.Assert(client == null);
			client = newClient;
			activeThread = new Thread(new ThreadStart(ActiveThread));
			activeThread.Start();
		}

		public void Dispose()
		{
			if (disposed)
				return;
			if (activeThread != null)
				activeThread.Join();
			disposed = true;
			if (client != null)
				client.Dispose();
		}


		private void ActiveThread()
		{
			while (!disposed)
			{
				try
				{
					var stream = client.GetStream();

					while (!disposed && client.Connected)
					{
						var item = formatter.Deserialize(stream) as IDispatchable;
						try
						{
							item.OnArrive(owner,this);
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

				if (active)
				{
					while (!disposed && !client.Connected)
					{
						try
						{
							client = new TcpClient(Address.HostName, Address.Port);
						}
						catch (Exception)
						{
							Thread.Sleep(1000);
						}
					}
					lock (onConnect)
						onConnect(this);
				}
				else
				{
					return;	//about to be diposed anyway
				}
			}
		}


		public event Event OnConnect
		{
			add
			{
				lock (onConnect)
				{
					onConnect += value;
					if (Established)
						value(this);
				}
			}
			remove
			{
				lock (onConnect)
				{
					onConnect -= value;
				}
			}
		}

		public void Dispatch(IDispatchable p)
		{
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
