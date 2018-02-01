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
		public static int Port { get; set; } = 16234;

		public class Listener : IDisposable
		{
			private readonly TcpListener server;
			private readonly Thread listenerThread;

			public Action<ObservationLink> OnNewLink { get; set; }

			public Listener()
			{
				Log.Message("Starting observation link listener on part "+Port);
				server = new TcpListener(IPAddress.Any, Port);
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
			SignalUpdate(Simulation.Stack.NewestSDS);
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
						f.Serialize(compressor, compressQueue.Take());
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
				foreach (var d in p.Dependencies)
					SendProvider(d.Provider.Get());
				SendCompressed(p);
			}
		}

		private void SendNewProvidersOf(SDS sds)
		{
			foreach (var e in sds.FinalEntities)
			{
				DynamicCSLogic logic = e.MyLogic as DynamicCSLogic;
				if (logic == null)
					continue;
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
