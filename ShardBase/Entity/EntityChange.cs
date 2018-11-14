using Base;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard.EntityChange
{

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
		public int ReplicaCount { get; set; }

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
			if (dist <= Ranges.Motion)
				return true;
			LogError(currentEntityPosition + ": " + task + " exceeded maximum range (" + Ranges.Motion + "): " + dist);
			return false;
		}

		public bool CheckM(string task, Vec3 taskLocation, EntityID currentEntityPosition)
		{
			float dist = GetDistance(taskLocation, currentEntityPosition.Position);
			if (dist <= Ranges.Motion)
				return true;
			LogError(currentEntityPosition + ": " + task + " exceeded maximum range (" + Ranges.Motion + "): " + dist);
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

		public bool CheckT(string task, Vec3 taskLocation, Vec3 currentEntityPosition)
		{
			float dist = GetDistance(taskLocation, currentEntityPosition);
			if (dist <= Ranges.Transmission)
				return true;
			LogError(currentEntityPosition + ": " + task + " exceeded maximum transmission range (" + Ranges.Transmission + "): " + dist);
			return false;
		}
		public bool CheckT(string task, Vec3 taskLocation, EntityID currentEntityPosition)
		{
			float dist = GetDistance(taskLocation, currentEntityPosition.Position);
			if (dist <= Ranges.Transmission)
				return true;
			LogError(currentEntityPosition + ": " + task + " exceeded maximum transmission range (" + Ranges.Transmission + "): " + dist);
			return false;
		}
		public bool CheckT(string task, Vec3 p, Entity reference)
		{
			return CheckT(task, p, reference.ID.Position);
		}

		//public bool CheckDistance(string task, Vec3 targetPosition, EntityID referencePosition, float maxDistance)
		//{
		//	float dist = EntityRanges.GetDistance(referencePosition.Position, targetPosition);
		//	if (dist <= maxDistance)
		//		return true;
		//	LogError(task + ": exceeded maximum range (" + maxDistance + "): " + dist);
		//	return false;
		//}


		public virtual Vec3 ClampDestination(string task, Vec3 newPosition, EntityID currentEntityPosition, float maxDistance)
		{
			float dist = GetDistance(newPosition, currentEntityPosition.Position);
			if (dist <= maxDistance)
				return newPosition;

			LogError(currentEntityPosition + ": " + task + " exceeded maximum range (" + maxDistance + "): " + dist);
			newPosition = currentEntityPosition.Position + (newPosition - currentEntityPosition.Position) * maxDistance * 0.99f / dist;

			var newDist = GetDistance(newPosition, currentEntityPosition.Position);
			Debug.Assert(newDist <= maxDistance);

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
			Vec3? loc;
			var rs = pool.CheckFindAndRemove(Target, e => ctx.CheckM("Removal", Target.Position, e), out loc);
			switch (rs)
			{
				case EntityPool.Result.NoError:
					return true;
				case EntityPool.Result.IDNotFoundLocationMismatch:
				case EntityPool.Result.IDNotFound:
				case EntityPool.Result.VerificationFailed:
					return false;	//do not distinbuish. these errors could all be either caused by bad node states OR bad entity behavior
				default:
					throw new IntegrityViolation(Target + ": Unsupported CheckFindAndRemove() return value (" + rs+")");
			}
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
#if STATE_ADV
		public readonly EntityAppearanceCollection Appearances;
#endif
		public readonly byte[] SerialLogic;
		[NonSerialized]
		protected EntityLogic directState;

		public Instantiation(EntityID origin, Vec3 targetLocation,
#if STATE_ADV
			EntityAppearanceCollection appearance, 
#endif
			EntityLogic directState, byte[] serialLogic) : base(origin)
		{
			TargetLocation = targetLocation;
			SerialLogic = serialLogic;
#if STATE_ADV
			Appearances = appearance;
#endif
			this.directState = directState;
			if (directState == null)
				directState = (EntityLogic)Helper.Deserialize(SerialLogic);
		}

		[OnDeserialized]
		private void SetValuesOnDeserialized(StreamingContext context)
		{
			directState = (EntityLogic)Helper.Deserialize(SerialLogic);
		}

		public override int GetHashCode()
		{
			return Helper.Hash(this)
				.Add(Origin)
				.Add(TargetLocation)
#if STATE_ADV
				.Add(Appearances)
#endif
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
#if STATE_ADV
					.Append(Appearances, other.Appearances)
#endif
					//.Append(Logic, other.Logic)
					.Finish();
		}

		public override bool Execute(EntityPool pool, ExecutionContext ctx)
		{
			if (!ctx.CheckM("Insert", Origin.Position, TargetLocation))
				return false;
			if (!ctx.LocalSpace.Contains(TargetLocation))
				return false;
			pool.ConflictFreeInsert(Origin, new Entity(new EntityID(Guid.NewGuid(), TargetLocation), Vec3.Zero, directState, SerialLogic
#if STATE_ADV
				,Appearances
#endif
				));
			directState = null;
			return true;
		}

		public override bool Affects(Box cube, ExecutionContext ctx)
		{
			return cube.Contains(TargetLocation);
		}
	}

	[Serializable]
	public class Motion : Instantiation
	{

		public Motion(EntityID origin, Vec3 targetLocation,
#if STATE_ADV
			EntityAppearanceCollection appearance, 
#endif
			EntityLogic logic,byte[] serialLogic) : base(origin, targetLocation,
#if STATE_ADV
				appearance, 
#endif
				logic,serialLogic)
		{}

		//public Motion(Entity e, byte[] newState, EntityAppearanceCollection newAppearance, Vec3 destination) : base(e.ID, destination, newAppearance, newState)
		//{ }


		protected Entity Entity
		{
			get
			{
				var rs = new Entity(Origin.Relocate(TargetLocation), TargetLocation - Origin.Position, directState, SerialLogic,
#if STATE_ADV
					Appearances, null,
#endif
					null);
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
				if (!pool.Find(Origin.Guid, out e))
					return false;
				if (!ctx.CheckM("Motion", TargetLocation, e))
					return false;

				if (ctx.LocalSpace.Contains(TargetLocation))
					pool.ConflictFreeUpdateEntity(Origin, Entity);
				else
					pool.ConflictFreeFindAndRemove(e.ID);
				return true;
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
				pool.ConflictFreeInsert(Origin,Entity);
				return true;
			}

		}

		public override bool Affects(Box cube, ExecutionContext ctx)
		{
			return cube.Contains(TargetLocation) || cube.Contains(Origin.Position);
		}

	}

	[Serializable]
	public abstract class CommonMessage : Abstract
	{
		public readonly int Channel;
		public readonly byte[] Payload;
		public readonly int SentOrderID;

		protected CommonMessage(EntityID origin, int sentOrderID, int channel, byte[] payload) : base(origin)
		{
			Channel = channel;
			SentOrderID = sentOrderID;
			Payload = payload;
		}
		public OrderedEntityMessage MakeMessage(bool isBroadcast)
		{
			return new OrderedEntityMessage(SentOrderID, new EntityMessage(new Actor(Origin), isBroadcast, Channel, Payload));
		}
		public override int CompareTo(object obj)
		{
			var other = obj as CommonMessage;
			if (other == null)
				return 1;
			if (other == this)
				return 0;
			var c = new Helper.Comparator()
					.Append(Origin, other.Origin)
					.Append(SentOrderID, other.SentOrderID)
					.Append(Channel, other.Channel)
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
	}


	[Serializable]
	public class Broadcast : CommonMessage
	{
		public readonly float MaxRange;

		public Broadcast(EntityID origin, int sentOrderID, float maxRange, int channel, byte[] payload) : base(origin,sentOrderID,channel,payload)
		{
			MaxRange = maxRange;
		}

		public OrderedEntityMessage MakeMessage()
		{
			return base.MakeMessage(true);
		}

		public override int CompareTo(object obj)
		{
			if (obj == null)
				return 1;
			if (obj == this)
				return 0;
			var other = obj as Broadcast;
			if (other == null)  //nothing i know
				return base.CompareTo(obj);
			var c = new Helper.Comparator()
					.AppendComparisonResult(base.CompareTo(other))
					.Append(MaxRange, other.MaxRange);
			return c.Finish();
		}

		public override int GetHashCode()
		{
			return Helper.Hash(this)
					.Add(base.GetHashCode())
					.Add(MaxRange)
					.GetHashCode();
		}

		public override bool Execute(EntityPool pool, ExecutionContext ctx)
		{
			pool.BroadcastMessage(Origin.Position, MaxRange, MakeMessage(true));
			return true;
		}

		public override bool Affects(Box cube, ExecutionContext ctx)
		{
			return cube.Intersects(Box.CenterExtent(Origin.Position,Math.Min(MaxRange, ctx.Ranges.Transmission), Bool3.True));
		}
	}

	[Serializable]
	public class Message : CommonMessage
	{
		public readonly Guid TargetEntityID;

		public Message(EntityID origin, int sentOrderID, Guid targetEntityID, int channel, byte[] payload) : base(origin, sentOrderID,channel, payload)
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
					.AppendComparisonResult(base.CompareTo(other))
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

		public override bool Affects(Box cube, ExecutionContext ctx)
		{
			return cube.Intersects(Box.CenterExtent(Origin.Position, ctx.Ranges.Transmission, Bool3.True));
		}
	}


#if STATE_ADV
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
#endif

}
