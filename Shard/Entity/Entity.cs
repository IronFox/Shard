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
	public struct Actor
	{
		public readonly Guid Guid;
		public readonly bool IsEntity;


		public Actor(Guid guid, bool isEntity)
		{
			Guid = guid;
			IsEntity = isEntity;
		}

		public override bool Equals(object obj)
		{
			return obj is Actor && ((Actor)obj) == this;
		}

		public override int GetHashCode()
		{
			return new Helper.HashCombiner(GetType()).Add(Guid).Add(IsEntity).GetHashCode();
		}

		public static bool operator ==(Actor a, Actor b)
		{
			return a.Guid == b.Guid && a.IsEntity == b.IsEntity;
		}
		public static bool operator !=(Actor a, Actor b)
		{
			return !(a == b);
		}

		public static bool operator ==(Actor a, EntityID b)
		{
			return a.Guid == b.Guid && a.IsEntity;
		}
		public static bool operator !=(Actor a, EntityID b)
		{
			return !(a == b);
		}
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
		public readonly Actor Sender;
		public readonly int Channel;
		public readonly byte[] Payload;
		public readonly bool IsBroadcast;

		public EntityMessage(Actor sender, bool broadcast, int channel, byte[] payload)
		{
			Sender = sender;
			Channel = channel;
			Payload = payload;
			IsBroadcast = broadcast;
		}

		public override bool Equals(object obj)
		{
			var other = obj as EntityMessage;
			if (other == null)
				return false;
			return Sender == other.Sender && Channel == other.Channel && Helper.AreEqual(Payload, other.Payload); 
		}

		public override int GetHashCode()
		{
			return Helper.Hash(this).Add(Sender).Add(Channel).Add(Payload).GetHashCode();
		}

		public override string ToString()
		{
			return "Msg:"+Sender+":["+Helper.Length(Payload)+"]";
		}
	}

	public class OrderedEntityMessage : IComparable<OrderedEntityMessage>
	{
		public readonly int OrderID;
		public readonly EntityMessage Message;

		public OrderedEntityMessage(int orderID, EntityMessage message)
		{
			OrderID = orderID;
			Message = message;
		}

		public int CompareTo(OrderedEntityMessage other)
		{
			int order = Message.Sender.Guid.CompareTo(other.Message.Sender.Guid);
			if (order != 0)
				return order;
			return OrderID.CompareTo(other.OrderID);
		}
	}


		

	[Serializable]
	public class Entity : IComparable<Entity>
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

		public class TimeTrace
		{
			private Stopwatch watch;


			private TimeSpan begin, 
							deserialize, 
							evolve, 
							reserialize,
							end;

			public TimeTrace(Stopwatch watch)
			{
				this.watch = watch;
			}

			public void Begin()
			{
				begin = watch.Elapsed;
			}

			public void SignalDeserializationDone()
			{
				deserialize = watch.Elapsed;
			}
			public void SignalEvolutionDone()
			{
				evolve = watch.Elapsed;
			}
			public void SignalReserializationDone()
			{
				reserialize = watch.Elapsed;
			}

			public void End()
			{
				end = watch.Elapsed;
			}

			public override string ToString()
			{
				return 
						begin.TotalMilliseconds+" ms"
						+ "->Des.: "+(deserialize-begin).NotNegative().TotalMilliseconds + " ms"
						+ "->Ev.: " + (evolve - deserialize - begin).NotNegative().TotalMilliseconds + " ms"
						+ "->Res.: " + (reserialize - evolve - deserialize - begin).NotNegative().TotalMilliseconds + " ms"
						+ "->End: " + (end - reserialize - evolve - deserialize - begin).NotNegative().TotalMilliseconds + " ms";
			}
		};

		internal Entity Clone()
		{
			var rs = new Entity(ID, transientDeserializedLogic, SerialLogicState, Appearances, null, null);
			return rs;

		}

		public EntityLogic Evolve(TimeTrace evolutionState, EntityChangeSet outChangeSet, int roundNumber, bool maySendMessages, ICollection<EntityMessage> clientMessages)
		{
			EntityLogic state = null;
			evolutionState.Begin();
			if (Helper.Length(SerialLogicState) > 0)
			{
				try
				{
					state = MyLogic;
					transientDeserializedLogic = null;
					evolutionState.SignalDeserializationDone();
					if (state == null)
						throw new ExecutionException(ID, "Unable to deserialize logic");


					var actions = new EntityLogic.Actions(this);
					state.Execute(ref actions,AddClientMessages(clientMessages), roundNumber, new EntityRandom(this,roundNumber));
					evolutionState.SignalEvolutionDone();

					byte[] serialLogic = Helper.SerializeToArray(state);
					evolutionState.SignalReserializationDone();

					actions.ApplyTo(outChangeSet,state, serialLogic,maySendMessages,roundNumber);
				}
				catch
				{
					outChangeSet.Add(new EntityChange.StateAdvertisement(new EntityContact(ID, Appearances, Vec3.Zero)));
					throw;
				}
			}
			evolutionState.End();
			return state;
		}


		private Entity AddClientMessages(ICollection<EntityMessage> messages)
		{
			if (messages == null || messages.Count == 0)
				return this;
			EntityMessage[] newMessages = Helper.Concat(InboundMessages, messages);
			return new Entity(ID, transientDeserializedLogic,SerialLogicState, Appearances, newMessages, Contacts);
		}

		public readonly EntityID ID;
		public readonly EntityAppearanceCollection Appearances;
		public readonly byte[] SerialLogicState;
		public EntityMessage[] InboundMessages { get; private set; }
		public EntityContact[] Contacts { get; private set; }
		public EntityLogic MyLogic
		{
			get
			{
				if (transientDeserializedLogic != null)
				{
					return transientDeserializedLogic;
				}
				transientDeserializedLogic =(EntityLogic)Helper.Deserialize(SerialLogicState);
				return transientDeserializedLogic;
			}
		}
		[NonSerialized]
		public EntityLogic transientDeserializedLogic;

		public IEnumerable<EntityMessage> EnumInboundEntityMessages(int channel)
		{
			if (InboundMessages != null)
				foreach (var msg in InboundMessages)
					if (msg.Sender.IsEntity && msg.Channel == channel)
						yield return msg;
		}

		public T GetAppearance<T>() where T : EntityAppearance
		{
			if (Appearances != null)
				return Appearances.Get<T>();
			return null;
		}

		public Entity() { }

		public Entity(EntityID id, EntityLogic state, EntityAppearanceCollection appearance = null) : this(id, state, Helper.SerializeToArray(state), appearance, null, null)
		{ }

		public Entity(EntityID id, EntityLogic dstate, byte[] state, EntityAppearanceCollection appearance= null) : this(id, dstate, state, appearance, null, null)
		{ }
		public Entity(EntityID id, EntityLogic dstate, byte[] state, EntityAppearanceCollection appearance, EntityMessage[] messages, EntityContact[] contacts) //: this()
		{
			if (!Simulation.FullSimulationSpace.Contains(id.Position))
				throw new IntegrityViolation("New entity location is located outside simulation space: "+id+", "+Simulation.FullSimulationSpace);
			ID = id;
			SerialLogicState = state;
			transientDeserializedLogic = dstate;
			Appearances = appearance;

			InboundMessages = messages;
			Contacts = contacts;
		}


		internal Entity SetIncoming(EntityMessage[] messages, EntityContact[] contacts)
		{
			this.InboundMessages = messages;
			this.Contacts = contacts;
			return this;
			//return new Entity(ID, transientDeserializedLogic, SerialLogicState, Appearances, messages, contacts);
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
			return (Entity[])Helper.Deserialize(entities);
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
				   Helper.AreEqual(SerialLogicState, other.SerialLogicState) &&
				   Helper.AreEqual(InboundMessages, other.InboundMessages) &&
				   Helper.AreEqual(Contacts, other.Contacts);
		}

		public override int GetHashCode()
		{
			return Helper.Hash(this)
				.Add(ID)
				.Add(Appearances)
				.Add(SerialLogicState)
				.Add(InboundMessages)
				.Add(Contacts)
				.GetHashCode();
		}
	}



}
