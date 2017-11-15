using System;
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
			public void Include(Set<T> other, Box targetSpace)
			{
				foreach (T el in other.bag)
					if (el.TargetIsLocatedIn(targetSpace))
						Add(el);
			}
			public void FilterByTargetLocation(Box targetSpace)
			{
				List<T> temp = new List<T>();
				T el;
				while (bag.TryTake(out el))
					if (el.TargetIsLocatedIn(targetSpace))
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

			public int Execute(EntityPool pool)
			{
				var ar = ToSortedArray();
				int numErrors = 0;
				Parallel.ForEach(ar, c => { if (!c.Execute(pool)) Interlocked.Increment(ref numErrors); });
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
				var h = new Helper.HashCombiner();
				foreach (var v in ToSortedArray())
					h.Add(v);
				return h.GetHashCode();
			}

		}

		private Set<EntityChange.Instantiation> instantiations = new Set<EntityChange.Instantiation>();
		private Set<EntityChange.Removal> removals = new Set<EntityChange.Removal>();
		private Set<EntityChange.Motion> motions = new Set<EntityChange.Motion>();
		private Set<EntityChange.Broadcast> messages = new Set<EntityChange.Broadcast>();
		private Set<EntityChange.StateAdvertisement> advertisements = new Set<EntityChange.StateAdvertisement>();

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
			messages.Add(mes);
		}
		public void Add(EntityChange.StateAdvertisement adv)
		{
			advertisements.Add(adv);
		}

		/// <summary>
		/// Executes all local changes on the specified pool, and automatically dispatches queued pool events
		/// </summary>
		/// <param name="pool">Pool to execute changes on</param>
		public int Execute(EntityPool pool)
		{
			int numErrors = 0;
			numErrors += messages.Execute(pool);
			numErrors += motions.Execute(pool);
			numErrors += removals.Execute(pool);
			numErrors += instantiations.Execute(pool);
			numErrors += advertisements.Execute(pool);
			pool.DispatchAll();
			return numErrors;
		}

		/// <summary>
		/// Selectively adds all remote changes whose target lies within the given target space
		/// </summary>
		/// <param name="remote">Source of the changes to add</param>
		/// <param name="targetSpace">Space to check for. Only changes whose target lies within this cube are added</param>
		public void Include(EntityChangeSet remote, Box targetSpace)
		{
			messages.Include(remote.messages, targetSpace);
			motions.Include(remote.motions, targetSpace);
			removals.Include(remote.removals, targetSpace);
			instantiations.Include(remote.instantiations, targetSpace);
			advertisements.Include(remote.advertisements, targetSpace);
		}


		public EntityChange.StateAdvertisement FindAdvertisementFor(EntityID id)
		{
			return advertisements.FindOrigin(id);
		}

		public EntityChange.Motion FindMotionOf(Guid id)
		{
			return motions.FindOrigin(id);
		}

		public EntityChangeSet Clone()
		{
			EntityChangeSet rs = new EntityChangeSet();
			rs.advertisements = advertisements.Clone();
			rs.instantiations = instantiations.Clone();
			rs.messages = messages.Clone();
			rs.motions = motions.Clone();
			rs.removals = removals.Clone();
			return rs;
		}

		public void Include(EntityChangeSet cs)
		{
			advertisements.Include(cs.advertisements);
			instantiations.Include(cs.instantiations);
			messages.Include(cs.messages);
			motions.Include(cs.motions);
			removals.Include(cs.removals);
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			foreach (var p in NamedSets)
				info.AddValue(p.Key, p.Value);
		}

		public IEnumerable<KeyValuePair<string,AbstractSet>> NamedSets
		{
			get
			{
				yield return new KeyValuePair<string, AbstractSet>("advertisements", advertisements);
				yield return new KeyValuePair<string, AbstractSet>("instantiations", instantiations);
				yield return new KeyValuePair<string, AbstractSet>("messages", messages);
				yield return new KeyValuePair<string, AbstractSet>("motions", motions);
				yield return new KeyValuePair<string, AbstractSet>("removals", removals);
			}
		}
		public IEnumerable<AbstractSet> Sets
		{
			get
			{
				yield return advertisements;
				yield return instantiations;
				yield return messages;
				yield return motions;
				yield return removals;
			}
		}



		public EntityChangeSet() { }

		private static void Get<T>(string name, ref Set<T> set, SerializationInfo info) where T : EntityChange.Abstract
		{

			set = (Set<T>)info.GetValue(name, typeof(Set<T>));
		}

		internal void FilterByTargetLocation(Box targetSpace)
		{
			messages.FilterByTargetLocation(targetSpace);
			motions.FilterByTargetLocation(targetSpace);
			removals.FilterByTargetLocation(targetSpace);
			instantiations.FilterByTargetLocation(targetSpace);
			advertisements.FilterByTargetLocation(targetSpace);
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
			var h = new Helper.HashCombiner()
				.Add(base.GetHashCode());
			foreach (var s in Sets)
				h.Add(s);
			return h.GetHashCode();
		}

		public EntityChangeSet(SerializationInfo info, StreamingContext context)
		{
			Get("advertisements", ref advertisements, info);
			Get("instantiations", ref instantiations, info);
			Get("messages", ref messages, info);
			Get("motions", ref motions, info);
			Get("removals", ref removals, info);
		}

	}

}
