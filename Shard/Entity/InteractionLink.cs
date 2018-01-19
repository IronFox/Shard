using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using VectorMath;

namespace Shard
{
	/// <summary>
	/// Link to an interacting client.
	/// Communication is treated just like ordinary inter-entity messages, except they have an external origin guid, or maximum range.
	/// They may be targeted to a specific entity or broadcast (target guid is empty)
	/// </summary>
//	[Serializable]
	public class InteractionLink : /*ISerializable,*/ IDisposable
	{
		public enum ChannelID
		{
			RegisterLink = 1,		//[int[4]: shardID]
			RegisterReceiver = 2,	//[Guid: me], may be called multiple times, to receive messages to different identities
			UnregisterReceiver = 3,	//[Guid: me], deauthenticate
			SendMessage = 4,		//c2s: [Guid: me][Guid: toEntity][int: channel][uint: num bytes][bytes...], s2c: [Guid: fromEntity][Guid: toReceiver][int: generation][int: channel][uint: num bytes][bytes...]
			Observe = 5,			//
			StopObservation = 6,	//
			Observation = 7,		//s2c: [SDS: new TLG SDS]
		}


		public struct OutPackage
		{
			public readonly uint Channel;
			public readonly byte[] Data;

			public OutPackage(uint channel, byte[] data)
			{
				Channel = channel;
				Data = data;
			}
		}

		private int orderIndex=0;

		private static List<InteractionLink> registry = new List<InteractionLink>();

		private readonly TcpClient client;
		private readonly Thread thread, writeThread;
		private readonly IPEndPoint endPoint;
		private readonly NetworkStream netStream;
		//private readonly MemoryStream writeStream = new MemoryStream();
		private readonly BlockingCollection<OutPackage> writeQueue = new BlockingCollection<OutPackage>();
		private bool closed = false;
		private readonly Func<Host, Link> linkLookup;

		private HashSet<Guid> guids = new HashSet<Guid>();
		private bool observe = false;

		public InteractionLink(TcpClient client, Func<Host, Link> linkLookup)
		{
			this.linkLookup = linkLookup;
			this.client = client;
			netStream = client.GetStream();
			endPoint = (IPEndPoint)client.Client.RemoteEndPoint;
			thread = new Thread(new ThreadStart(ThreadMain));
			thread.Start();
			writeThread = new Thread(new ThreadStart(WriterMain));
			writeThread.Start();
		}

		private void Message(string msg)
		{
			Log.Message(endPoint + ": "+msg);
		}
		private void Error(string msg)
		{
			Log.Error(endPoint + ": " + msg);
		}
		private void Error(Exception ex)
		{
			Log.Error(endPoint + ": " + ex);
		}

		private int remainingBytes = 0;

		private byte[] ReadBytes(int numBytes)
		{
			if (remainingBytes < numBytes)
				throw new SerializationException("Not enough bytes left in packet to deserialize "+numBytes+" byte(s)");
			byte[] rs = new byte[numBytes];
			netStream.Read(rs, numBytes);
			remainingBytes -= numBytes;
			return rs;
		}

		private static byte[] skipBuffer = new byte[1024];
		private void Skip(int bytes)
		{
			while (bytes > skipBuffer.Length)
			{
				netStream.Read(skipBuffer, skipBuffer.Length);
				bytes -= skipBuffer.Length;
			}
			if (bytes == 0)
				return;
			netStream.Read(skipBuffer, bytes);
		}

		private Guid NextGuid()
		{
			return new Guid(ReadBytes(16));
		}

		private int NextInt()
		{
			return BitConverter.ToInt32(ReadBytes(4), 0);
		}
		private float NextFloat()
		{
			return BitConverter.ToSingle(ReadBytes(4), 0);
		}

		private Vec3 NextVec3()
		{
			byte[] data = ReadBytes(12);
			return new Vec3(BitConverter.ToSingle(data,0), BitConverter.ToSingle(data, 4), BitConverter.ToSingle(data, 8));
		}

		private ShardID NextShardID()
		{
			byte[] raw = ReadBytes(4 * 4);
			return new ShardID(BitConverter.ToInt32(raw, 0), BitConverter.ToInt32(raw, 4), BitConverter.ToInt32(raw, 8), BitConverter.ToInt32(raw, 12));
		}

		private byte[] NextBytes()
		{
			int numBytes = BitConverter.ToInt32(ReadBytes(4),0);
			return ReadBytes(numBytes);
		}


		private void Abandon()
		{
			lock (this)
			{
				closed = true;
				writeQueue.Dispose();
				foreach (var g in guids)
					guidMap.TryRemove(g);
				guids.Clear();
			}
		}

