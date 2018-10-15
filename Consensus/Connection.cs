using Base;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace Consensus
{

	internal class ConsensusState
	{

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

	}

	internal class Connection : Identity, IDisposable
	{
		protected TcpClient tcpClient;

		private volatile bool closing = false;
		private Thread readThread;
		protected readonly Node owner;
		protected BinaryFormatter formatter = new BinaryFormatter();

		public ConsensusState ConsensusState { get; set; } = new ConsensusState();


		public bool IsDisposed => closing;
		public bool IsConnected
		{
			get
			{
				return !closing && tcpClient != null && tcpClient.Connected;
			}
		}

		public long LastIncoming { get; private set; }

		public bool IsAlive => IsConnected && (Node.GetNanoTime() - LastIncoming <= 2 * 1000 * 1000 * 1000L);

		public Connection(Node owner, Address addr, TcpClient client) : base(owner, null)
		{
			this.owner = owner;
			Address = () => addr;
			Assign(client,null);
			LastIncoming = Node.GetNanoTime();
		}
		public Connection(Node owner, Func<Address> addr) : base(owner, addr)
		{
			this.owner = owner;
			LastIncoming = Node.GetNanoTime();
		}

		public delegate void Event(ActiveConnection connection);
		public delegate void DataEvent<T>(ActiveConnection connection, T obj);

		//private event Event onConnect = new Event();



		//		private SpinLock tcpLock = new SpinLock();
		Mutex serialLock = new Mutex();
		Thread serialLockedBy;
		protected void TcpLocked(Action action)
		{
			if (closing)
				return;
			if (!serialLock.WaitOne(1000))
			{
				throw new InvalidOperationException("Unable to acquire lock in 1000ms");
			}
			try
			{
				serialLockedBy = Thread.CurrentThread;
				action();
			}
			finally
			{
				serialLockedBy = null;
				serialLock.ReleaseMutex();
			}


			//bool ack = false;
			//tcpLock.Enter(ref ack);
			//if (!ack)
			//	throw new InvalidOperationException("Cannot lock spinlock");
			//try
			//{
			//	ac();
			//}
			//catch (Exception ex)
			//{
			//	LogError(ex);
			//	throw;
			//}
			//finally
			//{
			//	tcpLock.Exit();
			//}
		}

		protected void Assign(TcpClient newClient, Action doLocked)
		{
			TcpLocked(() => { tcpClient = newClient; doLocked?.Invoke(); });
			readThread = new Thread(new ThreadStart(ActiveThread));
			readThread.Start();
		}

		public void Dispose()
		{
			TcpLocked(() =>
			{
				closing = true;
				if (tcpClient != null)
				{
					//client.Close();
					tcpClient.Dispose();
					//tcpClient = null;
				}
			});
			if (readThread != null && readThread != Thread.CurrentThread)
				readThread.Join();
		}


		private void ActiveThread()
		{
			while (!closing)
			{
				if (tcpClient != null)
				{
					string reason = "";
					try
					{
						LogMinorEvent("Begin stream read");

						while (IsConnected)
						{
							NetworkStream stream = null;
							IDispatchable item;
							try
							{
								TcpLocked(() => stream = tcpClient.Connected ? tcpClient.GetStream() : null);
								if (stream == null)
									continue;
								item = formatter.Deserialize(stream) as IDispatchable;
							}
							finally
							{
								//stream.Close();
							}
							try
							{
								//LogEvent("Deserialized inbound " + item);
								item.OnArrive(owner, this);
								LastIncoming = Node.GetNanoTime();
							}
							catch (Exception ex)
							{
								LogError("On implement " + item + " : " + ex);
							}
						}
						reason = "Connection lost d=" + closing + ",c=" + tcpClient.Connected;
					}
					catch (ObjectDisposedException ex)
					{
						LogError(ex.Message);
						reason = ex.Message;
					}
					catch (IOException ex)
					{
						LogError(ex.Message + " Closing link");
						TcpLocked(() => tcpClient.Dispose());
						reason = ex.Message;
					}
					catch (ArgumentException ex)
					{
						LogError(ex.Message + " Closing link");
						TcpLocked(() => tcpClient.Dispose());
						reason = ex.Message;
					}
					catch (SerializationException ex)
					{
						LogError(ex.Message + " Closing link");
						TcpLocked(() => tcpClient.Dispose());
						reason = ex.Message;
					}
					catch (SocketException ex)
					{
						LogError("Socket exception. Closing link");
						TcpLocked(() => tcpClient.Dispose());
						reason = ex.Message;
					}
					catch (Exception ex)
					{
						LogError(ex);
						LogError("Closing link");
						TcpLocked(() => tcpClient.Dispose());
						reason = ex.Message;
					}
					finally
					{
						LogMinorEvent("End stream read " + reason);
					}
				}
				if (!Reconnect())
				{
					LogMinorEvent("End read");
					return; //about to be disposed anyway
				}
			}
			LogMinorEvent("End read");
		}


		protected virtual bool Reconnect()
		{
			return false;
		}




		public void Dispatch(IDispatchable p)
		{
			//if (!IsConnected)
			//return;
			try
			{
				TcpLocked(() =>
				{
					var c = tcpClient;
					if (c != null && c.Connected)
					{
						//LogEvent("Dispatching " + p);
						try
						{
							var stream = c.GetStream();
							formatter.Serialize(stream, p);
						}
						catch (Exception ex)
						{
							LogError(ex);
							if (!(this is ActiveConnection))
								Dispose();
						}
					}
					else
					{
						LogMinorEvent("Cannot dispatch " + p);
					}
				});
			}
			catch (Exception ex)
			{
				LogError(ex);
				if (!(this is ActiveConnection))
					Dispose();
			}
		}

		internal void ResetConsensusState()
		{
			TcpLocked(() => ConsensusState = new ConsensusState());
		}


	}




	internal class ActiveConnection : Connection
	{
		public readonly int MemberIndex;

		public ActiveConnection(Node owner, Func<Address> addr, int idx) : base(owner, addr)
		{
			MemberIndex = idx;
			Connect();
		}

		private void Begin()
		{
			tcpClient.GetStream().Write(BitConverter.GetBytes(MemberIndex),0,4);
		}

		private ActiveConnection Connect()
		{
			Assign(null, null);
			//try
			//{
			//	var addr = Address();
			//	LogEvent("Attempting to connect to " + addr);
			//	var cl = new TcpClient(addr.HostName, addr.Port);
			//	Assign(cl,Begin);
			//}
			//catch
			//{
			//	Assign(null,null);
			//}
			return this;
		}

		protected override bool Reconnect()
		{
			LogMinorEvent("Reconnecting...");
			TcpClient nextClient = null;
			while (!IsDisposed && (nextClient == null || !nextClient.Connected))
			{
				try
				{
					var addr = Address();
					if (addr.IsEmpty)
					{
						LogMinorEvent("Unable to connect right now. Host address not known");
						Thread.Sleep(500);
						continue;
					}
					LogMinorEvent("Attempting to re-establish connection to "+addr);
					nextClient = new TcpClient(addr.Host,addr.Port);
					break;
				}
				catch { }
			}
			if (!IsDisposed)
			{
				LogMinorEvent("Connection established");
				TcpLocked(() =>
				{
					if (tcpClient != null)
					{
						tcpClient.Dispose();
						//tcpClient = null;
					}
					tcpClient = nextClient;
					Begin();
				});
				return true;
			}
			return false;
		}
	}
}
