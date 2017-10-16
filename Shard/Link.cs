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
	internal class Outbound
	{
		private ConcurrentQueue<object> dispatch = new ConcurrentQueue<object>()
								, sent = new ConcurrentQueue<object>();
		private Semaphore sem = new Semaphore(0, 1000);

		public void SignalConnectionRestart()
		{
			object item;
			int cnt = 0;
			while (sent.TryDequeue(out item))
			{
				dispatch.Enqueue(item);
				cnt++;
			}
			sem.Release(cnt > 0 ? cnt : 1);
		}


		public object GetNext()
		{
			sem.WaitOne();
			object rs = null;
			if (dispatch.TryDequeue(out rs))
			{
				sent.Enqueue(rs);
				return rs;
			}
			return null;
		}

		public void Add(object item)
		{
			dispatch.Enqueue(item);
			sem.Release();
		}

		SpinLock removeLock = new SpinLock();
		public void Filter(Func<object,bool> filter)
		{
			bool isIn = false;
			removeLock.Enter(ref isIn);
			if (!isIn)
				throw new Exception("Failed to acquire lock");
			try
			{

				Queue<object> temp = new Queue<object>();
				object tmp;
				while (dispatch.TryDequeue(out tmp))
					if (filter(tmp))
					{
						temp.Enqueue(tmp);
					}
				while (temp.Count > 0)
					dispatch.Enqueue(temp.Dequeue());


				while (sent.TryDequeue(out tmp))
					if (filter(tmp))
					{
						temp.Enqueue(tmp);
					}
				while (temp.Count > 0)
					sent.Enqueue(temp.Dequeue());

				//leave sem as is, allow null result of GetNext()
			}
			catch (Exception)
			{}
			removeLock.Exit();
		}

	}


	public class Link
	{

		private Outbound outbound = new Outbound();

		
		public readonly ShardID ID;
		private Thread readThread,writeThread,connectThread;
		private TcpClient client;
		private readonly Host host;
		private NetworkStream stream;
		private readonly Simulation simulation;

		public readonly bool IsActive;
		public Link(ShardID addr, Simulation simulation, bool isActive)
		{
			this.simulation = simulation;
			host = new Host(addr);
			IsActive = isActive;
			if (isActive)
				StartConnectionThread();

			writeThread = new Thread(new ThreadStart(WriteMain));
			writeThread.Start();

			Console.WriteLine(Name + ": Created");
		}
		private void StartConnectionThread()
		{
			if (connectThread != null)
				connectThread.Join();
			if (client.Connected)
				return;
			connectThread = new Thread(new ThreadStart(Connect));
			connectThread.Start();
		}

		private void Connect()
		{
			CloseThread(ref readThread);

			while (true)
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

		public void SetPassiveClient(TcpClient client)
		{
			Debug.Assert(!IsActive);

			try
			{
				if (client != null)
				{
					stream.Close();
					client.Close();
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine(ex);
			}

			CloseThread(ref connectThread);

			try
			{
				if (client != null && client.Connected)
					client.Close();
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine(ex);
			}

			CloseThread(ref readThread);

			this.client = client;
			stream = client.GetStream();
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
					Console.Error.WriteLine(ex);
				}
				thread = null;
			}
		}

		private Semaphore writeLock = new Semaphore(0,1);
		private Semaphore writeOut = new Semaphore(1, 1);

		private void StartCommunication()
		{
			readThread = new Thread(new ThreadStart(ReadMain));
			readThread.Start();
			outbound.SignalConnectionRestart();
			writeOut.WaitOne();
			writeLock.Release();
			Console.WriteLine(Name+ ": Connected to "+host);
		}

		public string Name
		{
			get
			{
				return (IsActive ? "Active" : "Passive")+" link to "+ID;
			}
		}

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
				while (client.Connected)
				{
					object obj = formatter.Deserialize(stream);
					simulation.FetchIncoming(obj);

				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine(ex);
				client.Close();
				if (IsActive)
					StartConnectionThread();
				return;
			}
		}


		public void Add(object obj)
		{
			outbound.Add(obj);
		}
		public void Filter(Func<object, bool> filter)
		{
			outbound.Filter(filter);
		}

		private void WriteMain()
		{
			BinaryFormatter formatter = new BinaryFormatter();

			//outer loop
			while (true)
			{
				writeLock.WaitOne();
				while (true)
				{
					try
					{
						if (client != null && client.Connected)
						{
							//communication loop
							object send = outbound.GetNext();
							if (send == null)
								continue;
							formatter.Serialize(stream, send);
						}
						else
							break;
					}
					catch (Exception ex)
					{
						Console.Error.WriteLine(ex);
						client.Close();
						break;
					}
				}
				writeOut.Release();
			}
		}

	}
}