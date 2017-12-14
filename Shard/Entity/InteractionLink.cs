using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace Shard
{
	/// <summary>
	/// Link to an interacting client.
	/// Communication is treated just like ordinary inter-entity messages, except they have an external origin guid, or maximum range.
	/// They may be targeted to a specific entity or broadcast (target guid is empty)
	/// </summary>
	[Serializable]
	public class InteractionLink : ISerializable
	{
		public enum ChannelID
		{
			RegisterLink = 1,		//[int[4]: shardID]
			RegisterReceiver = 2,	//[Guid: me], may be called multiple times, to receive messages from entities
			UnregisterReceiver = 3,	//[Guid: me], deauthenticate
			SendMessage = 4,	//c2s: [Guid: me][Guid: toEntity][uint: num bytes][bytes...], s2c: [Guid: fromEntity][uint: num bytes][bytes...]
		}

		private int orderIndex=0;

		private static List<InteractionLink> registry = new List<InteractionLink>();

		private readonly TcpClient client;
		private readonly Thread thread;
		private readonly IPEndPoint endPoint;
		private readonly NetworkStream stream;
		private bool closed = false;
		private readonly Func<Host, Link> linkLookup;

		private HashSet<Guid> guids = new HashSet<Guid>();

		public InteractionLink(TcpClient client, Func<Host, Link> linkLookup)
		{
			this.linkLookup = linkLookup;
			this.client = client;
			thread = new Thread(new ThreadStart(ThreadMain));
			thread.Start();
			endPoint = (IPEndPoint)client.Client.RemoteEndPoint;
			stream = client.GetStream();
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
			stream.Read(rs, numBytes);
			remainingBytes -= numBytes;
			return rs;
		}

		private static byte[] skipBuffer = new byte[1024];
		private void Skip(int bytes)
		{
			while (bytes > skipBuffer.Length)
			{
				stream.Read(skipBuffer, skipBuffer.Length);
				bytes -= skipBuffer.Length;
			}
			if (bytes == 0)
				return;
			stream.Read(skipBuffer, bytes);
		}

		private Guid NextGuid()
		{
			return new Guid(ReadBytes(16));
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
				foreach (var g in guids)
					guidMap.TryRemove(g);
				guids.Clear();
			}
		}

		private void ThreadMain()
		{

			Message("Initiating session");

			try
			{
				var f = new BinaryFormatter();

				byte[] header = new byte[8];
				while (!closed)
				{
					stream.Read(header, 8);    //channel + size
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
								byte[] data = NextBytes();
								if (!guids.Contains(from))
									Error("Not registered as " + from + ". Ignoring message");
								else
								{
									if (Simulation.Stack.Size > 0)
										Simulation.ClientMessageQueue.HandleIncomingMessage(from, to, data, orderIndex++);
									OnMessage?.Invoke(from,to,data);
								}
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
				foreach (var g in guids)
					guidMap.TryRemove(g);
				guids.Clear();

				try
				{
					stream.Close();
					client.Close();
					client.Dispose();
					Message("Connection closed");
					lock (registry)
						registry.Remove(this);
				}
				catch
				{ }
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

		public void Send(Guid senderEntity, Guid receiverID, byte[] data)
		{
			if (closed)
				return;
			try
			{
				lock (stream)
				{
					stream.Write(BitConverter.GetBytes((uint)ChannelID.SendMessage), 0, 4);
					stream.Write(BitConverter.GetBytes(data.Length+36), 0, 4);
					stream.Write(senderEntity.ToByteArray(), 0, 16);
					stream.Write(receiverID.ToByteArray(), 0, 16);
					stream.Write(BitConverter.GetBytes((uint)data.Length), 0, 4);
					stream.Write(data, 0, data.Length);
				}
			}
			catch (Exception ex)
			{
				Error(ex);
				Close();
			}
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

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			//nothing
		}

		public static InteractionLink Lookup(Guid guid)
		{
			InteractionLink link;
			if (guidMap.TryGetValue(guid, out link))
				return link;
			return null;
		}

		private static ConcurrentDictionary<Guid, InteractionLink> guidMap = new ConcurrentDictionary<Guid, InteractionLink>();


		public static void Relay(Guid sender, Guid receiver, byte[] data)
		{
			InteractionLink link = Lookup(receiver);
			if (link != null)
				link.Send(sender,receiver,data);
		}
	}
}