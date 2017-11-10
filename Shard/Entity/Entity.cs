using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	public class EntityFault : Exception
	{
		public EntityFault(Entity entity, string error): base(entity.ID+": "+error)
		{ }

	}

	public struct EntityID : IComparable<EntityID>, IHashable
	{
		public readonly Vec3 Position;
		public readonly Guid Guid;


		public EntityID(Guid guid, Vec3 position)
		{
			Guid = guid;
			Position = position;
		}

		public void Hash(Hasher h)
		{
			h.Add(Position);
			h.Add(Guid);
		}

		public override int GetHashCode()
		{
			return Position.GetHashCode() * 31 + Guid.GetHashCode();
		}

		public override bool Equals(object obj)
		{
			if (!(obj is EntityID))
				return false;
			var other = (EntityID)obj;
			return other == this;
		}

		public static bool operator==(EntityID a, EntityID b)
		{
			return a.Position == b.Position && a.Guid == b.Guid;
		}
		public static bool operator !=(EntityID a, EntityID b) => !(a == b);

		public override string ToString()
		{
			return Guid + " "+Position;
		}

		public int CompareTo(EntityID other)
		{
			int cmp = Position.CompareTo(other.Position);
			if (cmp != 0)
				return cmp;
			return Guid.CompareTo(other.Guid);
		}

		public EntityID Relocate(Vec3 targetLocation)
		{
			return new EntityID(Guid, targetLocation);
		}
	}


	public struct EntityAppearance : IComparable<EntityAppearance>, IHashable
	{
		//placeholder


		public void Hash(Hasher h)
		{
		}

		public int CompareTo(EntityAppearance other)
		{
			return 0;
		}

		public override bool Equals(object obj)
		{
			if (!(obj is EntityAppearance))
			{
				return false;
			}

			var appearance = (EntityAppearance)obj;
			return true;
		}

		public override int GetHashCode()
		{
			var hashCode = -1170177454;
			hashCode = hashCode * -1521134295 + base.GetHashCode();
			return hashCode;
		}

	}

	public struct EntityContact : IComparable<EntityContact>, IHashable
	{
		public readonly EntityID ID;
		public readonly EntityAppearance Appearance;
		public readonly Vec3 Velocity;

		public EntityContact(EntityID id, EntityAppearance appearance, Vec3 velocity)
		{
			ID = id;
			Appearance = appearance;
			Velocity = velocity;
		}

		public void Hash(Hasher h)
		{
			h.Add(ID);
			h.Add(Appearance);
			h.Add(Velocity);
		}

		public int CompareTo(EntityContact other)
		{
			return new Helper.Comparator()
				.Append(ID, other.ID)
				.Append(Appearance, other.Appearance)
				.Append(Velocity, other.Velocity)
				.Finish();
		}

		public override bool Equals(object obj)
		{
			if (!(obj is EntityContact))
			{
				return false;
			}

			var contact = (EntityContact)obj;
			return ID ==  contact.ID &&
				   EqualityComparer<EntityAppearance>.Default.Equals(Appearance, contact.Appearance)
				   && Velocity == contact.Velocity
				   ;
		}

		public override int GetHashCode()
		{
			var hashCode = 2035686911;
			hashCode = hashCode * -1521134295 + base.GetHashCode();
			hashCode = hashCode * -1521134295 + ID.GetHashCode();
			hashCode = hashCode * -1521134295 + Appearance.GetHashCode();
			hashCode = hashCode * -1521134295 + Velocity.GetHashCode();
			return hashCode;
		}
	}


	public class EntityMessage : IHashable
	{
		public readonly EntityID Sender;
		public readonly byte[] Payload;

		public EntityMessage(EntityID sender, byte[] payload)
		{
			Sender = sender;
			Payload = payload;
		}

		public void Hash(Hasher h)
		{
			h.Add(Sender);
			h.Add(Payload);
		}
	}

	public class OrderedEntityMessage
	{
		public readonly int OrderID;
		public readonly EntityMessage Message;

		public OrderedEntityMessage(int orderID, EntityMessage message)
		{
			OrderID = orderID;
			Message = message;
		}
	}


	public abstract class EntityLogic
	{
		public abstract class State : IHashable, IComparable<State>
		{
			public struct Message
			{
				public byte[] data;
				public Guid receiver;
			}

			public struct Changes
			{
				public Vec3 motionVector;
				public byte[][] broadcasts;
				public Message[] messages;

				public State newState;
			}

			public abstract Changes Evolve(Entity currentState);
			public abstract byte[] BinaryState { get; }
			public abstract string LogicID { get; }

			public void Hash(Hasher h)
			{
				h.Add(BinaryState);
			}

			public int CompareTo(State other)
			{
				return new Helper.Comparator()
					.Append(LogicID, other.LogicID)
					.Append(BinaryState, other.BinaryState)
					.Finish();
			}
		}

		public abstract State Instantiate(byte[] binaryState);
	}


	public struct Entity : IHashable, IComparable<Entity>
	{
		public struct Serial
		{
			public EntityID ID;
			public EntityAppearance Appearance;
			public string LogicID;
			public byte[] LogicState;
			public EntityMessage[] InboundMessages;   //must be archived if sent to sibling or DB, not necessary in RCSs
			public EntityContact[] Contacts;

			public Serial(Entity entity) : this()
			{
				ID = entity.ID;
				Appearance = entity.Appearance;
				LogicID = entity.LogicState.LogicID;
				LogicState = entity.LogicState.BinaryState;
				InboundMessages = entity.InboundMessages;
				Contacts = entity.Contacts;
			}

			internal void BeginFetchLogic()
			{
				if (LogicID != null && LogicID.Length > 0)
					DB.BeginFetchLogic(LogicID);
			}
		}

		public void Evolve(EntityChangeSet outChangeSet)
		{
			if (LogicState != null)
			{
				EntityLogic.State.Changes changes = LogicState.Evolve(this);

				int oID = 0;
				if (changes.broadcasts != null)
					foreach (var b in changes.broadcasts)
						outChangeSet.Add(new EntityChangeSet.Broadcast(ID, b, oID++));
				if (changes.messages != null)
					foreach (var m in changes.messages)
						outChangeSet.Add(new EntityChangeSet.Message(ID, oID++, m.receiver, m.data));

				Vec3 dest = ID.Position + changes.motionVector;
				outChangeSet.Add(new EntityChangeSet.Motion(this, changes.newState, dest));	//motion doubles as logic-state-update
				outChangeSet.Add(new EntityChangeSet.StateAdvertisement(new EntityContact(ID.Relocate(dest), Appearance,changes.motionVector)));
			}
		}

		public readonly EntityID ID;
		public readonly EntityAppearance Appearance;
		public readonly EntityLogic.State LogicState;
		public readonly EntityMessage[] InboundMessages;
		public readonly EntityContact[] Contacts;

		public string LogicID
		{
			get
			{
				return LogicState != null ? LogicState.LogicID : null;
			}
		}

		public void Hash(Hasher h)
		{
			h.Add(ID);
			h.Add(Appearance);
			h.Add(LogicState);
			if (InboundMessages != null)
				foreach (var m in InboundMessages)
					h.Add(m);
			if (Contacts != null)
				foreach (var c in Contacts)
					h.Add(c);
		}


		public Entity(Serial entity) : this(entity.ID,entity.LogicID,entity.LogicState,entity.Appearance, entity.InboundMessages,entity.Contacts)
		{}

		//public Entity MoveTo(Vec3 newLocation)
		//{
		//	return new Entity(new EntityID(ID.Guid, newLocation), LogicState, Appearance,InboundMessages,Contacts);
		//}
		public Entity(EntityID id, EntityLogic.State state, EntityAppearance appearance, EntityMessage[] messages, EntityContact[] contacts)
		{
			ID = id;
			LogicState = state;
			Appearance = appearance;

			InboundMessages = messages;
			Contacts = contacts;
		}


		public Entity(EntityID id, string logicID, byte[] logicState,EntityAppearance appearance, EntityMessage[] messages, EntityContact[] contacts)
		{
			ID = id;
			Appearance = appearance;
			Contacts = contacts;
			InboundMessages = messages;

			if (logicID == null || logicID.Length == 0)
				LogicState = null;
			else
			{
				EntityLogic logic = DB.TryGetLogic(logicID);
				if (logic != null)
					LogicState = logic.Instantiate(logicState);
				else
				{
					LogicState = null;
					throw new EntityFault(this,"Failed to retrieve logic '"+logicID+"'");
				}
			}
		}




		public static Entity[] Import(Serial[] entities)
		{
			Entity[] rs = new Entity[entities.Length];
			for (int i = 0; i < entities.Length; i++)
				rs[i] = new Entity(entities[i]);
			return rs;
		}
		public static Serial[] Export(Entity[] entities)
		{
			Serial[] rs = new Serial[entities.Length];
			for (int i = 0; i < entities.Length; i++)
				rs[i] = new Serial(entities[i]);
			return rs;
		}

		internal Entity SetIncoming(EntityMessage[] messages, EntityContact[] contacts)
		{
			return new Entity(ID, LogicState, Appearance, messages, contacts);
		}

		public int CompareTo(Entity other)
		{
			return new Helper.Comparator()
					.Append(ID, other.ID)
					.Append(Appearance, other.Appearance)
					.Append(LogicState, other.LogicState)
					.Finish();
		}
	}



}
