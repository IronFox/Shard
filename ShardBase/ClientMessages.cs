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
	public class ClientMessageBody : IComparable<ClientMessageBody>
	{
		public ClientMessageBody(byte[] data, int tlg, int targetTLG, int myReplicaIndex, bool isHashedDigest)
		{
			Data = data;
			RecordedTLG = tlg;
			RecordedByReplicaIndex = myReplicaIndex;
			IsHashedDigest = isHashedDigest;
			TargetTLG = targetTLG;
		}

		public ClientMessageBody ToDigestedMessage()
		{
			if (IsHashedDigest)
				return this;
			var digest = Digest.FromBinaryData(Data);
			return new ClientMessageBody(digest.Bytes, RecordedTLG, TargetTLG, RecordedByReplicaIndex, digest.IsHashed);
		}

		public Digest GetDigestedData()
		{
			if (IsHashedDigest)
				return new Digest(Data,true);
			return Digest.FromBinaryData(Data);
		}

		public readonly byte[] Data;
		public readonly bool IsHashedDigest;

		public readonly int RecordedTLG;
		public readonly int RecordedByReplicaIndex;
		/// <summary>
		/// Generation at which the message should be processed.
		/// The farther in the future, the more likely it will be delivered.
		/// Among multiple confirmations, the highest one will ultimately be applied
		/// </summary>
		public readonly int TargetTLG;

		public static bool operator ==(ClientMessageBody a, ClientMessageBody b)
		{
			return a.IsHashedDigest == b.IsHashedDigest && Helper.AreEqual(a.Data, b.Data);
			;//ignore RecordedTLG here, as timing differences might cause variances
		}

		public static bool operator !=(ClientMessageBody a, ClientMessageBody b)
		{
			return !(a == b);
		}

		public override bool Equals(object obj)
		{
			var other = obj as ClientMessageBody;
			if (other == null)
				return false;
			return this == other;
		}

		public override int GetHashCode()
		{
			return new Helper.HashCombiner(GetType())
				.Add(IsHashedDigest)
				.Add(Data)
				.GetHashCode();
		}

		public override string ToString()
		{
			return IsHashedDigest ? "<digest>" : "[" + Helper.Length(Data) + "]";
		}

		public int CompareTo(ClientMessageBody other)
		{
			return new Helper.Comparator()
				.Append(IsHashedDigest, other.IsHashedDigest)
				.Append(Data, other.Data)
				.Finish();
		}
	}

	[Serializable]
	public class ClientMessage : IComparable<ClientMessage>
	{
		public readonly ClientMessageID ID;
		public readonly ClientMessageBody Body;


		public ClientMessage(ClientMessageID id, ClientMessageBody body)
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
			if (Body.IsHashedDigest)
				throw new IntegrityViolation("Bad message body");
			return new EntityMessage(new Actor(ID.From), ID.IsBroadcast, ID.Channel, Body.Data);
		}
	};

	/// <summary>
	/// Container for multi-sibling message handling
	/// </summary>
	public class ConsistentClientMessageContainer
	{
		public readonly ClientMessageID ID;
		private int firstRecorded = int.MaxValue;
		private int targetTLG = int.MinValue;
		private Dictionary<int, Digest> digests = new Dictionary<int, Digest>();
		public byte[] MessagePayload { get; private set; }
		private int confirmationsRequired = 0;
		public ConsistentClientMessageContainer(ClientMessage msg, int confirmationsRequired)
		{
			ID = msg.ID;
			Add(msg.Body, confirmationsRequired);
		}
		public void Add(ClientMessageBody body, int confirmationsRequired)
		{
			digests.Add(body.RecordedByReplicaIndex, body.GetDigestedData());
			firstRecorded = Math.Min(firstRecorded, body.RecordedTLG);
			targetTLG = Math.Max(targetTLG, body.TargetTLG);
			if (!body.IsHashedDigest)
				MessagePayload = body.Data; //don't care if we overwrite previous data. IsConflicting will return true then
			if (confirmationsRequired > 0)
				this.confirmationsRequired = confirmationsRequired;
		}

		public bool IsConfirmed
		{
			get
			{
				for (int i = 0; i < confirmationsRequired; i++)
					if (!digests.ContainsKey(i))
						return false;
				return true;
			}
		}


		public int TargetTLG
		{
			get
			{
				return targetTLG;
			}
		}
		public ClientMessage ToMessage()
		{
			return new ClientMessage(ID, new ClientMessageBody(MessagePayload, firstRecorded, targetTLG, 0, false));
		}

		public bool IsConflicting
		{
			get
			{
				Digest? compareWith = null;
				foreach (var t in digests)
					if (compareWith == null)
						compareWith = t.Value;
					else
						if (compareWith != t.Value)
							return false;
				return true;
			}
		}
	}

}
