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
			public byte[] data;
			public Actor receiver;

			public bool IsDirectedToClient { get { return !receiver.IsEntity; } }


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
			public LazyList<byte[]> broadcasts;
			public LazyList<Message> messages;
			public LazyList<Instantiation> instantiations;
			public LazyList<EntityID> removals;
			public EntityAppearanceCollection newAppearances;

			public NewState(Entity source) : this()
			{
				newPosition = source.ID.Position;
				newAppearances = source.Appearances?.Duplicate();
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

			public void Remove(EntityID entityID)
			{
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

			public void Send(Actor receiver, byte[] data)
			{
				Send(new Message() { receiver = receiver, data = data });
			}
			public void Send(Message message)
			{
				messages.Add(message);
			}
			public void Broadcast(byte[] data)
			{
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
			return await Task.Run(() =>
			{
				EntityRandom random = new EntityRandom(currentState, generation);
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

}