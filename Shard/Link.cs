using VectorMath;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using System.Net;
using Base;

namespace Shard
{
	internal class Outbound : IDisposable
	{
		private ConcurrentQueue<Tuple<string, object>> dispatch = new ConcurrentQueue<Tuple<string, object>>()
									, sent = new ConcurrentQueue<Tuple<string, object>>();

		private const int MaxDispatch = 1000;
		private Semaphore sem = new Semaphore(0, MaxDispatch);
		private bool disposed = false;

		public Outbound Destroy()
		{
			Outbound rs = new Outbound();
			Tuple<string,object> item;
			if (dispatch != null)
				while (dispatch.TryDequeue(out item))
					rs.Set(item.Item1, item.Item2);
			if (sent != null)
				while (sent.TryDequeue(out item))
					rs.Set(item.Item1, item.Item2);

			Dispose();
			return rs;
		}


		public object GetNext()
		{
			if (disposed)
				return null;
			sem.WaitOne();
			Tuple<string,object> rs = null;
			if (dispatch.TryDequeue(out rs))
			{
				sent.Enqueue(rs);
				return rs.Item2;
			}
			return null;
		}

		/// <summary>
		/// Inserts or updates an existing object in the outbound queue.
		/// If an equal element is already contained, nothing happens.
		/// Otherwise the element is rescheduled for transfer.
		/// </summary>
		/// <param name="key">Key to look for/insert as</param>
		/// <param name="item">Item to insert</param>
		/// <returns>true if the element was inserted or updated, false if a matching object was found with the same key</returns>
		public bool Set(string key, object item)
		{
			if (disposed)
				throw new ObjectDisposedException("Outbound");
			bool enq = true;
			Filter((k, obj) =>
			{
				if (k != key)
					return true;	//keep (mismatching key)
				if (obj.Equals(item))
				{
					enq = false;
					return true;	//keep (is equal)
				}
				return false;
			}
			);
			if (enq)
				dispatch.Enqueue(new Tuple<string,object>(key,item));
			sem.Release();
			return enq;
		}

		SpinLock filterLock = new SpinLock();

		public bool IsDisposed { get { return disposed; } }

		public int ItemCount { get { return sent.Count + dispatch.Count; } }

		public int SentCount { get { return sent.Count; } }

		public int Filter(Func<string,object,bool> filter)
		{
			int removed = 0;
			filterLock.DoLocked(()=>
			{
				try
				{

					Queue<Tuple<string, object>> temp = new Queue<Tuple<string, object>>();
					Tuple<string, object> tmp;
					while (dispatch.TryDequeue(out tmp))
						if (filter(tmp.Item1, tmp.Item2))
						{
							temp.Enqueue(tmp);
						}
						else
							removed++;
					while (temp.Count > 0)
						dispatch.Enqueue(temp.Dequeue());


					while (sent.TryDequeue(out tmp))
						if (filter(tmp.Item1, tmp.Item2))
						{
							temp.Enqueue(tmp);
						}
						else
							removed++;
					while (temp.Count > 0)
						sent.Enqueue(temp.Dequeue());

					//leave sem as is, allow null result of GetNext()
				}
				catch (Exception)
				{ }
			});
			return removed;
		}

		public void Dispose()
		{
			disposed = true;
			Filter((k, o) => false);
			sent = null;
			try
			{
				sem.Release(MaxDispatch);
			}
			catch
			{ }
			sem.Dispose();
		}
	}


	public sealed class Link : IDisposable
	{

		private Outbound outbound = new Outbound();

		
		public readonly ShardID ID;
		private Thread readThread,writeThread,connectThread;
		private TcpClient client;
		private NetworkStream stream;
		public readonly int LinearIndex;

		public Action<RCS, int> OnPutRCS { get; set; }

		public void UploadToDB(int generation, RCS rcs)
		{
			DB.PutAsync(new SerialRCS(GetOutboundRCSID(generation), rcs)).Wait();
			OnPutRCS(rcs, generation);
		}

		public readonly bool IsSibling;
		private Address address;


		private void UpdateAddress(Address addr)
		{
			if (address == addr)
				return;
			address = addr;
			ObservationLink.SignalAddressUpdate(FullShardAddress);
		}

		public Action<Link, object> OnData { get; set; } = (lnk, obj) => Simulation.FetchIncoming(lnk, obj);


		public readonly bool IsActive;

