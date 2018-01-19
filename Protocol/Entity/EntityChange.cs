using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard.EntityChange
{
	public struct EntityRanges
	{
		/// <summary>
		/// Maximum movement range
		/// </summary>
		public readonly float M;
		/// <summary>
		/// Maximum influence range
		/// </summary>
		public readonly float R;
		/// <summary>
		/// Maximum sensor range
		/// </summary>
		public readonly float S;


		public EntityRanges(float r, float m, float s)
		{
			M = m;
			R = r;
			S = s;
		}



	}

	public abstract class ExecutionContext
	{
		public abstract void LogMessage(string message);
		public abstract void LogError(string message);
		public readonly EntityRanges Ranges;
		/// <summary>
		/// Local simulation space
		/// </summary>
		public readonly Box LocalSpace;

		public int GenerationNumber { get; protected set; }

		public float GetDistance(Vec3 a, Vec3 b)
		{
			return Vec3.GetChebyshevDistance(a, b);
		}

		public ExecutionContext(EntityRanges ranges, Box localSimulationSpace)
		{
			Ranges = ranges;
			LocalSpace = localSimulationSpace;
		}
		public bool CheckM(string task, Vec3 taskLocation, Vec3 currentEntityPosition)
		{
			float dist = GetDistance(taskLocation, currentEntityPosition);
			if (dist <= Ranges.M)
				return true;
			LogError(currentEntityPosition + ": " + task + " exceeded maximum range (" + Ranges.M + "): " + dist);
			return false;
		}

		public bool CheckM(string task, Vec3 taskLocation, EntityID currentEntityPosition)
		{
			float dist = GetDistance(taskLocation, currentEntityPosition.Position);
			if (dist <= Ranges.M)
				return true;
			LogError(currentEntityPosition + ": " + task + " exceeded maximum range (" + Ranges.M + "): " + dist);
			return false;
		}
		public bool CheckM(string task, Vec3 p, Entity reference)
		{
			return CheckM(task, p, reference.ID);
		}

		public bool CheckR(string task, Vec3 taskLocation, Vec3 currentEntityPosition)
		{
			float dist = GetDistance(taskLocation, currentEntityPosition);
			if (dist <= Ranges.R)
				return true;
			LogError(currentEntityPosition + ": " + task + " exceeded maximum range (" + Ranges.R + "): " + dist);
			return false;
		}
		public bool CheckR(string task, Vec3 taskLocation, EntityID currentEntityPosition)
		{
			float dist = GetDistance(taskLocation, currentEntityPosition.Position);
			if (dist <= Ranges.R)
				return true;
			LogError(currentEntityPosition + ": " + task + " exceeded maximum range (" + Ranges.R + "): " + dist);
			return false;
		}
		public bool CheckR(string task, Vec3 p, Entity reference)
		{
			return CheckR(task, p, reference.ID.Position);
		}

		//public bool CheckDistance(string task, Vec3 targetPosition, EntityID referencePosition, float maxDistance)
		//{
		//	float dist = EntityRanges.GetDistance(referencePosition.Position, targetPosition);
		//	if (dist <= maxDistance)
		//		return true;
		//	LogError(task + ": exceeded maximum range (" + maxDistance + "): " + dist);
		//	return false;
		//}


		internal Vec3 ClampDestination(string task, Vec3 newPosition, EntityID currentEntityPosition, float maxDistance)
		{
			float dist = GetDistance(newPosition, currentEntityPosition.Position);
			if (dist <= maxDistance)
				return newPosition;

			LogError(currentEntityPosition + ": " + task + " exceeded maximum range (" + maxDistance + "): " + dist);
			newPosition = currentEntityPosition.Position + (newPosition - currentEntityPosition.Position) * maxDistance / dist;

			Debug.Assert(GetDistance(newPosition, currentEntityPosition.Position) <= maxDistance);

			return newPosition;
		}

		public abstract void RelayClientMessage(Guid entityID, Guid clientID, int channel, byte[] data);

	}


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


		public abstract bool Affects(Box cube, ExecutionContext ctx);

		public abstract int CompareTo(object other);
		public abstract bool Execute(EntityPool pool, ExecutionContext ctx);
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


		public override bool Execute(EntityPool pool, ExecutionContext ctx)
		{
			return pool.FindAndRemove(Target, e => ctx.CheckM("Removal", Target.Position, e));
		}

		public override bool Affects(Box cube, ExecutionContext ctx)
		{
			return cube.Contains(Target.Position);
		}


	}

	[Serializable]
	public class Instantiation : Abstract
	{
		public readonly Vec3 TargetLocation;
		public readonly EntityAppearanceCollection Appearances;
		public readonly byte[] SerialLogic;
		[NonSerialized]
		protected EntityLogic directState;

		public Instantiation(EntityID origin, Vec3 targetLocation, EntityAppearanceCollection appearance, EntityLogic directState, byte[] serialLogic) : base(origin)
		{
			TargetLocation = targetLocation;
			SerialLogic = serialLogic;
			Appearances = appearance;
			this.directState = directState;
		}

		public override int GetHashCode()
		{
			return Helper.Hash(this)
				.Add(Origin)
				.Add(TargetLocation)
				.Add(Appearances)
				.Add(SerialLogic)
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

		public override bool Execute(EntityPool pool, ExecutionContext ctx)
		{
			if (!ctx.CheckM("Insert", Origin.Position, TargetLocation))
				return false;
			var rs = pool.Insert(new Entity(new EntityID(Guid.NewGuid(), TargetLocation), directState, SerialLogic, Appearances));
			directState = null;
			return rs;
		}

		public override bool Affects(Box cube, ExecutionContext ctx)
		{
			return cube.Contains(TargetLocation);
		}
	}

	[Serializable]
	public class Motion : Instantiation
	{

		public Motion(EntityID origin, Vec3 targetLocation, EntityAppearanceCollection appearance, EntityLogic logic,byte[] serialLogic) : base(origin, targetLocation, appearance, logic,serialLogic)
		{}

		//public Motion(Entity e, byte[] newState, EntityAppearanceCollection newAppearance, Vec3 destination) : base(e.ID, destination, newAppearance, newState)
		//{ }


		protected Entity Entity
		{
			get
			{
				var rs = new Entity(Origin.Relocate(TargetLocation), directState, SerialLogic, Appearances, null, null);
				directState = null;
				return rs;
			}
		}

		public override bool Execute(EntityPool pool, ExecutionContext ctx)
		{
			Int3 opCoords = TargetLocation.FloorInt3;
			if (ctx.LocalSpace.Contains(Origin.Position))
			{
				Entity e;
				if (!pool.Find(Origin, out e))
					return false;
				if (!ctx.CheckM("Motion", TargetLocation, e))
					return false;

				if (ctx.LocalSpace.Contains(TargetLocation))
				{
					return pool.UpdateEntity(e, Entity);
				}
				else
					return pool.FindAndRemove(e.ID);
			}
			else
			{
				if (!ctx.LocalSpace.Contains(TargetLocation))
				{
					ctx.LogMessage("Motion: Shard coordinate mismatch. Local=" + ctx.LocalSpace + ", target=" + TargetLocation);
					return false;
				}
				if (!ctx.CheckM("Motion", Origin.Position, TargetLocation))
					return false;
				return pool.Insert(Entity);
			}

		}

		public override bool Affects(Box cube, ExecutionContext ctx)
		{
			return cube.Contains(TargetLocation) || cube.Contains(Origin.Position);
		}

	}



	[Serializable]
	public class Broadcast : Abstract
	{
		public readonly int Channel;
		public readonly byte[] Payload;
		public readonly int SentOrderID;


		public Broadcast(EntityID origin, int sentOrderID, int channel, byte[] payload) : base(origin)
		{
			Channel = channel;
			SentOrderID = sentOrderID;
			Payload = payload;
		}

		public OrderedEntityMessage MakeMessage(bool isBroadcast)
		{
			return new OrderedEntityMessage(SentOrderID, new EntityMessage(new Actor(Origin.Guid,true), isBroadcast,Channel,Payload));
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
					.Append(Channel,other.Channel)
					.Append(Payload, other.Payload);
			return c.Finish();
		}

		public override int GetHashCode()
		{
			return Helper.Hash(this)
					.Add(Origin)
					.Add(SentOrderID)
					.Add(Channel)
					.Add(Payload)
					.GetHashCode();
		}

		public override bool Execute(EntityPool pool, ExecutionContext ctx)
		{
			pool.BroadcastMessage(Origin.Position, MakeMessage(true));
			return true;
		}

		public override bool Affects(Box cube, ExecutionContext ctx)
		{
			return cube.Intersects(Box.CenterExtent(Origin.Position,ctx.Ranges.R, Bool3.True));
		}
	}

	[Serializable]
	public class Message : Broadcast
	{
		public readonly Guid TargetEntityID;

		public Message(EntityID origin, int sentOrderID, Guid targetEntityID, int channel, byte[] payload) : base(origin, sentOrderID, channel, payload)
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

		public override bool Execute(EntityPool pool, ExecutionContext ctx)
		{
			return pool.RelayMessage(Origin.Position, TargetEntityID, MakeMessage(false));
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

		public override bool Execute(EntityPool pool, ExecutionContext ctx)
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


		public override bool Affects(Box cube, ExecutionContext ctx)
		{
			return cube.Intersects(Box.CenterExtent(Origin.Position, ctx.Ranges.S, Bool3.True));
		}
	}

}
