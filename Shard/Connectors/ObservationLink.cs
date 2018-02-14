using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shard
{
	/// <summary>
	/// Link to an SDS observing client.
	/// Communication is treated just like ordinary inter-entity messages, except they have an external origin guid,
	/// or maximum range.
	/// The remote end is expected to be a .net program that references the ShardBase assembly to deserialize SDS,
	/// logics, appeartances etc.
	/// </summary>
	public class ObservationLink
	{
		//public static int Port { get; set; } = 15235;

		public class Listener : IDisposable
		{
			private readonly TcpListener server;
			private readonly Thread listenerThread;

			public Action<ObservationLink> OnNewLink { get; set; }

			public Listener(int port)
			{
				Log.Message("Starting observation link listener on port "+port);
				server = new TcpListener(IPAddress.Any, port);
				server.Start();
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
						TcpClient client = server.AcceptTcpClient();
						try
						{
							IPEndPoint addr = (IPEndPoint)client.Client.RemoteEndPoint;
							Log.Message("Handling observation connection from " + addr);
							Establish(client);
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



		private static List<ObservationLink> registry = new List<ObservationLink>();

		private readonly TcpClient client;
		private readonly Thread writeThread;
		private readonly IPEndPoint endPoint;
		private readonly NetworkStream netStream;
		//private readonly MemoryStream writeStream = new MemoryStream();
		private readonly BlockingCollection<object> compressQueue = new BlockingCollection<object>();
		private bool closed = false;


		public ObservationLink(TcpClient client)
		{
			this.client = client;
			netStream = client.GetStream();
			endPoint = (IPEndPoint)client.Client.RemoteEndPoint;
			writeThread = new Thread(new ThreadStart(WriterMain));
			writeThread.Start();
			SendCompressed(Simulation.ID);

			foreach (var n in Simulation.Neighbors)
				SendCompressed(n.ShardPeerAddress);

			if (sentProviders.Count != 0)
				throw new Exception("Inconsistent start");
			SendSDS(Simulation.Stack.NewestFinishedSDS);
		}

		private void Message(string msg)
		{
			Log.Message(endPoint + ": " + msg);
		}
		private void Error(string msg)
		{
			Log.Error(endPoint + ": " + msg);
		}
		private void Error(Exception ex)
		{
			Log.Error(endPoint + ": " + ex);
		}



		private void WriterMain()
		{
			Message("Starting Write");
			try
			{
				var f = new BinaryFormatter();
				using (var compressor = new LZ4.LZ4Stream(netStream, LZ4.LZ4StreamMode.Compress))
				{
					while (!closed)
					{
						var obj = compressQueue.Take();
						//Message("sending "+obj);
						f.Serialize(compressor, obj);
						//Message("flushing");
						//compressor.Flush();
						//netStream.Flush();
					}

				}
			}
			catch (ObjectDisposedException)
			{ }
			catch (SocketException)
			{ }
			catch (Exception ex)
			{
				Error(ex);
			}
			Close();
		}

		private void Close()
		{
			lock (this)
			{
				if (closed)
					return;
				closed = true;
				Message("Connection closing...");

				try { netStream.Close(); } catch { }
				try { client.Close(); } catch { }
				try { client.Dispose(); } catch { }
				try { compressQueue.Dispose(); } catch { }
				lock (registry)
					registry.Remove(this);
			}
		}

		public static ObservationLink Establish(TcpClient client)
		{
			try
			{
				ObservationLink rs = new ObservationLink(client);
				Log.Message("Registering " + client.Client.RemoteEndPoint);
				lock (registry)
				{
					registry.Add(rs);
				}
				return rs;
			}
			catch
			{ }
			return null;
		}

		
		public void SendCompressed(object serializable)
		{
			if (closed)
				return;
			try
			{
				if (compressQueue.Count > 16)
				{
					Message("More than 16 packages queued up for compression. Closing down");
					Close();
					return;
				}
				//Log.Message("Added "+serializable);
				compressQueue.Add(serializable);
			}
			catch (Exception ex)
			{
				Error(ex);
				Close();
			}
		}

		private ConcurrentDictionary<string, bool> sentProviders = new ConcurrentDictionary<string, bool>();

		public void SendSDS(SDS sds)
		{
			if (closed)
				return;
			SendNewProvidersOf(sds);
			SendCompressed(sds);
		}

		private void SendProvider(CSLogicProvider p)
		{
			if (p != null && sentProviders.TryAdd(p.AssemblyName, true))
			{
				//Message("SendProvider: "+p);
				//Log.Message("Sending provider " + p.AssemblyName);
				if (p.Dependencies != null)
					foreach (var d in p.Dependencies)
						SendProvider(d.Provider.Get());
				SendCompressed(p);
			}
			//else
			//	Message("SendProvider rejected: " + p);

		}

		private void SendNewProvidersOf(SDS sds)
		{
			//Message("Checking providers of g" + sds.Generation);
			foreach (var e in sds.FinalEntities)
			{
				DynamicCSLogic logic = e.MyLogic as DynamicCSLogic;
				if (logic == null)
					continue;
				if (string.IsNullOrEmpty(logic.Provider.AssemblyName))
					throw new IntegrityViolation("");
				//Message("Checking logic " + logic.Provider);
				SendProvider(logic.Provider);
			}
		}

		public bool Connected
		{
			get
			{
				return !closed && client.Connected;
			}
		}

		public Action<Guid, Guid, byte[]> OnMessage { get; set; }
		public Action<Guid> OnUnregisterReceiver { get; set; }
		public Action<Guid> OnRegisterReceiver { get; set; }




		public static byte[] Compress(object serializable)
		{
			using (MemoryStream ms = new MemoryStream())
			using (var stream = new LZ4.LZ4Stream(Helper.Serialize(serializable), LZ4.LZ4StreamMode.Compress))
			{
				stream.CopyTo(ms);
				return ms.ToArray();
			}
		}

		public static void SignalUpdate(SDS sds)
		{
			lock (registry)
			{
				foreach (var o in registry)
					o.SendSDS(sds);
			}
		}


		public void Dispose()
		{
			Close();
		}

	}
}
