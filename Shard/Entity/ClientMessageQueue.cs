using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
		class Gen
		{
			public readonly int Generation;

			public Gen(int gen)
			{
				Generation = gen;
			}

			/// <summary>
			/// Ordered by receiver entity
			/// </summary>
			public Dictionary<Guid, List<ClientMessage>> newConfirmedMessages 
				= new Dictionary<Guid, List<ClientMessage>>();
			public ConcurrentDictionary<ClientMessageID, ConsistentClientMessageContainer> pending =
				new ConcurrentDictionary<ClientMessageID, ConsistentClientMessageContainer>();

		};
		ConcurrentQueue<ClientMessage> newMessages = new ConcurrentQueue<ClientMessage>();

		Deque<Gen> generations = new Deque<Gen>();

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
		/// Thread-safe
		/// </summary>
		public void Collect(ref Dictionary<Guid, ClientMessage[]> inOutMessages, out bool isInconsistent, int generation, int replicaCount)
		{
			isInconsistent = false;
			if (generations.Count == 0 && newMessages.IsEmpty)
				return;
			Dictionary<Guid, List<ClientMessage>> temp = new Dictionary<Guid, List<ClientMessage>>();
			if (inOutMessages == null)
				inOutMessages = new Dictionary<Guid, ClientMessage[]>();
			else
			{
				foreach (var t in inOutMessages)
					temp.GetOrCreate(t.Key).AddRange(t.Value);
				inOutMessages.Clear();
			}
			{
				ClientMessage msg;
				while (newMessages.TryDequeue(out msg))
					temp.GetOrCreate(msg.ID.To).Add(msg);
			}

			if (generations.Count > 0)
				lock (generations)
				{
					while (generations.Count > 0 && generations.Front.Generation + 2 < generation)
						generations.RemoveFront();
					if (generations.Count == 0)
						return;

					if (generation < generations.Front.Generation)
						return;
					int at = generation - generations.Front.Generation;
					if (at >= generations.Count)
						return;
					Gen gen = generations[at];

					if (!gen.pending.IsEmpty)
						isInconsistent = true;

					if (gen.newConfirmedMessages.Count == 0)
						return;

					foreach (var t in gen.newConfirmedMessages)
						temp.GetOrCreate(t.Key).AddRange(t.Value);
					gen.newConfirmedMessages.Clear();
				}

			foreach (var t in temp)
			{
				t.Value.Sort();
				inOutMessages[t.Key] = t.Value.ToArray();
			}
		}

		private void Log(string msg)
		{
			//Shard.Log.Minor("CMQ @" + archiveGeneration + " A="+archivedMessages.Count+" N="+newMessages.Count+": " + msg);
		}

		private void LogError(string msg)
		{
			Shard.Log.Error("CMQ: " + msg);
		}




		private Gen GetOrCreateGen(int generation)
		{
			lock (generations)
			{
				Gen gen;
				if (generations.Count == 0)
				{
					gen = new Gen(generation);
					generations.Add(gen);
				}
				else
				{
					if (generation < generations.Front.Generation)
						return null;
					while (generations.Back.Generation < generation)
						generations.Add(new Gen(generations.Back.Generation + 1));
					gen = generations[generation - generations.Front.Generation];
				}
				return gen;
			}
		}



		/// <summary>
		/// Registers an incoming client message for subsequent collection (via Collect()).
		/// Thread-safe
		/// </summary>
		public void HandleIncomingMessage(ClientMessage message, int requireReaffirmations)
		{
			if (requireReaffirmations > 1 || requireReaffirmations < 0)
			{
				lock (generations)
				{
					Gen gen = null;
					foreach (var g in generations)
						if (g.pending.ContainsKey(message.ID))
						{
							gen = g;
							break;
						}
					if (gen == null)
						gen = GetOrCreateGen(message.Body.TargetTLG);
					if (gen == null)
						return;

					var ctr = gen.pending.AddOrUpdate(message.ID, id => new ConsistentClientMessageContainer(message, requireReaffirmations), (id, ctr1)
					=>
					{
						ctr1.Add(message.Body, requireReaffirmations);
						return ctr1;
					});


					if (ctr.TargetTLG != gen.Generation)
					{
						gen.pending.ForceRemove(message.ID);
						gen = GetOrCreateGen(ctr.TargetTLG);
						if (gen == null)
							return;
						gen.pending.ForceAdd(message.ID, ctr);
					}


					if (ctr.IsConfirmed)
					{
						if (!ctr.IsConflicting)
							gen.newConfirmedMessages.GetOrCreate(message.ID.To).Add(message);
						gen.pending.ForceRemove(message.ID);
					}
				}
			}
			else
			{
				newMessages.Enqueue(message);
			}
		}

		public void Confirm(ClientMessage message)
		{
			HandleIncomingMessage(message, -1);
		}
	}
}
