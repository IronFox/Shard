using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	public class EntityPool : IHashable, IEnumerable<Entity>
	{
		private class Container
		{
			public class BySender
			{
				ConcurrentDictionary<int, EntityMessage> inMessages = new ConcurrentDictionary<int, EntityMessage>();


				public bool Add(OrderedEntityMessage msg)
				{
					return inMessages.TryAdd(msg.OrderID, msg.Message);
				}

				public void AddTo(List<EntityMessage> messages)
				{
					var ar = inMessages.ToArray();
					Array.Sort(ar, (a, b) => a.Key.CompareTo(b.Key));
					foreach (var p in ar)
						messages.Add(p.Value);
				}

			}

			public Container(Entity e)
			{
				entity = e;
			}
			public Entity entity;
			public ConcurrentDictionary<Guid, BySender> messages = new ConcurrentDictionary<Guid, BySender>();
			public ConcurrentDictionary<Guid, EntityContact> contacts = new ConcurrentDictionary<Guid, EntityContact>();
		}

		private ConcurrentDictionary<EntityID, Container> fullMap = new ConcurrentDictionary<EntityID, Container>();
		private ConcurrentDictionary<Guid, Container> guidMap = new ConcurrentDictionary<Guid, Container>();

		public EntityPool(IEnumerable<Entity> entities)
		{
			if (entities != null)
				foreach (var e in entities)
					if (!Insert(e))
						Log.Message("Failed to insert entity " + e);
		}
		public EntityPool()
		{ }

		public int Count
		{
			get
			{
				return guidMap.Count;
			}
		}

		public bool Contains(EntityID id)
		{
			return fullMap.ContainsKey(id);
		}
		public bool Contains(Guid id)
		{
			return guidMap.ContainsKey(id);
		}

		public bool Find(EntityID id, out Entity e)
		{
			Container rs;
			if (fullMap.TryGetValue(id, out rs))
			{
				e = rs.entity;
				return true;
			}
			e = new Entity();
			return false;
		}
		public bool Find(Guid id, out Entity e)
		{
			Container rs;
			if (guidMap.TryGetValue(id, out rs))
			{
				e = rs.entity;
				return true;
			}
			e = new Entity();
			return false;
		}
		public void AddContact(EntityContact c)
		{
			foreach (var p in fullMap)
			{
				if (p.Key.Guid != c.ID.Guid && Simulation.GetDistance(c.ID.Position, p.Key.Position) <= Simulation.SensorRange)
				{
					if (!p.Value.contacts.TryAdd(c.ID.Guid, c))
					{
						EntityContact existing;
						while (true)
						{
							if (!p.Value.contacts.TryGetValue(c.ID.Guid, out existing))
								throw new IntegrityViolation("Should have been able to query existing contact " + c);
							int comp = c.CompareTo(existing);
							if (comp >= 0)
								break;
							if (p.Value.contacts.TryUpdate(c.ID.Guid, c, existing))
								break;
						}
					}
				}
			}
		}


		public bool RelayMessage(Vec3 senderPosition, Guid receiver, OrderedEntityMessage message)
		{
			Container rs;
			if (!guidMap.TryGetValue(receiver, out rs))
				return false;
			if (!Simulation.CheckDistance("Message", senderPosition, rs.entity, Simulation.R))
				return false;
			return rs.messages.GetOrAdd(message.Message.Sender.Guid, guid => new Container.BySender()).Add(message);
		}

		public void BroadcastMessage(Vec3 senderPosition, OrderedEntityMessage message)
		{
			foreach (var p in fullMap)
			{
				if (Simulation.GetDistance(senderPosition, p.Key.Position) <= Simulation.R)
					p.Value.messages.GetOrAdd(message.Message.Sender.Guid, guid => new Container.BySender()).Add(message);
			}
		}

		/// <summary>
		/// Evolves all local entities (in parallel), and stores changes in the specified change set
		/// </summary>
		/// <param name="set"></param>
		public int Evolve(EntityChangeSet set, InconsistencyCoverage ic, int roundNumber, int timeoutMS=1000)
		{
			return set.Evolve(EnumerateEntities(), ic, roundNumber, timeoutMS);
		}

		public IEnumerable<Entity> EnumerateEntities()
		{
			foreach (var ctr in fullMap.Values)
				yield return ctr.entity;
		}

		public Entity[] ToArray()
		{
			var tmp = Helper.ToArray(fullMap.Values);
			Entity[] rs = new Entity[tmp.Length];
			for (int i = 0; i < tmp.Length; i++)
				rs[i] = tmp[i].entity;
			return rs;
		}

		/// <summary>
		/// Sends all queued messages and contact events in parallel to the respective entities.
		/// All entities are recreated (these attributes are readonly)
		/// </summary>
			public void DispatchAll()
		{
			Parallel.ForEach(fullMap.Values, ctr =>
			{
				List<EntityMessage> messages = new List<EntityMessage>();
				var ar = ctr.messages.ToArray();
				Array.Sort(ar, (a, b) => a.Key.CompareTo(b.Key));
				foreach (var p in ar)
					p.Value.AddTo(messages);

				EntityContact[] ctx = Helper.ToArray(ctr.contacts.Values);
				Array.Sort(ctx, (a, b) => a.ID.CompareTo(b.ID));

				ctr.entity = ctr.entity.SetIncoming(messages.ToArray(), ctx);

				ctr.messages.Clear();
				ctr.contacts.Clear();
			}
			);
		}


		private bool FindAndRemove(Container ctr)
		{
			Container other;
			if (!fullMap.TryRemove(ctr.entity.ID, out other))
				return false;
			if (other != ctr)
			{
				fullMap.ForceAdd(ctr.entity.ID, other);
				throw new IntegrityViolation("Removed container does not equal provided container");
			}
			guidMap.ForceRemove(ctr.entity.ID.Guid);
			return true;
		}
		public bool FindAndRemove(EntityID id)
		{
			Container ctr;
			if (!fullMap.TryRemove(id, out ctr))
				return false;
			guidMap.ForceRemove(id.Guid);
			return true;
		}

		public bool FindAndRemove(EntityID id, Func<Entity, bool> verifyMatch)
		{
			Container ctr;
			if (!fullMap.TryRemove(id, out ctr))
				return false;
			if (!verifyMatch(ctr.entity))
			{
				fullMap.ForceAdd(id, ctr);
				return false;
			}
			guidMap.ForceRemove(id.Guid);
			return true;
		}

		public bool Insert(Entity entity)
		{
			Container ctr = new Container(entity);
			if (!guidMap.TryAdd(entity.ID.Guid, ctr))
				return false;
			fullMap.ForceAdd(entity.ID, ctr);
			return true;
		}

		public bool UpdateEntity(Entity original, Entity updated)
		{
			if (original.ID.Guid != updated.ID.Guid)
				throw new InvalidOperationException("Trying to replace entity with different ID: " + original.ID + " -> " + updated.ID);
			Container ctr;
			if (!fullMap.TryRemove(original.ID, out ctr))
				return false;
			ctr.entity = updated;
			fullMap.ForceAdd(updated.ID, ctr);
			return true;
		}

		public void VerifyIntegrity()
		{
			if (fullMap.Count != guidMap.Count)
				throw new IntegrityViolation("fullMap.Count (" + fullMap.Count + ") != guidMap.Count (" + guidMap.Count + ")");

			foreach (var p in fullMap)
				if (!guidMap.ContainsKey(p.Key.Guid))
					throw new IntegrityViolation(p.Key + " is not contained in guidMap");
		}

		public EntityPool Clone()
		{
			EntityPool rs = new EntityPool();
			foreach (var e in this)
				rs.Insert(e);
			return rs;
		}

		public void Hash(Hasher h)
		{
			var source = guidMap.Values.ToArray();
			Entity[] ar = new Entity[source.Length];
			for (int i = 0; i < ar.Length; i++)
				ar[i] = source[i].entity;

			Array.Sort(ar);
			foreach (var e in ar)
				h.Add(e);
		}

		private class ConverterEnumerator : IEnumerator<Entity>
		{
			IEnumerator<Container> other;

			public ConverterEnumerator(IEnumerator<Container> o)
			{
				other = o;
			}

			public Entity Current => other.Current.entity;

			object IEnumerator.Current => other.Current.entity;

			public void Dispose()
			{
				other.Dispose();
			}

			public bool MoveNext()
			{
				return other.MoveNext();
			}

			public void Reset()
			{
				other.Reset();
			}
		}

		public IEnumerator<Entity> GetEnumerator()
		{
			return new ConverterEnumerator(guidMap.Values.GetEnumerator());
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return new ConverterEnumerator(guidMap.Values.GetEnumerator());
		}
	}

	[Serializable]
	internal class ExecutionException : Exception
	{
		public ExecutionException()
		{
		}

		public ExecutionException(string message) : base(message)
		{
		}

		public ExecutionException(string message, Exception innerException) : base(message, innerException)
		{
		}

		protected ExecutionException(SerializationInfo info, StreamingContext context) : base(info, context)
		{
		}
	}
}
