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
				private struct MessageKey : IComparable<MessageKey>
				{
					public int orderID;
					public EntityID senderID;

					public MessageKey(OrderedEntityMessage msg) : this()
					{
						orderID = msg.OrderID;
						senderID = new EntityID(msg.Message.Sender.Guid,msg.Message.Sender.Position);
					}

					public int CompareTo(MessageKey other)
					{
						return new Helper.Comparator()
							.Append(orderID, other.orderID)
							.Append(senderID, other.senderID)
							.Finish();
					}

					public override bool Equals(object obj)
					{
						if (!(obj is MessageKey))
						{
							return false;
						}

						var key = (MessageKey)obj;
						return orderID == key.orderID && senderID == key.senderID;
					}

					public override int GetHashCode()
					{
						var hashCode = 93245203;
						hashCode = hashCode * -1521134295 + orderID.GetHashCode();
						hashCode = hashCode * -1521134295 + senderID.GetHashCode();
						return hashCode;
					}
				}

				ConcurrentDictionary<MessageKey, EntityMessage> inMessages = new ConcurrentDictionary<MessageKey, EntityMessage>();


				public bool Add(OrderedEntityMessage msg)
				{
					return inMessages.TryAdd(new MessageKey(msg), msg.Message);
				}

				public void AddTo(List<EntityMessage> messages, EntityRanges ranges, Vec3 entityLocation)
				{
					var ar = inMessages.ToArray();
					Array.Sort(ar, (a, b) => a.Key.CompareTo(b.Key));
					int lastID = -1;
					foreach (var p in ar)
					{
						if (p.Key.orderID != lastID)
							messages.Add(p.Value);
						lastID = p.Key.orderID;
					}
				}

			}

			public Container(Entity e)
			{
				entity = e;
			}
			public Entity entity;
			public ConcurrentDictionary<Guid, BySender> messages = new ConcurrentDictionary<Guid, BySender>();
			internal ConcurrentBag<Tuple<EntityID, Entity>> deferredUpdates = new ConcurrentBag<Tuple<EntityID, Entity>>();
#if STATE_ADV
			public ConcurrentDictionary<Guid, EntityContact> contacts = new ConcurrentDictionary<Guid, EntityContact>();
#endif
		}

		private KDTree<float, Container> tree;
		private ConcurrentDictionary<EntityID, Container> fullMap = new ConcurrentDictionary<EntityID, Container>();
		private ConcurrentDictionary<Guid, Container> guidMap = new ConcurrentDictionary<Guid, Container>();
		private ConcurrentDictionary<Guid, ConcurrentBag<Entity>> deferredInserts = new ConcurrentDictionary<Guid, ConcurrentBag<Entity>>();
		private ConcurrentBag<EntityID> deferredRemovals = new ConcurrentBag<EntityID>();

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



