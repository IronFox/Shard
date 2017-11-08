using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{

	public struct EntityID : IComparable<EntityID>
	{
		public readonly Vec3 Position;
		public readonly Guid Guid;


		public EntityID(Guid guid, Vec3 position)
		{
			Guid = guid;
			Position = position;
		}

		public void AddTo(Hasher h)
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
	}


	public struct EntityAppearance
	{
		public readonly Vec3 Velocity;

		public EntityAppearance(Vec3 velocity) : this()
		{
			Velocity = velocity;
		}

		public void AddTo(Hasher h)
		{
			h.Add(Velocity);
		}

		public EntityAppearance MoveBy(Vec3 delta)
		{
			return new EntityAppearance(delta);
		}
	}

	public struct EntityContact
	{
		public readonly EntityID ID;
		public readonly EntityAppearance Appearance;

		public EntityContact(EntityID id, EntityAppearance appearance)
		{
			ID = id;
			Appearance = appearance;
		}

		public void AddTo(Hasher h)
		{
			ID.AddTo(h);
			Appearance.AddTo(h);
		}
	}


	public class EntityMessage
	{
		public readonly EntityID Sender;
		public readonly byte[] Payload;

		public EntityMessage(EntityID sender, byte[] payload)
		{
			Sender = sender;
			Payload = payload;
		}

		internal void AddTo(Hasher h)
		{
			Sender.AddTo(h);
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
		public abstract class State
		{
			public abstract State Evolve(Entity inEntity, EntityChangeSet outChanges, out Vec3 motionVector);
			public abstract byte[] BinaryState { get; }

			public void AddTo(Hasher h)
			{
				h.Add(BinaryState);
			}
		}

		public abstract State Instantiate(byte[] binaryState);
	}


	public struct Entity
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
				LogicID = entity.LogicID;
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




		public readonly EntityID ID;
		public readonly EntityAppearance Appearance;
		public readonly EntityLogic.State LogicState;
		public readonly bool IsInconsistent;
		public readonly string LogicID;
		public readonly EntityMessage[] InboundMessages;
		public readonly EntityContact[] Contacts;

		public void AddTo(Hasher h)
		{
			ID.AddTo(h);
			Appearance.AddTo(h);
			if (LogicState != null)
				LogicState.AddTo(h);
			h.Add(IsInconsistent);
			h.Add(LogicID);
			if (InboundMessages != null)
				foreach (var m in InboundMessages)
					m.AddTo(h);
			if (Contacts != null)
				foreach (var c in Contacts)
					c.AddTo(h);
		}


		public Entity(Serial entity) : this(entity.ID,entity.LogicID,entity.LogicState,false,entity.Appearance, entity.InboundMessages,entity.Contacts)
		{}

		public Entity MoveTo(Vec3 newLocation)
		{
			return new Entity(new EntityID(ID.Guid, newLocation), LogicID, LogicState, IsInconsistent, Appearance.MoveBy(newLocation - ID.Position),InboundMessages,Contacts);
		}
		private Entity(EntityID id, string logicID, EntityLogic.State state, bool isInconsistent, EntityAppearance appearance, EntityMessage[] messages, EntityContact[] contacts)
		{
			ID = id;
			LogicID = logicID;
			IsInconsistent = isInconsistent;
			LogicState = state;
			Appearance = appearance;

			InboundMessages = messages;
			Contacts = contacts;
		}


		public Entity(EntityID id, string logicID, byte[] logicState,bool isInconsistent, EntityAppearance appearance, EntityMessage[] messages, EntityContact[] contacts)
		{
			ID = id;
			LogicID = logicID;
			IsInconsistent = isInconsistent;
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
					IsInconsistent = true;
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
			return new Entity(ID, LogicID, LogicState, IsInconsistent, Appearance, messages, contacts);
		}
	}

	public class EntityPool
	{
		private class Container
		{
			public class BySender
			{
				ConcurrentDictionary<int, EntityMessage> inMessages = new ConcurrentDictionary<int, EntityMessage>();


				public bool Add(OrderedEntityMessage msg)
				{
					return inMessages.TryAdd(msg.OrderID, msg.Message);
				}

				public void AddTo(List<EntityMessage> messages)
				{
					var ar = inMessages.ToArray();
					Array.Sort(ar, (a, b) => a.Key.CompareTo(b.Key));
					foreach (var p in ar)
						messages.Add(p.Value);
				}

			}

			public Container(Entity e)
			{
				entity = e;
			}
			public Entity entity;
			public ConcurrentDictionary<Guid, BySender> messages = new ConcurrentDictionary<Guid, BySender>();
			public ConcurrentDictionary<Guid, EntityContact> contacts = new ConcurrentDictionary<Guid, EntityContact>();
		}

		private ConcurrentDictionary<EntityID, Container> fullMap = new ConcurrentDictionary<EntityID, Container>();
		private ConcurrentDictionary<Guid, Container> guidMap = new ConcurrentDictionary<Guid, Container>();

		public bool Find(EntityID id, out Entity e)
		{
			Container rs;
			if (fullMap.TryGetValue(id, out rs))
			{
				e = rs.entity;
				return true;
			}
			e = new Entity();
			return false;
		}
		public bool Find(Guid id, out Entity e)
		{
			Container rs;
			if (guidMap.TryGetValue(id, out rs))
			{
				e = rs.entity;
				return true;
			}
			e = new Entity();
			return false;
		}
		public bool AddContact(EntityContact c)
		{
			foreach (var p in fullMap)
			{
				if (Simulation.GetDistance(c.ID.Position, p.Key.Position) <= Simulation.M)
				{
					if (!p.Value.contacts.TryAdd(c.ID.Guid, c))
					{
						EntityContact existing;
						if (!p.Value.contacts.TryGetValue(c.ID.Guid, out existing))
							throw new Exception("Should have been able to query existing contact "+c);
						if (c.CompareTo(exi))


					}
				}
						messages.GetOrAdd(message.Message.Sender.Guid, guid => new Container.BySender()).Add(message);
			}
		}


		public bool RelayMessage(Vec3 senderPosition, Guid receiver, OrderedEntityMessage message)
		{
			Container rs;
			if (!guidMap.TryGetValue(receiver, out rs))
				return false;
			if (!Simulation.CheckDistance("Message", senderPosition, rs.entity, Simulation.M))
				return false;
			return rs.messages.GetOrAdd(message.Message.Sender.Guid, guid => new Container.BySender()).Add(message);
		}

		public void BroadcastMessage(Vec3 senderPosition, OrderedEntityMessage message)
		{
			foreach (var p in fullMap)
			{
				if (Simulation.GetDistance(senderPosition,p.Key.Position) <= Simulation.M)
					p.Value.messages.GetOrAdd(message.Message.Sender.Guid, guid => new Container.BySender()).Add(message);
			}
		}


		public void DispatchAll()
		{
			Parallel.ForEach(fullMap.Values, ctr => 
			{
				List<EntityMessage> messages = new List<EntityMessage>();
				var ar = ctr.messages.ToArray();
				Array.Sort(ar,(a,b) => a.Key.CompareTo(b.Key));
				foreach (var p in ar)
					p.Value.AddTo(messages);

				EntityContact[] ctx = Helper.ToArray(ctr.contacts.Values);
				Array.Sort(ctx, (a, b) => a.ID.CompareTo(b.ID));

				ctr.entity = ctr.entity.SetIncoming(messages.ToArray(), ctx);
			}
			);
		}


		private bool FindAndRemove(Container ctr)
		{
			Container other;
			if (!fullMap.TryRemove(ctr.entity.ID, out other))
				return false;
			if (other != ctr)
				throw new Exception("Removed container does not equal provided container");

			return true;
		}
		public bool FindAndRemove(EntityID id)
		{
			Container ctr;
			return fullMap.TryRemove(id, out ctr);
		}

		public bool FindAndRemove(EntityID id, Func<Entity, bool> verifyMatch)
		{
			Container ctr;
			if (!fullMap.TryRemove(id, out ctr))
				return false;
			if (!verifyMatch(ctr.entity))
			{
				if (!fullMap.TryAdd(id, ctr))
					throw new Exception("Failed to reinsert entity "+id);
				return false;
			}
			return true;
		}

		public bool Insert(Entity entity)
		{
			Container ctr = new Container(entity);
			return fullMap.TryAdd(entity.ID, ctr);
		}

		public bool UpdateEntity(Entity original, Entity updated)
		{
			Container ctr;
			if (!fullMap.TryRemove(original.ID, out ctr))
				return false;
			ctr.entity = updated;
			return fullMap.TryAdd(updated.ID, ctr);
		}
	}


	public class EntityChangeSet
	{
		public readonly int Generation;

		public EntityChangeSet(int generation)
		{
			Generation = generation;
		}

		public abstract class Change
		{
			public readonly EntityID Origin;

			protected Change(EntityID origin)
			{
				Origin = origin;
			}

			public abstract bool Execute(EntityPool pool);
		}

		public class Removal : Change
		{
			public readonly EntityID Target;

			public Removal(EntityID origin, EntityID target) : base(origin)
			{
				Target = target;
			}

			public override bool Execute(EntityPool pool)
			{
				return pool.FindAndRemove(Target, e => Simulation.CheckDistance("Removal", Target.Position, e, Simulation.M));
			}
		}

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

			public override bool Execute(EntityPool pool)
			{
				if (!Simulation.CheckDistance("Insert", Origin.Position, TargetLocation, Simulation.M))
					return false;
				return pool.Insert(new Entity(new EntityID(Guid.NewGuid(), TargetLocation), LogicID, LogicState, false, Appearance, null, null));
			}
		}

		public class Motion : Instantiation
		{
			public readonly bool IsInconsistent;

			public Motion(EntityID origin, Vec3 targetLocation, EntityAppearance appearance, string logicID, byte[] logicState, bool isInconsistent) : base(origin, targetLocation, appearance, logicID, logicState)
			{
				IsInconsistent = isInconsistent;
			}

			public Motion(Entity e, Vec3 destination) : base(e.ID, destination, e.Appearance, e.LogicID, e.LogicState.BinaryState)
			{
				IsInconsistent = e.IsInconsistent;
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
						return pool.UpdateEntity(e, e.MoveTo(TargetLocation));
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
					return pool.Insert(new Entity(new EntityID(Origin.Guid, TargetLocation), LogicID, LogicState, IsInconsistent, Appearance.MoveBy(TargetLocation - Origin.Position), null, null));
				}

			}
		}


		public class Broadcast : Change
		{
			public readonly byte[] Payload;
			public readonly int SentOrderID;

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

			public override bool Execute(EntityPool pool)
			{
				pool.BroadcastMessage(Origin.Position, Message);
				return true;
			}
		}

		public class Message : Broadcast
		{
			public readonly Guid TargetEntityID;

			protected Message(EntityID origin, int sentOrderID, Guid targetEntityID, byte[] payload) : base(origin, sentOrderID, payload)
			{
				TargetEntityID = targetEntityID;
			}

			public override bool Execute(EntityPool pool)
			{
				return pool.RelayMessage(Origin.Position, TargetEntityID, Message);
			}
		}


		public class StateAdvertisement : Change
		{
			public readonly EntityAppearance Appearance;


			public override bool Execute(EntityPool pool)
			{
				pool.
			}


		}

}
