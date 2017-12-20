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


		/// <summary>
		/// Fetches (and clears out) any queued up messages.
		/// Thread-safe against HandleIncomingMessage. NOT thread-safe against competing calls to Collect()
		/// </summary>
		/// <param name="generation">Generation to query. 
		/// If less than previous generations, nothing happens and null is returned. 
		/// If equal, new messages are archived, and full archive is returned.
		/// If greater, archived messages are purged and only new messages are returned/archived </param>
		/// <returns>Dictionary of queued messages, grouped by target entity id, ordered by sender and order number. May be null</returns>
		public Dictionary<Guid, EntityMessage[]> Collect(int generation)
		{
			if (generation < archiveGeneration)
				return null;
			if (generation > archiveGeneration)
			{
				Log("Generation transition: ->"+generation);
				archiveGeneration = generation;
				archivedMessages.Clear();
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
			Shard.Log.Message("CMQ @" + archiveGeneration + " A="+archivedMessages.Count+" N="+newMessages.Count+": " + msg);
		}

		/// <summary>
		/// Registers an incoming client message for subsequent collection (via Collect()).
		/// Thread-safe
		/// </summary>
		/// <param name="fromClient">GUID of the sender client</param>
		/// <param name="toEntity">GUID of the targeted entity. Set to Guid.Empty to broadcast</param>
		/// <param name="data">Data to send. May be null</param>
		/// <param name="orderIndex">Sender message order index. The same sender must not reuse the same order index</param>
		public void HandleIncomingMessage(Guid fromClient, Guid toEntity, int channel, byte[] data, int orderIndex)
		{
			lock (newMessages)
			{
				if (!newMessages.ContainsKey(toEntity))
					newMessages[toEntity] = new List<OrderedEntityMessage>();
				newMessages[toEntity].Add(new OrderedEntityMessage(orderIndex, new EntityMessage(new Actor(fromClient, false), toEntity == Guid.Empty, channel,data)));
			}
		}

	}
}
