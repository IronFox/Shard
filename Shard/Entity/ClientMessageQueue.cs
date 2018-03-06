using System;
using System.Collections;
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
		class Gen<T> where T : new()
		{
			public readonly int Generation;

			public Gen(int gen)
			{
				Generation = gen;
			}

			public T data = new T();
			///// <summary>
			///// Ordered by receiver entity
			///// </summary>
			//public Dictionary<Guid, List<ClientMessage>> newConfirmedMessages 
			//	= new Dictionary<Guid, List<ClientMessage>>();
			//public ConcurrentDictionary<ClientMessageID, ConsistentClientMessageContainer> pending =
			//	new ConcurrentDictionary<ClientMessageID, ConsistentClientMessageContainer>();

		};
		ConcurrentQueue<ClientMessage> newMessages = new ConcurrentQueue<ClientMessage>();


		class GenQueue<T> : IEnumerable<Gen<T>>, ICollection<Gen<T>>   where T : new()
		{
			private Deque<Gen<T>> generations = new Deque<Gen<T>>();

			public int Count => generations.Count;

			public bool IsReadOnly => false;

			public Gen<T> this[int index] { get => generations[index]; }

			public IEnumerator<Gen<T>> GetEnumerator()
			{
				return generations.GetEnumerator();
			}

			public Gen<T> GetOrCreate(int generation)
			{
				lock (this)
				{
					Gen<T> gen;
					if (generations.Count == 0)
					{
						gen = new Gen<T>(generation);
						generations.Add(gen);
					}
					else
					{
						if (generation < generations.Front.Generation)
							return null;
						while (generations.Back.Generation < generation)
							generations.Add(new Gen<T>(generations.Back.Generation + 1));
						gen = generations[generation - generations.Front.Generation];
					}
					return gen;
				}
			}

			public Gen<T> Get(int generation)
			{
				if (generation < generations.Front.Generation)
					return null;
				int at = generation - generations.Front.Generation;
				if (at >= generations.Count)
					return null;
				return generations[at];
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return generations.GetEnumerator();
			}

			public void Trim(int minGeneration, Action<T> dropped)
			{
				while (generations.Count > 0 && generations.Front.Generation < minGeneration)
				{
					dropped(generations.Front.data);
					generations.RemoveFront();
				}
			}

			public void Add(Gen<T> item)
			{
				generations.Add(item);
			}

			public void Clear()
			{
				generations.Clear();
			}

			public bool Contains(Gen<T> item)
			{
				return generations.Contains(item);
			}

			public void CopyTo(Gen<T>[] array, int arrayIndex)
			{
				generations.CopyTo(array, arrayIndex);
			}

			public bool Remove(Gen<T> item)
			{
				return generations.Remove(item);
			}
		}



		GenQueue<Dictionary<Guid, List<ClientMessage>>> newConfirmedMessages
			= new GenQueue<Dictionary<Guid, List<ClientMessage>>>();
		GenQueue<ConcurrentDictionary<ClientMessageID, ConsistentClientMessageContainer>> pending
			= new GenQueue<ConcurrentDictionary<ClientMessageID, ConsistentClientMessageContainer>>();
		

		private static EntityMessage[] Sort(List<OrderedEntityMessage> list)
		{
			list.Sort();
			EntityMessage[] rs = new EntityMessage[list.Count];
			for (int i = 0; i < list.Count; i++)
				rs[i] = list[i].Message;
			return rs;
		}

		public void Trim(int oldestUnconfirmedTLG, int oldestConfirmedTLG)
		{
			newConfirmedMessages.Trim(oldestConfirmedTLG, list =>
			{
				//if we ever get here it means we somehow received a newer consistent SDS,
				// which must have delivered the message,
				// so there is no need to do anything here
			});
			pending.Trim(oldestUnconfirmedTLG, msgs =>
			{
				foreach (var t in msgs)
					InteractionLink.SignalDeliveryFailure(t.Key, "Delivery window passed");
			});
		}

		/// <summary>
		/// Fetches (and clears out) any queued up messages.
		/// Thread-safe
		/// </summary>
		public void Collect(ref Dictionary<Guid, ClientMessage[]> inOutMessages, out bool isInconsistent, int generation, int replicaCount)
		{
			isInconsistent = false;
			if (newConfirmedMessages.Count == 0 && pending.Count == 0 && newMessages.IsEmpty)
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
				{
					InteractionLink.SignalDelivery(msg.ID);
					temp.GetOrCreate(msg.ID.To).Add(msg);
				}
			}

			if (pending.Count > 0)
				lock (pending)
				{
					var gen = pending.Get(generation);
					if (!gen.data.IsEmpty)
						isInconsistent = true;
				}

			if (newConfirmedMessages.Count > 0)
				lock (newConfirmedMessages)
				{
					var gen = newConfirmedMessages.Get(generation);
					foreach (var t in gen.data)
						temp.GetOrCreate(t.Key).AddRange(t.Value);
					gen.data.Clear();
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



		


		/// <summary>
		/// Registers an incoming client message for subsequent collection (via Collect()).
		/// Thread-safe
		/// </summary>
		public void HandleIncomingMessage(ClientMessage message, int requireReaffirmations)
		{
			if (requireReaffirmations > 1 || requireReaffirmations < 0)
			{
				lock (pending)
				{
					Gen< ConcurrentDictionary < ClientMessageID, ConsistentClientMessageContainer >> gen = null;
					foreach (var g in pending)
						if (g.data.ContainsKey(message.ID))
						{
							gen = g;
							break;
						}
					if (gen == null)
						gen = pending.GetOrCreate(message.Body.TargetTLG);
					if (gen == null)
						return;

					var ctr = gen.data.AddOrUpdate(message.ID, id => new ConsistentClientMessageContainer(message, requireReaffirmations), (id, ctr1)
					=>
					{
						ctr1.Add(message.Body, requireReaffirmations);
						return ctr1;
					});


					if (ctr.TargetTLG != gen.Generation)
					{
						gen.data.ForceRemove(message.ID);
						gen = pending.GetOrCreate(ctr.TargetTLG);
						if (gen == null)
							return;
						gen.data.ForceAdd(message.ID, ctr);
					}


					if (ctr.IsConfirmed)
					{
						if (!ctr.IsConflicting)
							lock(newConfirmedMessages)
								newConfirmedMessages.GetOrCreate(ctr.TargetTLG).data.GetOrCreate(message.ID.To).Add(message);
						gen.data.ForceRemove(message.ID);
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
