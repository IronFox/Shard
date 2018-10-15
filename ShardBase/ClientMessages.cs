using Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	[Serializable]
	public struct ClientMessageID : IComparable<ClientMessageID>
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
		/// Unique ID of this message. Secondary sorting key
		/// </summary>
		public readonly Guid MessageID;
		/// <summary>
		/// Linear order index of this message. Primary sorting key
		/// </summary>
		public readonly int OrderIndex;

		/// <summary>
		/// Message channel
		/// </summary>
		public int Channel { get; set; }

		/// <summary>
		/// Checks whether the local message represents a broadcast
		/// </summary>
		public bool IsBroadcast
		{
			get
			{
				return To == Guid.Empty;
			}
		}

		public ClientMessageID(Guid from, Guid to, Guid messageID, int channel, int orderIndex)
		{
			Channel = channel;
			From = from;
			To = to;
			MessageID = messageID;
			OrderIndex = orderIndex;
		}

		public static bool operator ==(ClientMessageID a, ClientMessageID b)
		{
			return a.From == b.From
				&& a.To == b.To
				&& a.MessageID == b.MessageID
				&& a.Channel == b.Channel
				&& a.OrderIndex == b.OrderIndex
				;
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
				.Add(MessageID)
				.Add(OrderIndex)
				.Add(Channel)
				.GetHashCode();
		}

		public override string ToString()
		{
			return From + "->" + To + " #" + MessageID + " Ch" + Channel;
		}

		public int CompareTo(ClientMessageID other)
		{
			return new Helper.Comparator()
				.Append(OrderIndex, other.OrderIndex)
				.Append(MessageID, other.MessageID)
				.Append(From, other.From)
				.Append(To, other.To)
				.Append(Channel,other.Channel)
				.Finish();
		}
	}
	
	[Serializable]
	public class ClientMessage : IComparable<ClientMessage>
	{
		public readonly ClientMessageID ID;
		public readonly byte[] Body;


		public ClientMessage(ClientMessageID id, byte[] body)
		{
			ID = id;
			Body = body;
		}

		public int CompareTo(ClientMessage other)
		{
			return new Helper.Comparator()
				.Append(ID, other.ID)
				.Append(Body, other.Body)
				.Finish();
		}

		public override bool Equals(object obj)
		{
			var other = obj as ClientMessage;
			if (other == null)
				return false;
			return ID == other.ID && Body == other.Body;
		}
		public override int GetHashCode()
		{
			return new Helper.HashCombiner(GetType())
				.Add(ID)
				.Add(Body)
				.GetHashCode();
		}

		public EntityMessage ToEntityMessage()
		{
			return new EntityMessage(new Actor(ID.From), ID.IsBroadcast, ID.Channel, Body);
		}
	};

	public struct MessagePack
	{
		public readonly Dictionary<Guid, ClientMessage[]> Messages;
		public readonly bool IsComplete;

		public MessagePack(Dictionary<Guid, ClientMessage[]> msg, bool complete)
		{
			Messages = msg;
			IsComplete = complete;
			if (complete && msg == null)
				throw new ArgumentNullException("msg");
		}
	}


}
