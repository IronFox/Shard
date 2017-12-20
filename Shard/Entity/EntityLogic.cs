using System;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	/// <summary>
	/// Abstract entity behavior descriptor.
	/// Actual behavior is implemented in Evolve()
	/// </summary>
	[Serializable]
	public abstract class EntityLogic
	{
		public struct Message
		{
			public Actor receiver;
			public int channel;
			public byte[] data;

			public bool IsDirectedToClient { get { return !receiver.IsEntity; } }
		}

		public struct Broadcast
		{
			public int channel;
			public byte[] data;
		}

		public struct Instantiation
		{
			public Vec3 targetLocation;
			public EntityAppearanceCollection appearances;
			public EntityLogic logic;
		}

		public struct Actions
		{
			private readonly EntityID id;
			public Vec3 NewPosition { get; set; }
			private LazyList<Broadcast> broadcasts;
			private LazyList<Message> messages;
			private LazyList<Instantiation> instantiations;
			private LazyList<EntityID> removals;
			private EntityAppearanceCollection newAppearances;

			public Actions(Entity source) : this()
			{
				id = source.ID;
				NewPosition = source.ID.Position;
				newAppearances = source.Appearances?.Duplicate();
			}

			public void ReplaceInstantiations(Func<Instantiation, Instantiation> replacer)
			{
				for (int i = 0; i < instantiations.Count; i++)
					instantiations[i] = replacer(instantiations[i]);
			}

			public void ApplyTo(EntityChangeSet outChangeSet, byte[] serialLogic, bool maySendMessages, int roundNumber)
			{
				Vec3 dest = Simulation.ClampDestination("Motion", NewPosition, id, Simulation.M);
				var newID = id.Relocate(dest);
				outChangeSet.Add(new EntityChange.Motion(id, dest, newAppearances, serialLogic)); //motion doubles as logic-state-update
				outChangeSet.Add(new EntityChange.StateAdvertisement(new EntityContact(newID, newAppearances, dest - id.Position)));
				foreach (var inst in instantiations)
					outChangeSet.Add(new EntityChange.Instantiation(newID, Simulation.ClampDestination("Instantiation", inst.targetLocation, newID, Simulation.M), inst.appearances, Helper.SerializeToArray(inst.logic)));
				foreach (var rem in removals)
				{
					if (Simulation.CheckDistance("Removal", rem.Position, newID, Simulation.M))
						outChangeSet.Add(new EntityChange.Removal(newID, rem));
				}
				int messageID = 0;
				foreach (var m in messages)
				{
					if (m.IsDirectedToClient)
					{
						if (maySendMessages)
							InteractionLink.Relay(id.Guid, m.receiver.Guid, m.channel, m.data, roundNumber);
					}
					else
						outChangeSet.Add(new EntityChange.Message(id, messageID++, m.receiver.Guid, m.channel, m.data));
				}
				foreach (var b in broadcasts)
					outChangeSet.Add(new EntityChange.Broadcast(id, messageID++, b.channel, b.data));
			}


			public void Instantiate(Vec3 targetLocation, EntityLogic logic, EntityAppearanceCollection appearances)
			{
				instantiations.Add(new Instantiation()
				{
					appearances = appearances,
					logic = logic,
					targetLocation = targetLocation
				});
			}
			public void Instantiate(Vec3 targetLocation, string assemblyName, string logicName, object[] constructorParameters, EntityAppearanceCollection appearances)
			{
				Instantiate(targetLocation, new DynamicCSLogic(assemblyName, logicName, constructorParameters), appearances);
			}

			public void Kill(EntityID entityID)
			{
				removals.Add(entityID);
			}

			public void Add(EntityAppearance app)
			{
				if (newAppearances == null)
					newAppearances = new EntityAppearanceCollection();
				newAppearances.Add(app);
			}

			public T GetAppearance<T>() where T: EntityAppearance
			{
				return newAppearances?.Get<T>();
			}

			public void AddOrReplace(EntityAppearance app)
			{
				if (newAppearances == null)
					newAppearances = new EntityAppearanceCollection();
				newAppearances.AddOrReplace(app);
			}

			public void Send(Actor receiver, int channel, byte[] data)
			{
				Send(new Message() { receiver = receiver, channel = channel, data = data });
			}
			public void Send(Message message)
			{
				messages.Add(message);
			}
			public void Broadcast(int channel, byte[] data)
			{
				broadcasts.Add(new Broadcast() { channel = channel, data = data });
			}
		}


		/// <summary>
		/// Creates an asynchronous task that computes the next state in a separate thread
		/// </summary>
		/// <param name="currentState">Current entity state</param>
		/// <param name="generation">Evolution generation index, starting from 0</param>
		/// <param name="randomSource">Source for random values used during execution</param>
		/// <returns></returns>
		public async Task<Actions> EvolveAsync(Entity currentState, int generation)
		{
			return await Task.Run(() =>
			{
				EntityRandom random = new EntityRandom(currentState, generation);
				Actions newState = new Actions(currentState);
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
		public abstract void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource);
	}

}