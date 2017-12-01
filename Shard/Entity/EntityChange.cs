using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard.EntityChange
{



	[Serializable]
	public abstract class Abstract : IComparable
	{
		public readonly EntityID Origin;

		protected Abstract(EntityID origin)
		{
			Origin = origin;
		}

		public override bool Equals(object obj)
		{
			bool rs = CompareTo(obj) == 0;
			return rs;
		}


		public abstract bool Affects(Box cube);

		public abstract int CompareTo(object other);
		public abstract bool Execute(EntityPool pool);
		public override int GetHashCode()
		{
			return Origin.GetHashCode();
		}
	}

	[Serializable]
	public class Removal : Abstract
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
				return 1;
			if (other == this)
				return 0;
			int cmp = Origin.CompareTo(other.Origin);
			if (cmp != 0)
				return cmp;
			return Target.CompareTo(other.Target);
		}
		public override int GetHashCode()
		{
			return Helper.Hash(this).Add(Origin).Add(Target).GetHashCode();
		}


		public override bool Execute(EntityPool pool)
		{
			return pool.FindAndRemove(Target, e => Simulation.CheckDistance("Removal", Target.Position, e, Simulation.M));
		}

		public override bool Affects(Box cube)
		{
			return cube.Contains(Target.Position);
		}


	}

	[Serializable]
	public class Instantiation : Abstract
	{
		public readonly Vec3 TargetLocation;
		public readonly EntityAppearanceCollection Appearances;
		public readonly EntityLogic Logic;

		public Instantiation(EntityID origin, Vec3 targetLocation, EntityAppearanceCollection appearance, EntityLogic logic) : base(origin)
		{
			TargetLocation = targetLocation;
			Logic = logic;
			Appearances = appearance;
		}

		public override int GetHashCode()
		{
			return Helper.Hash(this)
				.Add(Origin)
				.Add(TargetLocation)
				.Add(Appearances)
				.Add(Logic)
				.GetHashCode();
		}


		public override int CompareTo(object obj)
		{
			Instantiation other = obj as Instantiation;
			if (other == null)
				return 1;
			if (other == this)
				return 0;
			return new Helper.Comparator()
					.Append(Origin, other.Origin)
					.Append(TargetLocation, other.TargetLocation)
					.Append(Appearances, other.Appearances)
					//.Append(Logic, other.Logic)
					.Finish();
		}

		public override bool Execute(EntityPool pool)
		{
			if (!Simulation.CheckDistance("Insert", Origin.Position, TargetLocation, Simulation.M))
				return false;
			return pool.Insert(new Entity(new EntityID(Guid.NewGuid(), TargetLocation), Logic, Appearances, null, null));
		}

		public override bool Affects(Box cube)
		{
			return cube.Contains(TargetLocation);
		}
	}

	[Serializable]
	public class Motion : Instantiation
	{
		public Motion(EntityID origin, Vec3 targetLocation, EntityAppearanceCollection appearance, EntityLogic logic) : base(origin, targetLocation, appearance, logic)
		{ }

		public Motion(Entity e, EntityLogic newState, EntityAppearanceCollection newAppearance, Vec3 destination) : base(e.ID, destination, newAppearance, newState)
		{ }


		protected Entity Entity
		{
			get
			{
				return new Entity(Origin.Relocate(TargetLocation), Logic, Appearances, null, null);
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

		public override bool Affects(Box cube)
		{
			return cube.Contains(TargetLocation) || cube.Contains(Origin.Position);
		}

	}



	[Serializable]
	public class Broadcast : Abstract
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
				return 1;
			if (other == this)
				return 0;
			var c = new Helper.Comparator()
					.Append(Origin, other.Origin)
					.Append(SentOrderID, other.SentOrderID)
					.Append(Payload, other.Payload);
			return c.Finish();
		}

		public override int GetHashCode()
		{
			return Helper.Hash(this)
					.Add(Origin)
					.Add(SentOrderID)
					.Add(Payload)
					.GetHashCode();
		}

		public override bool Execute(EntityPool pool)
		{
			pool.BroadcastMessage(Origin.Position, Message);
			return true;
		}

		public override bool Affects(Box cube)
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
			if (obj == null)
				return 1;
			if (obj == this)
				return 0;
			var other = obj as Message;
			if (other == null)	//nothing i know
				return base.CompareTo(obj);
			var c = new Helper.Comparator()
					.Append(base.CompareTo(other))
					.Append(TargetEntityID, other.TargetEntityID);
			return c.Finish();
		}
		public override int GetHashCode()
		{
			return Helper.Hash(this)
					.Add(base.GetHashCode())
					.Add(TargetEntityID)
					.GetHashCode();
		}

		public override bool Execute(EntityPool pool)
		{
			return pool.RelayMessage(Origin.Position, TargetEntityID, Message);
		}
	}


	[Serializable]
	public class StateAdvertisement : Abstract
	{
		public readonly EntityAppearanceCollection Appearances;
		public readonly Vec3 Velocity;

		public StateAdvertisement(EntityID id, EntityAppearanceCollection fullAppearance, Vec3 velocity) : base(id)
		{
			Appearances = fullAppearance;
			Velocity = velocity;
		}

		public StateAdvertisement(EntityContact entityContact) : base(entityContact.ID)
		{
			Appearances = entityContact.Appearances;
			Velocity = entityContact.Velocity;
		}

		public override bool Execute(EntityPool pool)
		{
			pool.AddContact(new EntityContact(Origin, Appearances, Velocity));
			return true;
		}

		public override int CompareTo(object obj)
		{
			var other = obj as StateAdvertisement;
			if (other == null)
				return 1;
			if (other == this)
				return 0;
			return new Helper.Comparator()
					.Append(Origin, other.Origin)
					.Append(Appearances, other.Appearances)
					.Finish();
		}

		public override int GetHashCode()
		{
			return Helper.Hash(this)
					.Add(Origin)
					.Add(Appearances)
					.GetHashCode();
		}


		public override bool Affects(Box cube)
		{
			float r = Simulation.SensorRange;
			return cube.Intersects(Box.CreateUsingMax(Origin.Position - r, Origin.Position + r, Bool3.True));
		}
	}

}
