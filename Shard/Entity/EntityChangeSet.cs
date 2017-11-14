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

		[Serializable]
		private class Set<T> : ISerializable where T : Change
		{
			private ConcurrentBag<T> bag = new ConcurrentBag<T>();

			public void Add(T item)
			{
				bag.Add(item);
			}

			public Set<T> Clone()
			{
				Set<T> rs = new Set<T>();
				foreach (T el in bag)
					rs.bag.Add(el);	// no need to clone: all readonly
				if (rs.bag.Count != bag.Count)
					throw new IntegrityViolation("Count mismatch: "+rs.bag.Count +" != "+bag.Count);
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

			public int Execute(EntityPool pool)
			{
				T[] ar = bag.ToArray();
				Array.Sort(ar);
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

			public Set()
			{ }
			public Set(SerializationInfo info, StreamingContext context)
			{
				T[] ar = (T[])info.GetValue("Changes", typeof( T[] ));
				foreach (var c in ar)
					bag.Add(c);
			}

			public void GetObjectData(SerializationInfo info, StreamingContext context)
			{
				info.AddValue("Changes", bag.ToArray());
			}
		}

		private Set<Instantiation> instantiations = new Set<Instantiation>();
		private Set<Removal> removals = new Set<Removal>();
		private Set<Motion> motions = new Set<Motion>();
		private Set<Broadcast> messages = new Set<Broadcast>();
		private Set<StateAdvertisement> advertisements = new Set<StateAdvertisement>();

		public void Add(Instantiation inst)
		{
			instantiations.Add(inst);
		}

		public void Add(Removal rem)
		{
			removals.Add(rem);
		}
		public void Add(Motion mot)
		{
			motions.Add(mot);
		}

		public void Add(Broadcast mes)
		{
			messages.Add(mes);
		}
		public void Add(StateAdvertisement adv)
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



		[Serializable()]
		public abstract class Change : IComparable
		{
			public readonly EntityID Origin;

			protected Change(EntityID origin)
			{
				Origin = origin;
			}

			public abstract bool TargetIsLocatedIn(Box cube);

			public abstract int CompareTo(object other);
			public abstract bool Execute(EntityPool pool);
		}

		[Serializable]
		public class Removal : Change
		{
			public readonly EntityID Target;

			public Removal(EntityID origin, EntityID target) : base(origin)
			{
				Target = target;
			}

			public override int CompareTo(object obj)
			{
				Removal other = obj as Removal;
				if (other == null)
					return 0;
				int cmp = Origin.CompareTo(other.Origin);
				if (cmp != 0)
					return cmp;
				return Target.CompareTo(other.Target);
			}

			public override bool Execute(EntityPool pool)
			{
				return pool.FindAndRemove(Target, e => Simulation.CheckDistance("Removal", Target.Position, e, Simulation.M));
			}

			public override bool TargetIsLocatedIn(Box cube)
			{
				return cube.Contains(Target.Position);
			}
		}

		[Serializable]
		public class Instantiation : Change
		{
			public readonly Vec3 TargetLocation;
			public readonly EntityAppearance Appearance;
			public readonly byte[] LogicState;
			public readonly string LogicID;

			public Instantiation(EntityID origin, Vec3 targetLocation, EntityAppearance appearance, string logicID, byte[] logicState) : base(origin)
			{
				TargetLocation = targetLocation;
				LogicState = logicState;
				LogicID = logicID;
				Appearance = appearance;
			}

			public override int CompareTo(object obj)
			{
				Instantiation other = obj as Instantiation;
				if (other == null)
					return 0;
				return new Helper.Comparator()
						.Append(Origin, other.Origin)
						.Append(TargetLocation, other.TargetLocation)
						.Append(Appearance, other.Appearance)
						.Append(LogicID, other.LogicID)
						.Append(LogicState, other.LogicState)
						.Finish();
			}

			public override bool Execute(EntityPool pool)
			{
				if (!Simulation.CheckDistance("Insert", Origin.Position, TargetLocation, Simulation.M))
					return false;
				return pool.Insert(new Entity(new EntityID(Guid.NewGuid(), TargetLocation), LogicID, LogicState, Appearance, null, null));
			}

			public override bool TargetIsLocatedIn(Box cube)
			{
				return cube.Contains(TargetLocation);
			}
		}

		[Serializable]
		public class Motion : Instantiation
		{
			[NonSerialized()] public readonly EntityLogic.State DirectState;
			public Motion(EntityID origin, Vec3 targetLocation, EntityAppearance appearance, string logicID, byte[] logicState, bool isInconsistent) : base(origin, targetLocation, appearance, logicID, logicState)
			{}

			public Motion(Entity e, EntityLogic.State newState, EntityAppearance newAppearance, Vec3 destination) : base(e.ID, destination, newAppearance, newState != null ? newState.LogicID : null, newState != null ? newState.BinaryState : null)
			{
				DirectState = newState;
			}

			public override int CompareTo(object obj)
			{
				Motion other = obj as Motion;
				if (other == null)
					return 0;
				return new Helper.Comparator()
						.Append(base.CompareTo(other))
						.Finish();
			}

			protected Entity Entity
			{
				get
				{
					if (DirectState != null)
						return new Entity(Origin.Relocate(TargetLocation), DirectState, Appearance, null, null);
					return new Entity(Origin.Relocate(TargetLocation), LogicID, LogicState, Appearance, null, null);
				}
			}

			public override bool Execute(EntityPool pool)
			{
				Int3 opCoords = TargetLocation.FloorInt3;
				if (Simulation.Owns(Origin.Position))
				{
					Entity e;
					if (!pool.Find(Origin, out e))
						return false;
					if (!Simulation.CheckDistance("Motion", TargetLocation, e, Simulation.M))
						return false;

					if (Simulation.Owns(TargetLocation))
					{
						return pool.UpdateEntity(e, Entity);
					}
					else
						return pool.FindAndRemove(e.ID);
				}
				else
				{
					if (!Simulation.Owns(TargetLocation))
					{
						Log.Message("Motion: Shard coordinate mismatch. Local=" + Simulation.ID + ", target=" + TargetLocation);
						return false;
					}
					if (!Simulation.CheckDistance("Motion", Origin.Position, TargetLocation, Simulation.M))
						return false;
					return pool.Insert(Entity);
				}

			}
		}

		public StateAdvertisement FindAdvertisementFor(EntityID id)
		{
			return advertisements.FindOrigin(id);
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
			info.AddValue("advertisements", advertisements);
			info.AddValue("instantiations", instantiations);
			info.AddValue("messages", messages);
			info.AddValue("motions", motions);
			info.AddValue("removals", removals);
		}

		public EntityChangeSet() { }

		private static void Get<T>(string name, ref Set<T> set, SerializationInfo info) where T : Change
		{
			set = (Set<T>)info.GetValue(name, typeof(Set<T>));
		}
		public EntityChangeSet(SerializationInfo info, StreamingContext context)
		{
			Get("advertisements", ref advertisements, info);
			Get("instantiations", ref instantiations, info);
			Get("messages", ref messages, info);
			Get("motions", ref motions, info);
			Get("removals", ref removals, info);
		}

		[Serializable]
		public class Broadcast : Change
		{
			public readonly byte[] Payload;
			public readonly int SentOrderID;

			public Broadcast(EntityID sender, byte[] payload, int orderID) : base(sender)
			{
				Payload = payload;
				SentOrderID = orderID;
			}

			protected Broadcast(EntityID origin, int sentOrderID, byte[] payload) : base(origin)
			{
				SentOrderID = sentOrderID;
				Payload = payload;
			}

			public OrderedEntityMessage Message
			{
				get
				{
					return new OrderedEntityMessage(SentOrderID, new EntityMessage(Origin, Payload));
				}
			}

			public override int CompareTo(object obj)
			{
				var other = obj as Broadcast;
				if (other == null)
					return 0;
				return new Helper.Comparator()
						.Append(Origin, other.Origin)
						.Append(SentOrderID, other.SentOrderID)
						.Append(Payload, other.Payload)
						.Finish();
			}

			public override bool Execute(EntityPool pool)
			{
				pool.BroadcastMessage(Origin.Position, Message);
				return true;
			}

			public override bool TargetIsLocatedIn(Box cube)
			{
				float r = Simulation.R;
				return cube.Intersects(Box.CreateUsingMax(Origin.Position - r, Origin.Position + r, Bool3.True));
			}
		}

		[Serializable]
		public class Message : Broadcast
		{
			public readonly Guid TargetEntityID;

			public Message(EntityID origin, int sentOrderID, Guid targetEntityID, byte[] payload) : base(origin, sentOrderID, payload)
			{
				TargetEntityID = targetEntityID;
			}
			public override int CompareTo(object obj)
			{
				var other = obj as Message;
				if (other == null)
					return 0;
				return new Helper.Comparator()
						.Append(base.CompareTo(other))
						.Append(TargetEntityID, other.TargetEntityID)
						.Finish();
			}

			public override bool Execute(EntityPool pool)
			{
				return pool.RelayMessage(Origin.Position, TargetEntityID, Message);
			}
		}


		[Serializable]
		public class StateAdvertisement : Change
		{
			public readonly EntityAppearance Appearance;
			public readonly Vec3 Velocity;

			public StateAdvertisement(EntityID id, EntityAppearance fullAppearance, Vec3 velocity) : base(id)
			{
				Appearance = fullAppearance;
				Velocity = velocity;
			}

			public StateAdvertisement(EntityContact entityContact) : base(entityContact.ID)
			{
				Appearance = entityContact.Appearance;
				Velocity = entityContact.Velocity;
			}

			public override bool Execute(EntityPool pool)
			{
				pool.AddContact(new EntityContact(Origin, Appearance,Velocity));
				return true;
			}

			public override int CompareTo(object obj)
			{
				var other = obj as StateAdvertisement;
				if (other == null)
					return 0;
				return new Helper.Comparator()
						.Append(Origin, other.Origin)
						.Append(Appearance, other.Appearance)
						.Finish();
			}

			public override bool TargetIsLocatedIn(Box cube)
			{
				float r = Simulation.SensorRange;
				return cube.Intersects(Box.CreateUsingMax(Origin.Position - r, Origin.Position + r, Bool3.True));
			}
		}

	}

}