		/// <summary>
		/// Creates a new link to a sibling shard
		/// </summary>
		/// <param name="id">Remote shard ID</param>
		/// <param name="isActive">Actively establish the connection. If false, wait for inbound connection</param>
		/// <param name="linearIndex">Linear index in the neighborhood</param>
		/// <param name="isSibling">True if this is a link to a sibling shard (not a neighbor)</param>
		public Link(ShardID id, bool isActive, int linearIndex, bool isSibling) : this(BaseDB.TryGetPeerAddress(id),isActive,linearIndex,isSibling)
		{
			ID = id;
		}
		/// <summary>
		/// Creates a new link to a sibling shard
		/// </summary>
		/// <param name="remoteHost">Address of the remote shard</param>
		/// <param name="isActive">Actively establish the connection. If false, wait for inbound connection</param>
		/// <param name="linearIndex">Linear index in the neighborhood</param>
		/// <param name="isSibling">True if this is a link to a sibling shard (not a neighbor)</param>
		public Link(Address remoteHost, bool isActive, int linearIndex, bool isSibling)
		{
			IsSibling = isSibling;
			LinearIndex = linearIndex;
			UpdateAddress(remoteHost);
			IsActive = isActive;
			if (isActive)
				StartConnectionThread();

			writeThread = new Thread(new ThreadStart(WriteMain));
			writeThread.Start();

			Log.Message(Name + ": Created");
		}
		private void StartConnectionThread()
		{
			onComm.Reset();
			if (connectThread != null)
				connectThread.Join();
			if (ConnectionIsActive)
				return;
			connectThread = new Thread(new ThreadStart(Connect));
			connectThread.Start();
		}

		/// <summary>
		/// Queries the database ID used to identify the inbound RCS stack associated with this link
		/// </summary>
		public RCS.StackID InboundRCSStackID
		{
			get
			{
				return new RCS.StackID(ID.XYZ, Simulation.ID.XYZ);
			}
		}
		/// <summary>
		/// Queries the database ID used to identify the outbound RCS stack associated with this link
		/// </summary>
		public RCS.StackID OutboundRCSStackID
		{
			get
			{
				return new RCS.StackID(Simulation.ID.XYZ, ID.XYZ);
			}
		}
		/// <summary>
		/// Queries the generation ID of an outbound RCS
		/// </summary>
		/// <param name="gen">Generation to construct the ID for</param>
		/// <returns>Generational RCS ID</returns>
		public RCS.GenID GetOutboundRCSID(int gen)
		{
			return new RCS.GenID(OutboundRCSStackID, gen);
		}



		private bool dispose = false;

		private void Connect()
		{
			CloseThread(ref readThread);

			while (!dispose)
			{
				try
				{
					if (address.IsEmpty)
						TryRefreshAddress();
					if (!address.IsEmpty)
					{
						client = new TcpClient(address.Host, address.Port);

						var stream = client.GetStream();
						stream.Write(BitConverter.GetBytes((uint)InteractionLink.ChannelID.RegisterLink), 0, 4);
						stream.Write(BitConverter.GetBytes(32), 0, 4);
						WriteID(stream, Simulation.ID);
						WriteID(stream, ID);
						break;
					}
				}
				catch (Exception)
				{ }

				Thread.Sleep(1000);
				TryRefreshAddress();
			}
			StartCommunication();
		}

		private static void WriteID(NetworkStream stream, ShardID id)
		{
			stream.Write(BitConverter.GetBytes(id.X), 0, 4);
			stream.Write(BitConverter.GetBytes(id.Y), 0, 4);
			stream.Write(BitConverter.GetBytes(id.Z), 0, 4);
			stream.Write(BitConverter.GetBytes(id.ReplicaLevel), 0, 4);
		}

		private void TryRefreshAddress()
		{
			if (AddressLocked)
				return;
			var addr = BaseDB.TryGetPeerAddress(ID);
			UpdateAddress(addr);
		}

