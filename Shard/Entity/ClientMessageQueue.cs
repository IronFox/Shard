using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shard
{
	[Serializable]
	public struct ClientMessageID
	{
		/// <summary>
		/// GUID of the sender client
		/// </summary>
		public readonly Guid From;
		/// <summary>
		/// GUID of the receiving entity (or Guid.Empty if broadcast)
		/// </summary>
		public readonly Guid To;
		/// <summary>
		/// Index of this message. (From, OrderIndex) must be unique
		/// </summary>
		public readonly int OrderIndex;

		public bool IsBroadcast
		{
			get
			{
				return To == Guid.Empty;
			}
		}

		public ClientMessageID(Guid from, Guid to, int orderIndex)
		{
			From = from;
			To = to;
			OrderIndex = orderIndex;
		}

		public static bool operator ==(ClientMessageID a, ClientMessageID b)
		{
			return a.From == b.From && a.To == b.To && a.OrderIndex == b.OrderIndex;
		}
		public static bool operator !=(ClientMessageID a, ClientMessageID b)
		{
			return !(a == b);
		}

		public override bool Equals(object obj)
		{
			if (!(obj is ClientMessageID))
				return false;
			ClientMessageID other = (ClientMessageID)obj;
			return this == other;
		}

		public override int GetHashCode()
		{
			return new Helper.HashCombiner(GetType())
				.Add(From)
				.Add(To)
				.Add(OrderIndex)
				.GetHashCode();
		}

		public override string ToString()
		{
			return From + "->" + To + " #" + OrderIndex;
		}
	}

	[Serializable]
	public class ClientMessage
	{
		public ClientMessage(ClientMessageID id, int channel, byte[] data, int tlg, int intendedApplicationTLG)
		{
			Data = data;
			Channel = channel;
			ID = id;
			RecordedTLG = tlg;
			IntendedApplicationTLG = intendedApplicationTLG;
		}

		public ClientMessageID ID { get; set; }
		public int Channel { get; set; }
		public byte[] Data { get; set; }
		public int RecordedTLG { get; set; }
		public int IntendedApplicationTLG { get; set; }

		public override bool Equals(object obj)
		{
			var other = obj as ClientMessage;
			if (other == null)
				return false;
			return other.ID == ID
				&& Channel == other.Channel
				&& IntendedApplicationTLG == other.IntendedApplicationTLG
				&& Helper.AreEqual(Data, other.Data)
				;//ignore RecordedTLG here, as timing differences might cause variances
		}

		public override int GetHashCode()
		{
			return new Helper.HashCombiner(GetType())
				.Add(ID)
				.Add(Channel)
				.Add(Data)
				.GetHashCode();
		}

		public override string ToString()
		{
			return ID + " Ch" + Channel + " [" + Helper.Length(Data) + "]";
		}

		public OrderedEntityMessage Translate()
		{
			return new OrderedEntityMessage(
				ID.OrderIndex, 
				new EntityMessage(
					new Actor(ID.From), 
					ID.IsBroadcast, 
					Channel, 
					Data
				)
			);
		}
	}




	/// <summary>
	/// Inbound message queue for messages from clients to entities.
	/// SDS Processing new entities may collect queued messages for entity evaluation.
	/// Querying must specify the targeted generation.
	/// If the generation is less than the last queried generations, null is returned.
	/// Otherwise new messages are archived, and the full archive returned.
	/// If the new generation is higher than the recorded one, the archive is first cleared (only new messages are returned)
	/// </summary>
	public class ClientMessageQueue
	{
		Dictionary<Guid, List<OrderedEntityMessage>> 
					newMessages = new Dictionary<Guid, List<OrderedEntityMessage>>(),
					archivedMessages = new Dictionary<Guid, List<OrderedEntityMessage>>();
		int archiveGeneration = 0;

		public int ArchivedGeneration { get { return archiveGeneration; } }

		private static EntityMessage[] Sort(List<OrderedEntityMessage> list)
		{
			list.Sort();
			EntityMessage[] rs = new EntityMessage[list.Count];
			for (int i = 0; i < list.Count; i++)
				rs[i] = list[i].Message;
			return rs;
		}

		private ConcurrentDictionary<ClientMessageID, ReaffirmationCounter> reaffirmations
			= new ConcurrentDictionary<ClientMessageID, ReaffirmationCounter>();

		/// <summary>
		/// Fetches (and clears out) any queued up messages.
		/// Thread-safe against HandleIncomingMessage. NOT thread-safe against competing calls to Collect()
		/// </summary>
		/// <param name="generation">Generation to query. 
		/// If less than previous generations, nothing happens and null is returned. 
		/// If equal, new messages are archived, and full archive is returned.
		/// If greater, archived messages are purged and only new messages are returned/archived </param>
		/// <returns>Dictionary of queued messages, grouped by target entity id, ordered by sender and order number. May be null</returns>
		public Dictionary<Guid, EntityMessage[]> Collect(int generation, out bool isInconsistent)
		{
			isInconsistent = false;
			if (generation < archiveGeneration)
				return null;
			if (generation > archiveGeneration)
			{
				Log("Generation transition: ->"+generation);
				archiveGeneration = generation;
				archivedMessages.Clear();
			}

			lock (newMessages)
			{
				var copy = reaffirmations.Values.ToArray();
				foreach (var r in copy)
				{
					var msg = r.fullMessage;
					if (msg.IntendedApplicationTLG > generation)
						continue;
					if (msg.IntendedApplicationTLG < generation)
					{
						LogError("TLG window missed: discarding message " + msg);
						reaffirmations.ForceRemove(r.fullMessage.ID);
						isInconsistent = true;
						continue;
					}
					if (r.reaffirmationsReceived < r.reaffirmationsRequired)
					{
						isInconsistent = true;	//some but not all received => inconsistent, but maybe it's collected next time
						continue;
					}
					if (r.reaffirmationsRequired > 0)
					{
						//good case
						newMessages.GetOrCreate(msg.ID.To).Add(msg.Translate());
					}
					else
					{
						/*
						 * semi good case:
						 * we know it's consistent (at least here) because the neighbor will suffer the same issue
						 */
						LogError("Message conflict: discarding message " + msg);
					}
					reaffirmations.ForceRemove(r.fullMessage.ID);
				}
			}



			if (newMessages.Count > 0)
				lock (newMessages)
				{
					Log("Processing new messages");
					foreach (var pair in newMessages)
						archivedMessages.GetOrCreate(pair.Key).AddRange(pair.Value);
					newMessages.Clear();
				}

			if (archivedMessages.Count == 0)
				return null;

			var rs = new Dictionary<Guid, EntityMessage[]>();
			foreach (var pair in archivedMessages)
			{
				rs[pair.Key] = Sort(pair.Value);
			}
			return rs;
		}

		private void Log(string msg)
		{
			Shard.Log.Minor("CMQ @" + archiveGeneration + " A="+archivedMessages.Count+" N="+newMessages.Count+": " + msg);
		}

		private void LogError(string msg)
		{
			Shard.Log.Error("CMQ @" + archiveGeneration + " A=" + archivedMessages.Count + " N=" + newMessages.Count + ": " + msg);
		}

		private class ReaffirmationCounter
		{
			public ClientMessage fullMessage;
			public int	reaffirmationsRequired,
						reaffirmationsReceived;
		}


		/// <summary>
		/// Registers an incoming client message for subsequent collection (via Collect()).
		/// Thread-safe
		/// </summary>
		public void HandleIncomingMessage(ClientMessage message, int requireReaffirmations)
		{
			if (requireReaffirmations > 0)
			{
				var cnt = reaffirmations.AddOrUpdate(message.ID,
					id => new ReaffirmationCounter() { fullMessage = message, reaffirmationsRequired = requireReaffirmations },
					(id, value) => { Interlocked.CompareExchange(ref value.reaffirmationsRequired, requireReaffirmations,0); return value; }
				 );
				if (!cnt.fullMessage.Equals(message))	//id matches, but data does not
					cnt.reaffirmationsRequired = -1;	//invalidate
				return;
			}

			lock (newMessages)
			{
				newMessages.GetOrCreate(message.ID.To).Add(message.Translate());
			}
		}

		public void Reaffirm(ClientMessage message)
		{
			var cnt = reaffirmations.AddOrUpdate(message.ID,
				id => new ReaffirmationCounter() { fullMessage = message, reaffirmationsReceived = 1 },
				(id, value) => { Interlocked.Increment(ref value.reaffirmationsReceived); return value; }
			 );
			if (!cnt.fullMessage.Equals(message))   //id matches, but data does not
				cnt.reaffirmationsRequired = -1;    //invalidate
		}
	}
}
