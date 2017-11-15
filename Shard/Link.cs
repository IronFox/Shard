using VectorMath;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

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
			while (dispatch.TryDequeue(out item))
				rs.Set(item.Item1, item.Item2);
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
			bool isIn = false;
			int removed = 0;
			filterLock.Enter(ref isIn);
			if (!isIn)
				throw new Exception("Failed to acquire lock");
			try
			{

				Queue<Tuple<string, object>> temp = new Queue<Tuple<string, object>>();
				Tuple<string,object> tmp;
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
			{}
			filterLock.Exit();
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
		private readonly Host host;
		private NetworkStream stream;
		public readonly int LinearIndex;
		public readonly bool IsSibling;


		public Action<Link, object> OnData { get; set; } = (lnk, obj) => Simulation.FetchIncoming(lnk, obj);


		public readonly bool IsActive;
		public Link(ShardID remoteAddr, bool isActive, int linearIndex, bool isSibling) : this(new Host(remoteAddr),isActive,linearIndex,isSibling)
		{
			ID = remoteAddr;
		}

		public Link(Host remoteHost, bool isActive, int linearIndex, bool isSibling)
		{
			IsSibling = isSibling;
			LinearIndex = linearIndex;
			host  = remoteHost;
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

		public RCS.GenID InboundRCS(int generation)
		{
			return new RCS.GenID(ID.XYZ, Simulation.ID.XYZ, generation);
		}

		public RCS.GenID OutboundRCS(int generation)
		{
			return new RCS.GenID(Simulation.ID.XYZ, ID.XYZ, generation);
		}

		private bool dispose = false;

		private void Connect()
		{
			CloseThread(ref readThread);

			while (!dispose)
			{
				try
				{
					client = new TcpClient(host.URL, host.Port);
					break;
				}
				catch (Exception)
				{ }

				Thread.Sleep(1000);
			}
			StartCommunication();
		}

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
			Log.Message(Name+ ": Connected to "+host);
			onComm.Set();
		}

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
		public int OldestGeneration { get; internal set; }
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
			byte[] frame = new byte[4];
			byte[] buffer = new byte[client.ReceiveBufferSize];
			//outer loop
			try
			{
				//communication loop
				while (ConnectionIsActive)
				{
					Read(frame, 4);
					int size = BitConverter.ToInt32(frame,0);
					//Log.Debug(this + ": ReadMain() read frame of size "+size);
					using (var ms = new MemoryStream())
					{
						Read(buffer,ms, size);
						//Log.Debug(this + ": ReadMain() deserializing");
						ms.Seek(0, SeekOrigin.Begin);
						object obj = formatter.Deserialize(ms);
						//Log.Debug(this + ": ReadMain() dispatching " +obj);
						OnData(this, obj);
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex);
				client.Close();
			}
			onComm.Reset();
			//Log.Debug(this + ": ReadMain() exit");
			if (IsActive)
				StartConnectionThread();
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

		private void WriteMain()
		{
			BinaryFormatter formatter = new BinaryFormatter();

			byte[] frame = new byte[4];

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
							using (var ms = new MemoryStream())
							{
								//Log.Debug(this + ": WriteMain() sending "+send);

								formatter.Serialize(ms, send);
								ByteBuffer.Put(frame, 0, (int)ms.Length);
								stream.Write(frame, 0, frame.Length);
								ms.Seek(0, SeekOrigin.Begin);
								ms.CopyTo(stream);
								SentSinceLastReconnect++;

								//Log.Debug(this + ": WriteMain() "+ms.Length+" byte(s) sent");
							}
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

			client.Dispose();
			stream.Dispose();
			writerShouldStart.Dispose();
			writerIsWaiting.Dispose();
		}

		public void AwaitConnection()
		{
			onComm.WaitOne();
			//WaitHandle.WaitAny(new WaitHandle[]{ onComm});

		}
	}
}