		/// <summary>
		/// Notifies this link of an established incoming neighbor/sibling connection.
		/// The local link must not be active
		/// </summary>
		/// <param name="newClient">Established TCP connection to the neighbor/sibling matching the expected remote shard ID</param>
		public void SetPassiveClient(TcpClient newClient)
		{
			if (dispose)
				return;
			Debug.Assert(!IsActive);

			try
			{
				if (this.stream != null)
				{
					this.stream.Close();
					this.stream = null;
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
			try
			{
				if (this.client != null)
				{
					this.client.Close();
					//this.client = null;
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}

			CloseThread(ref connectThread);
			CloseThread(ref readThread);

			this.client = newClient;
			TryRefreshAddress();
			StartCommunication();
		}


		private static void CloseThread(ref Thread thread)
		{
			if (thread != null)
			{
				try
				{
					thread.Join();
				}
				catch (Exception ex)
				{
					Log.Error(ex);
				}
				thread = null;
			}
		}

		private ManualResetEvent onComm = new ManualResetEvent(false);

		private EventWaitHandle writerIsWaiting = new AutoResetEvent(false),
								writerShouldStart = new AutoResetEvent(false);


		private void StartCommunication()
		{
			stream = client.GetStream();
			readThread = new Thread(new ThreadStart(ReadMain));
			readThread.Start();
			var newOutbound = outbound.Destroy();
			//Log.Message(Name + ": Connecting to " + host);
			writerIsWaiting.WaitOne();
			outbound = newOutbound;
			writerShouldStart.Set();
			Log.Message(Name+ ": Connected to "+address);
			onComm.Set();
		}

		/// <summary>
		/// Queries a string representation of the remote shard ID and status (e.g. "Active link to ...").
		/// </summary>
		public string Name
		{
			get
			{
				return (IsActive ? "Active" : "Passive")+" link to "+ID;
			}
		}

		public override string ToString()
		{
			return Name;
		}

		public bool IsResponsive { get { return ConnectionIsActive; } }

		private int oldestGeneration = 0;
		/// <summary>
		/// Queries the known oldest generation of the remote shard.
		/// 0 if unknown
		/// </summary>
		public int OldestGeneration
		{
			get
			{
				return oldestGeneration;
			}
		}

		/// <summary>
		/// Updates the latest known oldest generation of the remote shard.
		/// The new value must be greater or equal than the last known oldest generation.
		/// This method is called as a response to a package from the neighbor.
		/// If the value changed, the outbound RCS stack is trimmed accordingly.
		/// </summary>
		/// <param name="newOldestGeneration">Updated oldest generation of the linked shard</param>
		/// <param name="currentTLG">Current simulation Top Level Generation (TLG)</param>
		public void SetOldestGeneration(int newOldestGeneration, int currentTLG)
		{
			if (oldestGeneration == newOldestGeneration)
				return;
			oldestGeneration = newOldestGeneration;
#if !DRY_RUN
			if (OutStack != null)
				OutStack.SignalOldestGenerationUpdateAsync(Simulation.ID.ReplicaLevel, newOldestGeneration, currentTLG).Wait();
#endif
			Log.Message(Name + ": ->g" + newOldestGeneration);
		}

		public bool ConnectionIsActive
		{
			get
			{
				try
				{
					return client != null && client.Client != null && client.Connected;
				}
				catch
				{
					return false;
				}
			}
		}

		public int OutboundItemCount { get { return outbound.ItemCount; } }

		private void Read(byte[] data, int bytes)
		{
			int offset = 0;
			int remaining = bytes;
			while (remaining > 0)
			{
				int read = stream.Read(data, offset, remaining);
				if (read <= 0)
					throw new Exception("stream.Read() returned "+read);
				offset += read;
				remaining -= read;
			}
		}
		private void Read(byte[] buffer, Stream outData, int bytes)
		{
			int remaining = bytes;
			while (remaining > 0)
			{
				int read = stream.Read(buffer, 0, Math.Min(remaining,buffer.Length));
				if (read <= 0)
					throw new Exception("stream.Read() returned " + read);
				remaining -= read;
				outData.Write(buffer, 0, read);
			}
		}
		private static byte[] skipBuffer = new byte[0x1000];
		private void Skip(int bytes)
		{
			while (bytes > 0)
			{
				Read(skipBuffer, System.Math.Min(skipBuffer.Length, bytes));
				bytes -= skipBuffer.Length;
			}
		}


		private void ReadMain()
		{
			BinaryFormatter formatter = new BinaryFormatter();
			//outer loop
			try
			{
				//communication loop
				while (ConnectionIsActive)
				{
					OnData(this, formatter.Deserialize(stream));
				}
			}
			catch (IOException ex)
			{
				Log.Error(Name + ": "+ex.Message+" Closing link");
				client.Close();
			}
			catch (ArgumentException ex)
			{
				Log.Error(Name + ": " + ex.Message + " Closing link");
				client.Close();
			}
			catch (SerializationException ex)
			{
				Log.Error(Name + ": "+ex.Message+" Closing link");
				client.Close();
			}
			catch (SocketException)
			{
				Log.Error(Name + ": Socket exception. Closing link");
				client.Close();
			}
			catch (Exception ex)
			{
				Log.Error(ex);
				Log.Error(Name + ": Closing link");
				client.Close();
			}
			onComm.Reset();
			if (IsActive)
			{
				Thread.Sleep(1000);
				TryRefreshAddress();
				StartConnectionThread();
			}
			else
				TryRefreshAddress();

		}

		public void ClearOutData()
		{
			Filter((key, obj) => false);
		}

		public void Set(string id, object obj)
		{
			outbound.Set(id,obj);
		}
		public int Filter(Func<string, object, bool> filter)
		{
			return outbound.Filter(filter);
		}


		public int SentSinceLastReconnect { get; private set; } = 0;
		public int OutboundSentCount { get { return outbound.SentCount; } }

		public bool VerboseWriter { get; set; } = false;
		public Box WorldSpace
		{
			get
			{
				return Box.OffsetSize(new Vec3(ID.XYZ), new Vec3(1), ID.XYZ + 1 < Simulation.Extent.XYZ);
			}
		}

		/// <summary>
		/// Calculates the sub section of a local IC (of size CommonResolution) that should be exported to the local neighbor
		/// </summary>
		public IntBox ICExportRegion
		{
			get
			{
				var delta = ID.XYZ - Simulation.ID.XYZ;
				Int3 offset = (delta * InconsistencyCoverage.CommonResolution + 1).Clamp(0, InconsistencyCoverage.CommonResolution + 1);
				Int3 end = (delta * InconsistencyCoverage.CommonResolution + InconsistencyCoverage.CommonResolution - 1).Clamp(0, InconsistencyCoverage.CommonResolution + 1);
				return IntBox.FromMinAndMax(offset, end, Bool3.True);
			}
		}
		public IntBox ICImportRegion
		{
			get
			{
				var delta = ID.XYZ - Simulation.ID.XYZ;
				Int3 offset = (delta * InconsistencyCoverage.CommonResolution).Clamp(0, InconsistencyCoverage.CommonResolution - 1);
				Int3 end = (delta * InconsistencyCoverage.CommonResolution + InconsistencyCoverage.CommonResolution - 1).Clamp(0, InconsistencyCoverage.CommonResolution - 1);
				return IntBox.FromMinAndMax(offset, end, Bool3.True);
			}
		}

		public FullShardAddress FullShardAddress
		{
			get
			{
				return new FullShardAddress(ID, address.Host,address.Port,BaseDB.TryGetConsensusAddress(ID).Port);
			}
		}

		/// <summary>
		/// Checks whether this link should be connected.
		/// This is of particular relevance only when dealing with siblings.
		/// </summary>
		public bool ShouldBeConnected
		{
			get
			{
				TryRefreshAddress();
				return !address.IsEmpty;
			}
		}

		/// <summary>
		/// If set true, disallows DB fetches to update the remote address
		/// </summary>
		public bool AddressLocked { get; set; }

		private void WriteMain()
		{
			BinaryFormatter formatter = new BinaryFormatter();

			//byte[] frame = new byte[4];

			//outer loop
			while (!dispose)
			{
				LogDebugW(this + ": WriteMain() waiting for connection");
				WaitHandle.SignalAndWait(writerIsWaiting, writerShouldStart);
				LogDebugW(this + ": WriteMain() begin");

				SentSinceLastReconnect = 0;
				while (true)
				{
					try
					{
						if (ConnectionIsActive)
						{
							//communication loop
							//Log.Debug(this + ": WriteMain() waiting for next object");
							object send = outbound.GetNext();
							if (send == null)
								if (outbound.IsDisposed)
								{
									LogDebugW(this + ": WriteMain() interrupt (" + SentSinceLastReconnect + "). Outbound is disposed");
									break;
								}
								else
									continue;
							formatter.Serialize(stream, send);
							SentSinceLastReconnect++;
						}
						else
						{
							LogDebugW(this + ": WriteMain() interrupt (" + SentSinceLastReconnect + "). Connection died");
							break;
						}
					}
					catch (Exception ex)
					{
						LogDebugW(this + ": WriteMain() exception (" + SentSinceLastReconnect + ")");
						Log.Error(ex);
						client.Close();
						break;
					}
				}
				//Log.Debug(this + ": WriteMain() restart");
			}
		}


		private void LogDebugW(string msg)
		{
			if (VerboseWriter)
				Log.Debug(msg);
		}

		public void Dispose()
		{
			GC.SuppressFinalize(this);
			dispose = true;

			try
			{
				if (connectThread != null)
					connectThread.Join();
			}
			catch { }

			try
			{
				if (client != null)
					client.Close();
			}
			catch { }
			try
			{
				outbound.Dispose();
				writerShouldStart.Set();
			}
			catch { }
			try
			{
				if (writeThread != null)
					writeThread.Join();
			}
			catch { }

			Helper.Dispose(client);
			Helper.Dispose(stream);
			Helper.Dispose(writerShouldStart);
			Helper.Dispose(writerIsWaiting);
		}

		public void AwaitConnection()
		{
			onComm.WaitOne();
			//WaitHandle.WaitAny(new WaitHandle[]{ onComm});

		}
	}
}