#if STATE_ADV
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
#endif

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

		public int BroadcastMessage(Vec3 senderPosition, float maxRange, OrderedEntityMessage message)
		{
			int counter = 0;

			foreach (var p in tree.RadialSearch(senderPosition.ToArray(), Math.Min(maxRange, ctx.Ranges.R)))
			{
				//if (Vec3.GetChebyshevDistance(p.Item2.entity.ID.Position, senderPosition) > ctx.Ranges.R)
				//	throw new IntegrityViolation("Sender range exceeded during broadcast. Tree is out of date");
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
				throw new IntegrityViolation("Expected generation number " + roundNumber + " in execution context, but found " + ctx.GenerationNumber);
			return set.Evolve(EnumerateEntities().ToList(), null, ic, budget, ctx);
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
				{
					p.Value.AddTo(messages,ctx.Ranges,ctr.entity.ID.Position);
				}

#if STATE_ADV
				EntityContact[] ctx = Helper.ToArray(ctr.contacts.Values);
				Array.Sort(ctx, (a, b) => a.ID.CompareTo(b.ID));
#endif

				ctr.entity = ctr.entity.SetIncoming(messages.ToArray()
#if STATE_ADV
					, ctx
#endif
					);

				ctr.messages.Clear();
#if STATE_ADV
				ctr.contacts.Clear();
#endif
			}
			);
		}


		private bool CheckFindAndRemove(Container ctr)
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


		public bool CheckFindAndRemove(EntityID id)
		{
			Container ctr;
			if (!fullMap.TryRemove(id, out ctr))
				return false;
			guidMap.ForceRemove(id.Guid);
			return true;
		}

		public enum Result
		{
			NoError,
			IDNotFound,
			IDNotFoundLocationMismatch,
			VerificationFailed,
		}

		public Result CheckFindAndRemove(EntityID id, Func<Entity, bool> verifyMatch, out Vec3? outLocation)
		{
			outLocation = null;
			Container ctr;
			if (!fullMap.TryRemove(id, out ctr))
			{
				if (guidMap.TryGetValue(id.Guid, out ctr))
				{
					outLocation = ctr.entity.ID.Position;
					return Result.IDNotFoundLocationMismatch;
				}
				return Result.IDNotFound;
			}
			outLocation = ctr.entity.ID.Position;
			if (!verifyMatch(ctr.entity))
			{
				fullMap.ForceAdd(id, ctr);
				return Result.VerificationFailed;
			}
			guidMap.ForceRemove(id.Guid);
			return Result.NoError;
		}

		private struct MovementPriority : IComparable<MovementPriority>
		{
			public readonly float Score;
			public readonly EntityID PresumedCurrent;
			public readonly Entity Destination;
			public readonly bool CurrentMatch;

			public MovementPriority(Entity current, EntityID presumedCurrent, Entity destination, EntityChange.ExecutionContext ctx, bool currentIsInconsistent, bool destIsInconsistent)
			{
				PresumedCurrent = presumedCurrent;
				Destination = destination;
				CurrentMatch = current.ID.Position == presumedCurrent.Position;


				Score = 1;
				if (CurrentMatch)
					Score+=10;
				if (destIsInconsistent)
					Score *= 0.5f;


				if (!CurrentMatch /*&& destIsInconsistent*/ && !currentIsInconsistent)
					Score = -1;
			}

			public int CompareTo(MovementPriority other)
			{
				return new Helper.Comparator()
					.Append(Score, other.Score)
					.Append(Destination, other.Destination)
					.Append(PresumedCurrent, other.PresumedCurrent)
					.Finish();
			}

			public static bool operator >(MovementPriority a, MovementPriority b)
			{
				return a.CompareTo(b) > 0;
			}
			public static bool operator <(MovementPriority a, MovementPriority b)
			{
				return a.CompareTo(b) < 0;
			}

		}
		private struct InsertPriority : IComparable<InsertPriority>
		{
			public readonly float Score;
			public readonly Entity Destination;

			public InsertPriority(Entity destination, EntityChange.ExecutionContext ctx, InconsistencyCoverage ic)
			{
				Destination = destination;

				Score = 1;
				Score /= (1f +  ic.GetInconsistencyAtR(ctx.LocalSpace.Relativate(destination.ID.Position)));
			}

			public int CompareTo(InsertPriority other)
			{
				int rs = Score.CompareTo(other.Score);
				if (rs != 0)
					return rs;
				return Destination.CompareTo(other.Destination);
			}

			public static bool operator >(InsertPriority a, InsertPriority b)
			{
				return a.CompareTo(b) > 0;
			}
			public static bool operator <(InsertPriority a, InsertPriority b)
			{
				return a.CompareTo(b) < 0;
			}

		}

		public int ResolveConflictFreeOperations(InconsistencyCoverage ic, EntityChange.ExecutionContext ctx)
		{
			int errors = 0;

			Parallel.ForEach(deferredInserts.Values, bag =>
			{
				Entity e;
				InsertPriority best = new InsertPriority();
				Interlocked.Add(ref errors, bag.Count - 1);
				while (bag.TryTake(out e))
				{
					var candidate = new InsertPriority(e, ctx, ic);
					if (candidate > best)
						best = candidate;
				}
				if (best.Destination == null || !Insert(best.Destination))
					Interlocked.Increment(ref errors);
			});
			deferredInserts.Clear();

			Parallel.ForEach(fullMap.Values, ctr =>
			{
				if (ctr.deferredUpdates.IsEmpty)
					return;
				Tuple<EntityID, Entity> tuple;
				bool inc = ic.IsInconsistentR(ctx.LocalSpace.Relativate(ctr.entity.ID.Position));
				MovementPriority best = new MovementPriority();
				Interlocked.Add(ref errors, ctr.deferredUpdates.Count - 1);
				while (ctr.deferredUpdates.TryTake(out tuple))
				{
					bool destInc = ic.IsInconsistentR(ctx.LocalSpace.Relativate(tuple.Item2.ID.Position));
					var candidate = new MovementPriority(ctr.entity, tuple.Item1, tuple.Item2, ctx, inc,destInc);
					if (candidate > best)
						best = candidate;
				}
				if (best.Destination != null)
				{
					ctr.entity = best.Destination;
					tree = null;
				}
				else
					Interlocked.Increment(ref errors);
			});
			EntityID id;
			while (deferredRemovals.TryTake(out id))
				if (CheckFindAndRemove(id))
					tree = null;
				else
					errors++;


			return errors;
		}

		public void ConflictFreeInsert(EntityID origin, Entity entity)
		{
			Container rs;
			if (guidMap.TryGetValue(entity.ID.Guid, out rs))
				rs.deferredUpdates.Add(new Tuple<EntityID, Entity>(origin, entity));
			else
				deferredInserts.GetOrAdd(entity.ID.Guid, id => new ConcurrentBag<Entity>()).Add(entity);

		}

		public void ConflictFreeUpdateEntity(EntityID original, Entity updated)
		{
			Container rs;
			if (guidMap.TryGetValue(original.Guid, out rs))
				rs.deferredUpdates.Add(new Tuple<EntityID,Entity>(original,updated));
		}

		public void ConflictFreeFindAndRemove(EntityID id)
		{
			if (fullMap.ContainsKey(id))
				deferredRemovals.Add(id);
		}

		public void FindAndRemove(EntityID id, Func<Entity, bool> verifyMatch)
		{
			Container ctr;
			if (!fullMap.TryRemove(id, out ctr))
			{
				if (guidMap.ContainsKey(id.Guid))
					throw new ExecutionException(id, "Location mismatch");
				else
					throw new ExecutionException(id, "ID not found for removal");
			}
			if (!verifyMatch(ctr.entity))
			{
				fullMap.ForceAdd(id, ctr);
				throw new ExecutionException(id, "Verification failed");
			}
			guidMap.ForceRemove(id.Guid);
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
