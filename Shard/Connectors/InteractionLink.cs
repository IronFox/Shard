using Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using VectorMath;

namespace Shard
{
	/// <summary>
	/// Link to an interacting client.
	/// Communication is treated just like ordinary inter-entity messages, except they have an external origin guid, or maximum range.
	/// They may be targeted to a specific entity or broadcast (target guid is empty)
	/// </summary>
	public class InteractionLink : IDisposable
	{
		public enum ChannelID
		{
			RegisterLink = 1,       //[int[4]: remote shardID][int[4]: local shardID]
			RegisterReceiver,	//[Guid: me], may be called multiple times, to receive messages to different identities
			UnregisterReceiver, //[Guid: me], deauthenticate
			SendMessage,        //c2s: [Guid: me][Guid: toEntity][Guid: msgID][int: channel][uint: num bytes][bytes...], s2c: [Guid: fromEntity][Guid: toReceiver][int: generation][int: channel][uint: num bytes][bytes...]
			MessageDelivered,   //s2c: [Guid: msgID]
			MessageDeliveryFailed, //s2c: [Guid: msgID][uint: strLength][byte[strLength]: ascii string reason]
			ShardLookup,			//c2s: [int[4]: request shardID] -> ShardLookupResponse
			ShardLookupResponse,    //s2c: [int[4]: request shardID][int: hostLength][byte[]: host][ushort: port]
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
		private readonly Func<ShardID, Link> linkLookup;

		private HashSet<Guid> guids = new HashSet<Guid>();

		public InteractionLink(TcpClient client, Func<ShardID, Link> linkLookup)
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

		public static void SignalDelivery(ClientMessageID id)
		{
			InteractionLink link;
			if (guidMap.TryGetValue(id.From, out link))
				using (MemoryStream ms = new MemoryStream())
				{
					ms.Write(id.MessageID.ToByteArray(), 0, 16);
					link.Send(new OutPackage((uint)ChannelID.MessageDelivered, ms.ToArray()));
				}
		}

		private void SignalDeliveryFailure(Guid messageID, string reason)
		{
			using (MemoryStream ms = new MemoryStream())
			{
				ms.Write(messageID.ToByteArray(), 0, 16);
				ms.Write(BitConverter.GetBytes((uint)reason.Length), 0, 4);
				foreach (char c in reason)
					ms.WriteByte((byte)c);
				Send(new OutPackage((uint)ChannelID.MessageDeliveryFailed, ms.ToArray()));
			}
		}

		public static void SignalDeliveryFailure(ClientMessageID id, string reason)
		{
			InteractionLink link;
			if (guidMap.TryGetValue(id.From,out link))
				link.SignalDeliveryFailure(id.MessageID, reason);
		}

		private void Error(string msg)
		{
			Log.Error(endPoint + ": " + msg);
		}


		private void Error(Exception ex)
		{
			Log.Error(endPoint + ": " + ex);
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
				lock (registry)
					registry.Remove(this);
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
			catch (ArgumentNullException)
			{ }
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

			BinaryReader reader = new BinaryReader(netStream);
			try
			{
				var f = new BinaryFormatter();

				byte[] header = new byte[8];
				while (!closed)
				{
					reader.RemainingBytes = 8;
					uint channel = reader.NextUInt();
					reader.RemainingBytes = reader.NextInt();


					try
					{
						switch (channel)
						{
							case (uint)ChannelID.RegisterLink:
								ShardID remoteID = reader.NextShardID();
								ShardID localID = reader.NextShardID();
								if (localID != Simulation.ID)
									throw new IntegrityViolation("Remote shard expected this shard to be " + localID + ", not " + Simulation.ID);
								var lnk = linkLookup?.Invoke(remoteID);
								if (lnk == null)
									throw new IntegrityViolation("Remote shard identifies as " + remoteID + ", but this is not a known neighbor of this shard " + Simulation.ID);
								lnk.SetPassiveClient(client);
								Abandon();
								break;
							case (uint)ChannelID.RegisterReceiver:
								Guid guid = reader.NextGuid();
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
							case (uint)ChannelID.ShardLookup:
								{
									ShardID id = reader.NextShardID();
									var addr = BaseDB.TryGetAddress(id);
									using (MemoryStream ms = new MemoryStream())
									{
										var str = Encoding.ASCII.GetBytes(addr.Host);
										ms.Write(id.AsBytes,0,16);
										ms.Write(BitConverter.GetBytes(str.Length), 0, 4);
										ms.Write(str,0,str.Length);
										ms.Write(BitConverter.GetBytes((ushort)addr.PeerPort),0,2);
										Send(new OutPackage((uint)ChannelID.ShardLookupResponse, ms.ToArray()));
									}
								}
								break;
							case (uint)ChannelID.UnregisterReceiver:
								{
									guid = reader.NextGuid();
									if (guids.Contains(guid))
									{
										Message("De-Authenticating as " + guid);
										guids.Remove(guid);
										guidMap.TryRemove(guid);
										OnUnregisterReceiver?.Invoke(guid);
									}
								}
								break;
							case (uint)ChannelID.SendMessage:
								{
									Guid from = reader.NextGuid();
									Guid to = reader.NextGuid();
									Guid id = reader.NextGuid();
									int msgChannel = reader.NextInt();
									byte[] data = reader.NextBytes();

									//int targetGen = Simulation.EstimateNextSuitableMessageTargetGeneration();
									if (!guids.Contains(from))
										Error("Not registered as " + from + ". Ignoring message");
									else
									{
										//int gen = Simulation.Stack.NewestFinishedSDSGeneration;
										ClientMessage msg = new ClientMessage(new ClientMessageID(from, to, id, msgChannel, orderIndex), data);

										var sender = new Address(this.endPoint);
										Simulation.Consensus?.Dispatch(msg, sender);
										OnMessage?.Invoke(msg, sender);
									}
								}
								break;

						}
					}
					catch (SerializationException ex)
					{
						Error(ex);
					}
					reader.SkipRemaining();

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

		public static InteractionLink Establish(TcpClient client, Func<ShardID, Link> linkLookup)
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

		public static void OnMessageCommit(Address addr, ClientMessageID id)
		{
			lock(registry)
				foreach (var r in registry)
					if (new Address(r.endPoint) == addr)
					{
						r.Confirm(id);
					}
		}

		private void Confirm(ClientMessageID id)
		{
			if (closed)
				return;
			Send(new OutPackage((uint)ChannelID.MessageDelivered, id.MessageID.ToByteArray()));
		}

		public bool Connected
		{
			get
			{
				return !closed && client.Connected;
			}
		}

		public Action<ClientMessage, Address> OnMessage { get; set; }
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
			var serial = new Newtonsoft.Json.JsonSerializer();
			StringBuilder str = new StringBuilder();
			TextWriter w = new StringWriter(str);
			foreach (var e in sds.FinalEntities)
			{
				serial.Serialize(w, e);
			}



			using (MemoryStream ms = new MemoryStream())
			using (var stream = new LZ4.LZ4Stream(Helper.Serialize(sds), LZ4.LZ4StreamMode.Compress))
			{
				stream.CopyTo(ms);
				return ms.ToArray();
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