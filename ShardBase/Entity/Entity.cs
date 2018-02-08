using Newtonsoft.Json;
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

#if STATE_ADV
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
#endif

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
			var rs = new Entity(ID, Velocity,transientDeserializedLogic, SerialLogicState,
#if STATE_ADV
				Appearances, null, 
#endif
				null);
			return rs;

		}

		public EntityLogic Evolve(TimeTrace evolutionState, EntityChangeSet outChangeSet, ICollection<EntityMessage> clientMessages, EntityChange.ExecutionContext ctx, bool locationIsInconsistent)
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
					state.Execute(ref actions,AddClientMessages(clientMessages), ctx.GenerationNumber, new EntityRandom(this, ctx.GenerationNumber),ctx.Ranges, locationIsInconsistent);
					evolutionState.SignalEvolutionDone();

					byte[] serialLogic = Helper.SerializeToArray(state);
					evolutionState.SignalReserializationDone();

					actions.ApplyTo(outChangeSet, state, serialLogic, ctx);
				}
				catch
				{
#if STATE_ADV
					outChangeSet.Add(new EntityChange.StateAdvertisement(new EntityContact(ID, Appearances, Vec3.Zero)));
#endif
					transientDeserializedLogic = null;
					throw;
				}
			}
			else
				transientDeserializedLogic = null;

			evolutionState.End();
			return state;
		}


		private Entity AddClientMessages(ICollection<EntityMessage> messages)
		{
			if (messages == null || messages.Count == 0)
				return this;
			EntityMessage[] newMessages = Helper.Concat(InboundMessages, messages);
			return new Entity(ID, Velocity,transientDeserializedLogic,SerialLogicState,
#if STATE_ADV
				Appearances, Contacts, 
#endif
				newMessages);
		}

		public readonly EntityID ID;
		/// <summary>
		/// Motion during the last evolution
		/// </summary>
		public readonly Vec3 Velocity;
#if STATE_ADV
		public readonly EntityAppearanceCollection Appearances;
#endif
		[JsonIgnore]
		public readonly byte[] SerialLogicState;
		public EntityMessage[] InboundMessages { get; private set; }
#if STATE_ADV
		public EntityContact[] Contacts { get; private set; }
#endif
		public EntityLogic MyLogic
		{
			get
			{
				if (transientDeserializedLogic != null)
					return transientDeserializedLogic;
				if (SerialLogicState == null)
					return null;
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

#if STATE_ADV
		public T GetAppearance<T>() where T : EntityAppearance
		{
			if (Appearances != null)
				return Appearances.Get<T>();
			return null;
		}
#endif

		public Entity() { }

		public Entity(EntityID id, Vec3 velocity, EntityLogic state
#if STATE_ADV
			, EntityAppearanceCollection appearance = null
#endif
			) : this(id, velocity, state, Helper.SerializeToArray(state),
#if STATE_ADV
				appearance,null, 
#endif
				null)
		{ }

		public Entity(EntityID id, Vec3 velocity, EntityLogic dstate, byte[] state
#if STATE_ADV
			, EntityAppearanceCollection appearance= null
#endif
			) : this(id, velocity,dstate, state,
#if STATE_ADV
				appearance, null, 
#endif
				null)
		{ }
		public Entity(EntityID id, Vec3 velocity, EntityLogic dstate, byte[] state
#if STATE_ADV
			, EntityAppearanceCollection appearance, EntityContact[] contacts
#endif
			, EntityMessage[] messages) //: this()
		{
			//if (!Simulation.FullSimulationSpace.Contains(id.Position))
			//	throw new IntegrityViolation("New entity location is located outside simulation space: "+id+", "+Simulation.FullSimulationSpace);
			ID = id;
			Velocity = velocity;
			SerialLogicState = state ?? Helper.SerializeToArray(dstate);
			transientDeserializedLogic = dstate;
#if STATE_ADV
			Appearances = appearance;
			Contacts = contacts;
#endif

			InboundMessages = messages;
		}


		internal Entity SetIncoming(EntityMessage[] messages
#if STATE_ADV
			, EntityContact[] contacts
#endif
			)
		{
			this.InboundMessages = messages;
#if STATE_ADV
			this.Contacts = contacts;
#endif
			return this;
			//return new Entity(ID, transientDeserializedLogic, SerialLogicState, Appearances, messages, contacts);
		}

		public int CompareTo(Entity other)
		{
			return new Helper.Comparator()
					.Append(ID, other.ID)
#if STATE_ADV
					.Append(Appearances, other.Appearances)
#endif
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
#if STATE_ADV
				   Equals(Appearances, other.Appearances) &&
				   Helper.AreEqual(Contacts, other.Contacts) &&
#endif
				   Helper.AreEqual(SerialLogicState, other.SerialLogicState) &&
				   Helper.AreEqual(InboundMessages, other.InboundMessages);
		}

		public override int GetHashCode()
		{
			return Helper.Hash(this)
				.Add(ID)
#if STATE_ADV
				.Add(Appearances)
				.Add(Contacts)
#endif
				.Add(SerialLogicState)
				.Add(InboundMessages)
				.GetHashCode();
		}
	}



}
