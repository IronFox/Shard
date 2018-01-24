using Supercluster.KDTree;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	/// <summary>
	/// Entity set using lock-free concurrency containers.
	/// Serialization guarantees written entities are in deterministic order, regardless of their order of insertion.
	/// As a result, Sha256 digests are also order-independent.
	/// Other operations may be affected by order of insertion.
	/// </summary>
	[Serializable]
	public class EntityPool : IEnumerable<Entity>, ISerializable
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

		private Supercluster.KDTree.KDTree<float, Container> tree;
		private ConcurrentDictionary<EntityID, Container> fullMap = new ConcurrentDictionary<EntityID, Container>();
		private ConcurrentDictionary<Guid, Container> guidMap = new ConcurrentDictionary<Guid, Container>();

		private EntityChange.ExecutionContext ctx;

		private static float Sqr(float x)
		{
			return x * x;
		}

		private KDTree<float, Container> Tree
		{
			get
			{
				lock (this)
				{
					var rs = tree;
					if (rs == null)
					{
						var containers = fullMap.Values.ToArray();
						float[][] points = new float[containers.Length][];
						for (int i = 0; i < points.Length; i++)
							points[i] = containers[i].entity.ID.Position.ToArray();
						tree = rs = new KDTree<float, Container>(3, points, containers, (x, y) =>
						{
							return ctx.GetDistance(new Vec3(x), new Vec3(y));
						});
					}
					return rs;
				}
			}
		}

		public EntityPool(IEnumerable<Entity> entities, EntityChange.ExecutionContext ctx)
		{
			this.ctx = ctx;
			if (entities != null)
				foreach (var e in entities)
				{
					var c = InsertC(e);
					if (c == null)
						ctx.LogMessage("Failed to insert entity " + e);
				}
		}

		public EntityPool(EntityChange.ExecutionContext ctx)
		{
			this.ctx = ctx;
		}

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
			var contacts = tree.RadialSearch(c.ID.Position.ToArray(), ctx.Ranges.S);

			foreach (var p in contacts)
			{
				if (p.Item2.entity.ID.Guid != c.ID.Guid)// && Simulation.GetDistance(c.ID.Position, p.Key.Position) <= Simulation.SensorRange)
				{
					if (!p.Item2.contacts.TryAdd(c.ID.Guid, c))
					{
						EntityContact existing;
						while (true)
						{
							if (!p.Item2.contacts.TryGetValue(c.ID.Guid, out existing))
								throw new IntegrityViolation("Should have been able to query existing contact " + c);
							int comp = c.CompareTo(existing);
							if (comp >= 0)
								break;
							if (p.Item2.contacts.TryUpdate(c.ID.Guid, c, existing))
								break;
						}
					}
				}
			}
		}


		/// <summary>
		/// Checks if the specified receiver exists, and is within influence range of <paramref name="senderPosition"/>.
		/// If so, the message is relayed to the receiver, and the method returns true.
		/// </summary>
		/// <param name="senderPosition"></param>
		/// <param name="receiver"></param>
		/// <param name="message"></param>
		/// <returns>True if the entity was found, in range, and the message index was not yet used by the receiver-sender-tuple</returns>
		public bool RelayMessage(Vec3 senderPosition, Guid receiver, OrderedEntityMessage message)
		{
			Container rs;
			if (!guidMap.TryGetValue(receiver, out rs))
				return false;
			if (!ctx.CheckR("Message", senderPosition, rs.entity))
				return false;
			return rs.messages.GetOrAdd(message.Message.Sender.Guid, guid => new Container.BySender()).Add(message);
		}

		public void RequireTree()
		{
			var t = Tree;
		}

		public int BroadcastMessage(Vec3 senderPosition, OrderedEntityMessage message)
		{
			int counter = 0;
			
			foreach (var p in tree.RadialSearch(senderPosition.ToArray(), ctx.Ranges.R))
			{
				if (p.Item2.entity.ID.Guid != message.Message.Sender.Guid)// && Simulation.GetDistance(senderPosition, p.Key.Position) <= Simulation.R)
				{
					p.Item2.messages.GetOrAdd(message.Message.Sender.Guid, guid => new Container.BySender()).Add(message);
					counter++;
				}
			}
			return counter;
		}

		/// <summary>
		/// Evolves all local entities (in parallel), and stores changes in the specified change set
		/// </summary>
		/// <param name="set"></param>
		public List<EntityError> TestEvolve(EntityChangeSet set, InconsistencyCoverage ic, int roundNumber, TimeSpan budget)
		{
			if (roundNumber != ctx.GenerationNumber)
				throw new IntegrityViolation("Expected generation number "+roundNumber+" in execution context, but found "+ctx.GenerationNumber);
			return set.Evolve(EnumerateEntities().ToList(), null,ic,budget,ctx);
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
			return InsertC(entity) != null;
		}

		private Container InsertC(Entity entity)
		{
			Container ctr = new Container(entity);
			if (!guidMap.TryAdd(entity.ID.Guid, ctr))
				return null;
			fullMap.ForceAdd(entity.ID, ctr);
			tree = null;
			return ctr;
		}

		public bool UpdateEntity(Entity original, Entity updated)
		{
			if (original.ID.Guid != updated.ID.Guid)
				throw new InvalidOperationException("Trying to replace entity with different ID: " + original.ID + " -> " + updated.ID);
			Container ctr;
			if (!fullMap.TryRemove(original.ID, out ctr))
				return false;
			if (ctr.entity.ID.Position != updated.ID.Position)
				tree = null;
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
			EntityPool rs = new EntityPool(ctx);
			foreach (var e in this)
				rs.Insert(e.Clone());
			return rs;
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

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			var source = guidMap.Values.ToArray();
			Entity[] ar = new Entity[source.Length];
			for (int i = 0; i < ar.Length; i++)
				ar[i] = source[i].entity;

			Array.Sort(ar);
			info.AddValue("ar", ar);
		}

		public SDS.Digest HashDigest	//for testing purposes
		{
			get
			{
				using (var ms = Helper.Serialize(this))
				{
					return new SDS.Digest(SHA256.Create().ComputeHash(ms));
				}

			}
		}
	}

	public class ExecutionException : Exception
	{
		public readonly EntityID EntityID;
		public ExecutionException(EntityID eID, string message, Exception nested=null):base(message,nested)
		{
			EntityID = eID;
		}


		public override string Message => EntityID+": "+ base.Message;

	}
}