		private void WriterMain()
		{
			Message("Starting Write");
			try
			{
				while (!closed)
				{
					OutPackage p = writeQueue.Take();
					netStream.Write(BitConverter.GetBytes(p.Channel), 0, 4);
					netStream.Write(BitConverter.GetBytes(p.Data.Length), 0, 4);
					netStream.Write(p.Data, 0, p.Data.Length);
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

		private void ThreadMain()
		{

			Message("Starting Read");

			try
			{
				var f = new BinaryFormatter();

				byte[] header = new byte[8];
				while (!closed)
				{
					netStream.Read(header, 8);    //channel + size
					uint channel = BitConverter.ToUInt32(header, 0);
					remainingBytes = BitConverter.ToInt32(header, 4);


					try
					{
						switch (channel)
						{
							case (uint)ChannelID.RegisterLink:
								ShardID id = NextShardID();
								var lnk = linkLookup?.Invoke(new Host(id));
								lnk.SetPassiveClient(client);
								Abandon();
								break;
							case (uint)ChannelID.RegisterReceiver:
								Guid guid = NextGuid();
								if (!guids.Contains(guid))
								{
									Message("Authenticating as " + guid);
									guids.Add(guid);
									while (!guidMap.TryAdd(guid, this))
									{
										InteractionLink link;
										if (guidMap.TryRemove(guid, out link))
											Message("Was already registered by " + link + ". Replacing...");
									}
									Message("Authenticated as " + guid);
									OnRegisterReceiver?.Invoke(guid);
								}
								break;
							case (uint)ChannelID.UnregisterReceiver:
								guid = NextGuid();
								if (guids.Contains(guid))
								{
									Message("De-Authenticating as " + guid);
									guids.Remove(guid);
									guidMap.TryRemove(guid);
									OnUnregisterReceiver?.Invoke(guid);
								}
								break;
							case (uint)ChannelID.SendMessage:
								Guid from = NextGuid();
								Guid to = NextGuid();
								int msgChannel = NextInt();
								byte[] data = NextBytes();
								if (!guids.Contains(from))
									Error("Not registered as " + from + ". Ignoring message");
								else
								{
									if (Simulation.Stack.Size > 0)
										Simulation.ClientMessageQueue.HandleIncomingMessage(from, to, msgChannel,data, orderIndex++);
									OnMessage?.Invoke(from,to,data);
								}
								break;

							case (uint)ChannelID.Observe:
								if (!observe && Simulation.Stack.Size > 0)
									SendSDS(Compress(Simulation.Stack.NewestSDS));
								observe = true;
								break;
							case (uint)ChannelID.StopObservation:
								observe = false;
								break;
						}
					}
					catch (SerializationException ex)
					{
						Error(ex);
					}
					Skip(remainingBytes);

				}

			}
			catch (SocketException)
			{}
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
				foreach (var g in guids)
					guidMap.TryRemove(g);
				guids.Clear();

				try { netStream.Close(); } catch { }
				try { client.Close(); } catch { }
				try { client.Dispose(); } catch { }
				try { writeQueue.Dispose(); } catch { }
				lock (registry)
					registry.Remove(this);
			}
		}

		public static InteractionLink Establish(TcpClient client, Func<Host, Link> linkLookup)
		{
			try
			{
				InteractionLink rs = new InteractionLink(client, linkLookup);
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

		public void Send(OutPackage pack)
		{
			if (closed)
				return;
			try
			{
				if (writeQueue.Count > 16)
				{
					Message("More than 16 packages queued up for dispatch. Closing down");
					Close();
					return;
				}
				writeQueue.Add(pack);
			}
			catch (Exception ex)
			{
				Error(ex);
				Close();
			}


		}

		public void RelayMessage(Guid senderEntity, Guid receiverID, int channel, byte[] data, int generation)
		{
			if (closed)
				return;
			using (MemoryStream ms = new MemoryStream())
			{
				ms.Write(senderEntity.ToByteArray(), 0, 16);
				ms.Write(receiverID.ToByteArray(), 0, 16);
				ms.Write(BitConverter.GetBytes(generation), 0, 4);
				ms.Write(BitConverter.GetBytes(channel), 0, 4);
				ms.Write(BitConverter.GetBytes((uint)data.Length), 0, 4);
				ms.Write(data, 0, data.Length);
				Send(new OutPackage((uint)ChannelID.SendMessage, ms.ToArray()));
			}
		}

		public void SendSDS(byte[] serializedSDS)
		{
			if (closed)
				return;
			Send(new OutPackage((uint)ChannelID.Observation, serializedSDS));
		}

		public bool Connected
		{
			get
			{
				return !closed && client.Connected;
			}
		}

		public Action<Guid,Guid,byte[]> OnMessage { get; set; }
		public Action<Guid> OnUnregisterReceiver { get; set; }
		public Action<Guid> OnRegisterReceiver { get; set; }


		public static InteractionLink Lookup(Guid guid)
		{
			InteractionLink link;
			if (guidMap.TryGetValue(guid, out link))
				return link;
			return null;
		}

		private static ConcurrentDictionary<Guid, InteractionLink> guidMap = new ConcurrentDictionary<Guid, InteractionLink>();


		public static byte[] Compress(SDS sds)
		{
			using (MemoryStream ms = new MemoryStream())
			using (var stream = new LZ4.LZ4Stream(Helper.Serialize(sds), LZ4.LZ4StreamMode.Compress))
			{
				stream.CopyTo(ms);
				return ms.ToArray();
			}
		}

		public static void SignalUpdate(SDS sds)
		{
			byte[] serial = null;
			foreach (var link in guidMap.Values)
				if (link.observe)
				{
					if (serial == null)
						serial = Compress(sds);
					link.SendSDS(serial);
				}
		}

		public static void Relay(Guid sender, Guid receiver, int channel, byte[] data, int generation)
		{
			InteractionLink link = Lookup(receiver);
			if (link != null)
				link.RelayMessage(sender,receiver,channel,data, generation);
		}

		public void Dispose()
		{
			Close();
		}
	}

}