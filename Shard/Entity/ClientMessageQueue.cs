using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
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

		public static Dictionary<Guid, EntityMessage[]> Collect()
		{
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

		public static void FetchClientMessage(Guid fromClient, Guid toEntity, byte[] data, int orderIndex)
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
