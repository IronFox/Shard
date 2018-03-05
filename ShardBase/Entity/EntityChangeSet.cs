using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{

	[Serializable()]
	public class EntityChangeSet : ISerializable
	{

		public abstract class AbstractSet
		{
			public abstract int Size { get; }
		}

		[Serializable]
		private class Set<T> : AbstractSet, ISerializable where T : EntityChange.Abstract
		{
			private ConcurrentBag<T> bag = new ConcurrentBag<T>();

			public override int Size => bag.Count;

			public void Add(T item)
			{
				bag.Add(item);
			}

			public Set<T> Clone()
			{
				Set<T> rs = new Set<T>();
				foreach (T el in bag)
					rs.bag.Add(el); // no need to clone: all readonly
				if (rs.bag.Count != bag.Count)
					throw new IntegrityViolation("Count mismatch: " + rs.bag.Count + " != " + bag.Count);
				return rs;
			}

			public void Include(Set<T> other)
			{
				foreach (T el in other.bag)
					bag.Add(el);
			}
			public void Include(Set<T> other, Box targetSpace, EntityChange.ExecutionContext ctx)
			{
				foreach (T el in other.bag)
					if (el.Affects(targetSpace, ctx))
						Add(el);
			}
			public void FilterByTargetLocation(Box targetSpace, EntityChange.ExecutionContext ctx)
			{
				List<T> temp = new List<T>();
				T el;
				while (bag.TryTake(out el))
					if (el.Affects(targetSpace,ctx))
						temp.Add(el);
				foreach (var e in temp)
					bag.Add(e);
			}

			public T[] ToSortedArray()
			{
				T[] ar = bag.ToArray();
				Array.Sort(ar);
				return ar;
			}

			public int Execute(EntityPool pool, EntityChange.ExecutionContext ctx)
			{
				//var ar = ToSortedArray();	//no point sorting when we're gonna execute the changes in parallel anyways
				int numErrors = 0;
				Parallel.ForEach(bag, c => { if (!c.Execute(pool,ctx)) Interlocked.Increment(ref numErrors); });
				return numErrors;
			}

			public T FindOrigin(EntityID id)
			{
				foreach (var c in bag)
					if (c.Origin == id)
						return c;
				return null;
			}
			public T FindOrigin(Guid guid)
			{
				foreach (var c in bag)
					if (c.Origin.Guid == guid)
						return c;
				return null;
			}

			public Set()
			{ }
			public Set(SerializationInfo info, StreamingContext context)
			{
				T[] ar = (T[])info.GetValue("Changes", typeof(T[]));
				foreach (var c in ar)
					bag.Add(c);
			}

			public void GetObjectData(SerializationInfo info, StreamingContext context)
			{
				info.AddValue("Changes", bag.ToArray());
			}

			public override bool Equals(object obj)
			{
				if (obj == this)
					return true;
				var other = obj as Set<T>;
				if (other == null)
					return false;

				var ar0 = ToSortedArray();
				var ar1 = other.ToSortedArray();

				return ar0.SequenceEqual(ar1);
			}

			public override int GetHashCode()
			{
				var h = Helper.Hash(this);
				foreach (var v in ToSortedArray())
					h.Add(v);
				return h.GetHashCode();
			}

		}

		private Set<EntityChange.Instantiation> instantiations = new Set<EntityChange.Instantiation>();
		private Set<EntityChange.Removal> removals = new Set<EntityChange.Removal>();
		private Set<EntityChange.Motion> motions = new Set<EntityChange.Motion>();
		private Set<EntityChange.Broadcast> broadcasts = new Set<EntityChange.Broadcast>();
		private Set<EntityChange.Message> messages = new Set<EntityChange.Message>();
#if STATE_ADV
		private Set<EntityChange.StateAdvertisement> advertisements = new Set<EntityChange.StateAdvertisement>();
#endif

		public void Add(EntityChange.Instantiation inst)
		{
			instantiations.Add(inst);
		}

		public void Add(EntityChange.Removal rem)
		{
			removals.Add(rem);
		}
		public void Add(EntityChange.Motion mot)
		{
			motions.Add(mot);
		}

		public void Add(EntityChange.Broadcast mes)
		{
			broadcasts.Add(mes);
		}
		public void Add(EntityChange.Message mes)
		{
			messages.Add(mes);
		}

#if STATE_ADV
		public void Add(EntityChange.StateAdvertisement adv)
		{
			advertisements.Add(adv);
		}
#endif

		/// <summary>
		/// Executes all local changes on the specified pool, and automatically dispatches queued pool events
		/// </summary>
		/// <param name="pool">Pool to execute changes on</param>
		public int Execute(EntityPool pool, InconsistencyCoverage ic, EntityChange.ExecutionContext ctx)
		{
			int numErrors = 0;
			numErrors += motions.Execute(pool, ctx);
			numErrors += pool.ResolveConflictFreeOperations(ic, ctx);
			numErrors += removals.Execute(pool, ctx);
			//numErrors += pool.ResolveConflictFreeOperations(ic);	//removals don't do conflicting stuff (one removes first, and all others fail, but entity is removed regardless, so all win)
			numErrors += instantiations.Execute(pool, ctx);
			numErrors += pool.ResolveConflictFreeOperations(ic,ctx);
#if STATE_ADV
			if (advertisements.Size > 0)
				pool.RequireTree();
			numErrors += advertisements.Execute(pool, ic, ctx);
#endif
			numErrors += messages.Execute(pool, ctx);
			if (broadcasts.Size > 0)
				pool.RequireTree();
			numErrors += broadcasts.Execute(pool, ctx);
			pool.DispatchAll();
			return numErrors;
		}




#if STATE_ADV
		public EntityChange.StateAdvertisement FindAdvertisementFor(EntityID id)
		{
			return advertisements.FindOrigin(id);
		}
#endif

		public EntityChange.Motion FindMotionOf(Guid id)
		{
			return motions.FindOrigin(id);
		}

		public EntityChangeSet Clone()
		{
			EntityChangeSet rs = new EntityChangeSet();
#if STATE_ADV
			rs.advertisements = advertisements.Clone();
#endif
			rs.instantiations = instantiations.Clone();
			rs.broadcasts = broadcasts.Clone();
			rs.messages = messages.Clone();
			rs.motions = motions.Clone();
			rs.removals = removals.Clone();
			return rs;
		}

		public void Include(EntityChangeSet cs)
		{
#if STATE_ADV
			advertisements.Include(cs.advertisements);
#endif
			instantiations.Include(cs.instantiations);
			broadcasts.Include(cs.broadcasts);
			messages.Include(cs.messages);
			motions.Include(cs.motions);
			removals.Include(cs.removals);
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			foreach (var p in NamedSets)
				info.AddValue(p.Key, p.Value);
		}

		public AbstractSet FindNamedSet(string name)
		{
			foreach (var pair in NamedSets)
				if (pair.Key == name)
					return pair.Value;
			return null;
		}

		public IEnumerable<KeyValuePair<string,AbstractSet>> NamedSets
		{
			get
			{
#if STATE_ADV
				yield return new KeyValuePair<string, AbstractSet>("advertisements", advertisements);
#endif
				yield return new KeyValuePair<string, AbstractSet>("instantiations", instantiations);
				yield return new KeyValuePair<string, AbstractSet>("broadcasts", broadcasts);
				yield return new KeyValuePair<string, AbstractSet>("messages", messages);
				yield return new KeyValuePair<string, AbstractSet>("motions", motions);
				yield return new KeyValuePair<string, AbstractSet>("removals", removals);
			}
		}
		public IEnumerable<AbstractSet> Sets
		{
			get
			{
#if STATE_ADV
				yield return advertisements;
#endif
				yield return instantiations;
				yield return broadcasts;
				yield return messages;
				yield return motions;
				yield return removals;
			}
		}



		public EntityChangeSet() { }

		/// <summary>
		/// Duplicates all changes in the specified source affecting the specified target space
		/// </summary>
		/// <param name="source"></param>
		/// <param name="targetSpace"></param>
		public EntityChangeSet(EntityChangeSet source, Box targetSpace, EntityChange.ExecutionContext ctx)
		{
			broadcasts.Include(source.broadcasts, targetSpace, ctx);
			messages.Include(source.messages, targetSpace, ctx);
			motions.Include(source.motions, targetSpace, ctx);
			removals.Include(source.removals, targetSpace, ctx);
			instantiations.Include(source.instantiations, targetSpace, ctx);
#if STATE_ADV
			advertisements.Include(source.advertisements, targetSpace, ctx);
#endif
		}

		public List<EntityError> Evolve(IReadOnlyList<Entity> entities,
			Dictionary<Guid, ClientMessage[]> clientMessages,
			InconsistencyCoverage ic, 
			TimeSpan budget,
			EntityChange.ExecutionContext ctx)
		{
			int numErrors = 0;

			List<Task> tasks = new List<Task>();
			List<Entity.TimeTrace> tables = new List<Entity.TimeTrace>();

			ClientMessage[] clientBroadcasts = null;
			if (clientMessages != null)
				clientMessages.TryGetValue(Guid.Empty, out clientBroadcasts);
			Stopwatch watch0 = new Stopwatch();

			object lazyLock = new object();
			bool exceeded = false;
			var rs = new LazyList<EntityError>();
			Parallel.For(0, entities.Count, i =>
			{
				Entity e = entities[i];
				ClientMessage[] messages = null;
				if (clientMessages != null)
				{
					clientMessages.TryGetValue(e.ID.Guid, out messages);
				}
				var t = new Entity.TimeTrace(watch0);

				//tables.Add(t);


				EntityLogic st = null;

				try
				{
					if (!exceeded)
					{
						st = e.Evolve(t, this, Helper.Concat(clientBroadcasts, messages), ctx, ic.IsInconsistentR(ctx.LocalSpace.Relativate(e.ID.Position)));
						if (e.transientDeserializedLogic != null)
							throw new IntegrityViolation("Transient deserialized logic was not whiped");
					}
					if (exceeded || watch0.Elapsed > budget)
					{
						exceeded = true;
						throw new TimeBudgetException(budget, t);
					}
				}
				catch (AssertFailedException)
				{
					throw;
				}
				catch (Exception ex)
				{
					if (st == null)
						st = (EntityLogic)Helper.Deserialize(e.SerialLogicState);
					var error = new EntityError(e, st, ex);
					lock (lazyLock)
						rs.Add(error);
					ic.FlagInconsistentR(ctx.LocalSpace.Relativate(e.ID.Position));
					Interlocked.Increment(ref numErrors);
				}
			});

/*

			watch0.Start();
			foreach (var e in entities)
			{
				EntityMessage[] messages = null;
				if (clientMessages != null)
				{
					clientMessages.TryGetValue(e.ID.Guid, out messages);
				}
				var t = new Entity.TimeTrace(watch0);
				tables.Add(t);
				tasks.Add(e.EvolveAsync(t,this, roundNumber, maySendMessages,Helper.Concat(clientBroadcasts, messages)));
			}
			int at = 0;

			Stopwatch watch = new Stopwatch();
			watch.Start();

			LazyList<EntityEvolutionException> rs = new LazyList<EntityEvolutionException>();

			foreach (var e in entities)
			{
				try
				{
					var remaining = (budget - watch.Elapsed).NotNegative();
					if (!tasks[at].Wait( remaining))
						throw new ExecutionException(e.ID, "Failed to execute " + (EntityLogic)Helper.Deserialize(e.SerialLogicState)+" in "+remaining.TotalMilliseconds+" ms");
				}
				catch (Exception ex)
				{
					rs.Add(new EntityEvolutionException(e, ex, tables[at]));

					ic.FlagInconsistentR(Simulation.MySpace.Relativate(e.ID.Position));

					Interlocked.Increment(ref numErrors);
				}
				at++;
			}*/

			return rs.InternalList;
		}

		private static ICollection<OrderedEntityMessage> Combine(ConcurrentBag<OrderedEntityMessage> a, ConcurrentBag<OrderedEntityMessage> b)
		{
			if (b == null || b.Count == 0)
				return a?.ToArray();
			if (a == null || a.Count == 0)
				return b.ToArray();
			OrderedEntityMessage[] rs = new OrderedEntityMessage[a.Count + b.Count];
			int at = 0;
			foreach (var msg in a)
				rs[at++] = msg;
			foreach (var msg in b)
				rs[at++] = msg;
			return rs;
		}

		private static void Get<T>(string name, ref Set<T> set, SerializationInfo info) where T : EntityChange.Abstract
		{

			set = (Set<T>)info.GetValue(name, typeof(Set<T>));
		}

		public void FilterByTargetLocation(Box targetSpace, EntityChange.ExecutionContext ctx)
		{
			broadcasts.FilterByTargetLocation(targetSpace, ctx);
			messages.FilterByTargetLocation(targetSpace, ctx);
			motions.FilterByTargetLocation(targetSpace, ctx);
			removals.FilterByTargetLocation(targetSpace, ctx);
			instantiations.FilterByTargetLocation(targetSpace, ctx);
#if STATE_ADV
			advertisements.FilterByTargetLocation(targetSpace, ctx);
#endif
		}

		public bool IsEmpty
		{
			get
			{
				foreach (var s in Sets)
					if (s.Size > 0)
						return false;
				return true;
			}
		}

		public override bool Equals(object obj)
		{
			if (obj == this)
				return true;
			var other = obj as EntityChangeSet;
			if (other == null)
				return false;
			return Sets.SequenceEqual(other.Sets);
			//var it0 = NamedSets.GetEnumerator();
			//var it1 = other.NamedSets.GetEnumerator();
			//while (true)
			//{
			//	if (!it0.MoveNext())
			//	{
			//		if (!it1.MoveNext())
			//			return true;
			//		return false;
			//	}
			//	if (!it1.MoveNext())
			//		return false;
			//	if (!it0.Current.Key.Equals(it1.Current.Key))
			//		throw new IntegrityViolation(it0.Current.Key+" != "+it1.Current.Key);
			//	if (!it0.Current.Value.Equals(it1.Current.Value))
			//		return false;
			//}
		}

		public override int GetHashCode()
		{
			var h = Helper.Hash(this)
				.Add(base.GetHashCode());
			foreach (var s in Sets)
				h.Add(s);
			return h.GetHashCode();
		}

		public EntityChangeSet(SerializationInfo info, StreamingContext context)
		{
#if STATE_ADV
			Get("advertisements", ref advertisements, info);
#endif
			Get("instantiations", ref instantiations, info);
			Get("broadcasts", ref broadcasts, info);
			Get("messages", ref messages, info);
			Get("motions", ref motions, info);
			Get("removals", ref removals, info);
		}

	}

}
