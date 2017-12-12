using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	/// <summary>
	/// Inbound message queue for messages from clients to entities.
	/// SDS Processing new entities may collect queued messages for entity evaluation
	/// </summary>
	public static class ClientMessageQueue
	{
		static Dictionary<Guid, List<OrderedEntityMessage>> queuedMessages = new Dictionary<Guid, List<OrderedEntityMessage>>();

		private static EntityMessage[] Sort(List<OrderedEntityMessage> list)
		{
			list.Sort();
			EntityMessage[] rs = new EntityMessage[list.Count];
			for (int i = 0; i < list.Count; i++)
				rs[i] = list[i].Message;
			return rs;
		}

		/// <summary>
		/// Fetches (and clears out) any queued up messages
		/// </summary>
		/// <returns>Dictionary of queued messages, grouped by target entity id, ordered by sender and order number. May be null</returns>
		public static Dictionary<Guid, EntityMessage[]> Collect()
		{
			if (queuedMessages.Count == 0)
				return null;
			lock (queuedMessages)
			{
				var rs = new Dictionary<Guid, EntityMessage[]>();
				foreach (var pair in queuedMessages)
				{
					rs[pair.Key] = Sort(pair.Value);
				}
				queuedMessages.Clear();
				return rs;
			}
		}

		/// <summary>
		/// Registers an incoming client message for subsequent collection (via Collect())
		/// </summary>
		/// <param name="fromClient">GUID of the sender client</param>
		/// <param name="toEntity">GUID of the targeted entity. Set to Guid.Empty to broadcast</param>
		/// <param name="data">Data to send. May be null</param>
		/// <param name="orderIndex">Sender message order index. The same sender must not reuse the same order index</param>
		public static void HandleIncomingMessage(Guid fromClient, Guid toEntity, byte[] data, int orderIndex)
		{
			lock (queuedMessages)
			{
				if (!queuedMessages.ContainsKey(toEntity))
					queuedMessages[toEntity] = new List<OrderedEntityMessage>();
				queuedMessages[toEntity].Add(new OrderedEntityMessage(orderIndex, new EntityMessage(new Actor(fromClient, false), toEntity == Guid.Empty, data)));
			}
		}

	}
}
