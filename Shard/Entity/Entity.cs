using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	public class EntityFault : Exception
	{
		public EntityFault(Entity entity, string error): base(entity.ID+": "+error)
		{ }

	}

	[Serializable]
	public struct EntityID : IComparable<EntityID>
	{
		public readonly Vec3 Position;
		public readonly Guid Guid;


		public EntityID(Guid guid, Vec3 position)
		{
			Guid = guid;
			Position = position;
		}

		public EntityID(Vec3 position) : this(Guid.NewGuid(), position)
		{ }


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
			return Guid.ToString().Substring(0,13) + " "+Position;
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

	[Serializable]
	public abstract class EntityAppearance : IComparable<EntityAppearance>
	{
		public abstract int CompareTo(EntityAppearance other);
		public override bool Equals(object obj)
		{
			EntityAppearance other = obj as EntityAppearance;
			return other != null && CompareTo(other) == 0;
		}
		public override abstract int GetHashCode();
	}

	[Serializable]
	public class EntityAppearanceCollection : IComparable<EntityAppearanceCollection>, ISerializable, IEnumerable<EntityAppearance>
	{
		private SortedList<Type, EntityAppearance> members = new SortedList<Type, EntityAppearance>();

		public void Add(EntityAppearance app)
		{
			Type t = app.GetType();
			if (!t.IsSerializable)
				throw new IntegrityViolation("Trying to add non-serializable appearance to collection: "+t);
			if (Contains(t))
				throw new IntegrityViolation("This appearance already exists in this collection");
			members.Add(t, app);
		}
		public void AddOrReplace(EntityAppearance app)
		{
			Type t = app.GetType();
			if (!t.IsSerializable)
				throw new IntegrityViolation("Trying to add non-serializable appearance to collection: " + t);
			members[app.GetType()] = app;
		}

		public bool Remove(Type t)
		{
			return members.Remove(t);
		}

		public bool Remove<T>()
		{
			return members.Remove(typeof(T));
		}

		public void Clear()
		{
			members.Clear();
		}


		public int CompareTo(EntityAppearanceCollection other)
		{
			var h = new Helper.Comparator();
			h.Append(members.Values, other.members.Values);
			return h.Finish();
		}

		public bool Contains(Type appearanceType)
		{
			return members.ContainsKey(appearanceType);
		}

		public bool Contains<T>() where T : EntityAppearance
		{
			return members.ContainsKey(typeof(T));
		}

		public T Get<T>() where T: EntityAppearance
		{
			EntityAppearance rs;
			if (!members.TryGetValue(typeof(T), out rs))
				return null;
			return (T)rs;
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("members", Helper.ToArray(members.Values));
		}

		public EntityAppearanceCollection()
		{ }

		public EntityAppearanceCollection(SerializationInfo info, StreamingContext context)
		{
			EntityAppearance[] field = (EntityAppearance[])info.GetValue("members", typeof(EntityAppearance[]));
			foreach (var a in field)
				Add(a);
		}


		public override string ToString()
		{
			if (members.Count == 0)
				return "{}";
			if (members.Count == 1)
				return members.Values[0].ToString();

			StringBuilder builder = new StringBuilder();


			foreach (var a in members.Values)
			{
				if (builder.Length != 0)
					builder.Append(',');
				builder.Append(a.ToString());
			}

			return "{"+builder.ToString()+"}";
		}

		public override bool Equals(object obj)
		{
			var other = obj as EntityAppearanceCollection;
			return other != null && CompareTo(other) == 0;
		}

		public override int GetHashCode()
		{
			var h = Helper.Hash(this);
			foreach (var app in members.Values)
				h.Add(app);
			return h.GetHashCode();
		}

		public IEnumerator<EntityAppearance> GetEnumerator()
		{
			return members.Values.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return members.Values.GetEnumerator();
		}

		public EntityAppearanceCollection Duplicate()
		{
			return (EntityAppearanceCollection)Helper.Deserialize(Helper.SerializeToArray(this));
		}
	}

	[Serializable]
	public struct EntityContact : IComparable<EntityContact>
	{
		public readonly EntityID ID;
		public readonly EntityAppearanceCollection Appearances;
		public readonly Vec3 Velocity;

		public EntityContact(EntityID id, EntityAppearanceCollection appearances, Vec3 velocity)
		{
			ID = id;
			Appearances = appearances;
			Velocity = velocity;
		}


		public int CompareTo(EntityContact other)
		{
			return new Helper.Comparator()
				.Append(ID, other.ID)
				.Append(Appearances, other.Appearances)
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
				   Equals(Appearances, contact.Appearances)
				   && Velocity == contact.Velocity
				   ;
		}

		public override int GetHashCode()
		{
			var hashCode = 2035686911;
			hashCode = hashCode * -1521134295 + ID.GetHashCode();
			hashCode = hashCode * -1521134295 + Appearances.GetHashCode();
			hashCode = hashCode * -1521134295 + Velocity.GetHashCode();
			return hashCode;
		}
	}


	[Serializable]
	public class EntityMessage
	{
		public readonly EntityID Sender;
		public readonly byte[] Payload;

		public EntityMessage(EntityID sender, byte[] payload)
		{
			Sender = sender;
			Payload = payload;
		}

		public override bool Equals(object obj)
		{
			var other = obj as EntityMessage;
			if (other == null)
				return false;
			return Sender == other.Sender && Helper.AreEqual(Payload, other.Payload); 
		}

		public override int GetHashCode()
		{
			return Helper.Hash(this).Add(Sender).Add(Payload).GetHashCode();
		}

		public override string ToString()
		{
			return "Msg:"+Sender+":["+Helper.Length(Payload)+"]";
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

	/// <summary>
	/// Abstract entity behavior descriptor.
	/// Actual behavior is implemented in Evolve()
	/// </summary>
	[Serializable]
	public abstract class EntityLogic
	{
		public struct Message
		{
			public byte[] data;
			public Guid receiver;
		}

		public struct Instantiation
		{
			public Vec3 targetLocation;
			public EntityAppearanceCollection appearances;
			public EntityLogic logic;
		}

		public struct NewState
		{
			public Vec3 newPosition;
			public List<byte[]> broadcasts;
			public List<Message> messages;
			public List<Instantiation> instantiations;
			public List<EntityID> removals;
			public EntityLogic newLogic;
			public EntityAppearanceCollection newAppearances;

			public NewState(Entity source)
			{
				newPosition = source.ID.Position;
				broadcasts = null;
				messages = null;
				instantiations = null;
				removals = null;
				newLogic = source.LogicState;
				newAppearances = source.Appearances?.Duplicate();
			}


			public void Instantiate(Vec3 targetLocation, EntityLogic logic, EntityAppearanceCollection appearances)
			{
				if (instantiations == null)
					instantiations = new List<Instantiation>();
				instantiations.Add( new Instantiation()
				{
					appearances = appearances,
					logic = logic,
					targetLocation = targetLocation
				});
			}

			public void Remove(EntityID entityID)
			{
				if (removals == null)
					removals = new List<EntityID>();
				removals.Add(entityID);
			}

			public void Add(EntityAppearance app)
			{
				if (newAppearances == null)
					newAppearances = new EntityAppearanceCollection();
				newAppearances.Add(app);
			}

			public void AddOrReplace(EntityAppearance app)
			{
				if (newAppearances == null)
					newAppearances = new EntityAppearanceCollection();
				newAppearances.AddOrReplace(app);
			}

			public void Send(Message message)
			{
				if (messages == null)
					messages = new List<Message>();
				messages.Add(message);
			}
			public void Broadcast(byte[] data)
			{
				if (broadcasts == null)
					broadcasts = new List<byte[]>();
				broadcasts.Add(data);
			}
		}


		/// <summary>
		/// Creates an asynchronous task that computes the next state in a separate thread
		/// </summary>
		/// <param name="currentState">Current entity state</param>
		/// <param name="generation">Evolution generation index, starting from 0</param>
		/// <param name="randomSource">Source for random values used during execution</param>
		/// <returns></returns>
		public async Task<NewState> EvolveAsync(Entity currentState, int generation)
		{
			return await Task.Run( () =>
			{
				EntityRandom random = new EntityRandom(currentState,generation);
				NewState newState = new NewState(currentState);
				Evolve(ref newState, currentState, generation, random);
				return newState;
			});
		}

		/// <summary>
		/// Evolves the local state, potentially generating some modifications to the base entity.
		/// The method must not change any local variables relevant to evolution. All entity modifications are limited to changes in.
		/// Evolution must be deterministic.
		/// <paramref name="newState"/>.
		/// </summary>
		/// <param name="newState">Modifications go here</param>
		/// <param name="currentState">Current entity state</param>
		/// <param name="generation">Evolution generation index, starting from 0</param>
		/// <param name="randomSource">Random source to be used exclusively for random values</param>
		public abstract void Evolve(ref NewState newState, Entity currentState, int generation, EntityRandom randomSource);
	}
		

	[Serializable]
	public struct Entity : IComparable<Entity>
	{
	
		public int FindContact(EntityID id)
		{
			if (Contacts == null)
				return -1;
			for (int i = 0; i < Contacts.Length; i++)
				if (Contacts[i].ID == id)
					return i;
			return -1;
		}
		public int FindContact(Guid id)
		{
			if (Contacts == null)
				return -1;
			for (int i = 0; i < Contacts.Length; i++)
				if (Contacts[i].ID.Guid == id)
					return i;
			return -1;
		}

		public bool HasContact(EntityID id)
		{
			return FindContact(id) != -1;
		}

		public bool HasContact(Guid guid)
		{
			return FindContact(guid) != -1;
		}

		public override string ToString()
		{
			return "Entity " + ID;
		}

		public async Task EvolveAsync(EntityChangeSet outChangeSet, int roundNumber)
		{
			if (LogicState != null)
			{
				try
				{
					var state = LogicState;
					Entity copy = this;

					var newState = await state.EvolveAsync(copy, roundNumber);
					int oID = 0;
					if (newState.broadcasts != null)
						foreach (var b in newState.broadcasts)
							outChangeSet.Add(new EntityChange.Broadcast(ID, b, oID++));
					if (newState.messages != null)
						foreach (var m in newState.messages)
							outChangeSet.Add(new EntityChange.Message(ID, oID++, m.receiver, m.data));

					Vec3 dest = Simulation.ClampDestination("Motion", newState.newPosition, ID, Simulation.M);
					var newID = ID.Relocate(dest);
					outChangeSet.Add(new EntityChange.Motion(this, newState.newLogic, newState.newAppearances, dest)); //motion doubles as logic-state-update
					outChangeSet.Add(new EntityChange.StateAdvertisement(new EntityContact(newID, newState.newAppearances, dest - ID.Position)));
					if (newState.instantiations != null)
						foreach (var inst in newState.instantiations)
							outChangeSet.Add(new EntityChange.Instantiation(newID, Simulation.ClampDestination("Instantiation", inst.targetLocation, newID, Simulation.M), inst.appearances, inst.logic));
					if (newState.removals != null)
						foreach (var rem in newState.removals)
						{
							if (Simulation.CheckDistance("Removal", rem.Position, newID, Simulation.M))
								outChangeSet.Add(new EntityChange.Removal(newID, rem));
						}
					int messageID = 0;
					if (newState.messages != null)
						foreach (var m in newState.messages)
							outChangeSet.Add(new EntityChange.Message(ID,messageID++,m.receiver,m.data));
					if (newState.broadcasts != null)
						foreach (var b in newState.broadcasts)
							outChangeSet.Add(new EntityChange.Broadcast(ID, b, messageID++));
				}
				catch
				{
					outChangeSet.Add(new EntityChange.StateAdvertisement(new EntityContact(ID, Appearances, Vec3.Zero)));
					throw;
				}
			}
		}

		public readonly EntityID ID;
		public readonly EntityAppearanceCollection Appearances;
		public readonly EntityLogic LogicState;
		public readonly EntityMessage[] InboundMessages;
		public readonly EntityContact[] Contacts;

		public T GetAppearance<T>() where T : EntityAppearance
		{
			if (Appearances != null)
				return Appearances.Get<T>();
			return null;
		}


		public Entity(EntityID id, EntityLogic state, EntityAppearanceCollection appearance, EntityMessage[] messages, EntityContact[] contacts)
		{
			if (!Simulation.FullSimulationSpace.Contains(id.Position))
				throw new IntegrityViolation("New entity location is located outside simulation space: "+id+", "+Simulation.FullSimulationSpace);
			ID = id;
			LogicState = state;
			Appearances = appearance;

			InboundMessages = messages;
			Contacts = contacts;
		}


		internal Entity SetIncoming(EntityMessage[] messages, EntityContact[] contacts)
		{
			return new Entity(ID, LogicState, Appearances, messages, contacts);
		}

		public int CompareTo(Entity other)
		{
			return new Helper.Comparator()
					.Append(ID, other.ID)
					.Append(Appearances, other.Appearances)
					//.Append(LogicState, other.LogicState)
					.Finish();
		}

		public static Entity[] Import(byte[] entities)
		{
			var f = new BinaryFormatter();
			using (var ms = new MemoryStream(entities))
			{
				return (Entity[])f.Deserialize(ms);
			}
		}

		public static byte[] Export(Entity[] entities)
		{
			var f = new BinaryFormatter();
			using (var ms = new MemoryStream())
			{
				f.Serialize(ms, entities);
				return ms.ToArray();
			}
		}

		public override bool Equals(object obj)
		{
			if (!(obj is Entity))
			{
				return false;
			}

			var other = (Entity)obj;
			return ID == other.ID &&
				   Equals(Appearances, other.Appearances) &&
				   Equals(LogicState, other.LogicState) &&
				   Helper.AreEqual(InboundMessages, other.InboundMessages) &&
				   Helper.AreEqual(Contacts, other.Contacts);
		}

		public override int GetHashCode()
		{
			return Helper.Hash(this)
				.Add(ID)
				.Add(Appearances)
				.Add(LogicState)
				.Add(InboundMessages)
				.Add(Contacts)
				.GetHashCode();
		}
	}



}
