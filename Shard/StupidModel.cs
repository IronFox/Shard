using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{

	[Serializable]
	public class Animal
	{
		public float Size;

		public Animal(float initialSize)
		{
			Size = initialSize;
		}

		public Animal(byte[] payload) : this(BitConverter.ToSingle(payload, 0))
		{ }

		public byte[] Export()
		{
			return BitConverter.GetBytes(Size);
		}
	}

	[Serializable]
	public class GridLogic
	{
		Animal animal;

		readonly int channel;

		public GridLogic(int channel, float animalSize)
		{
			this.channel = channel;
			if (animalSize > 0)
				animal = new Animal(animalSize);
		}

		public bool HasAnimal
		{
			get
			{
				return animal != null;
			}
		}
		public float AnimalSize
		{
			get
			{
				return animal != null ? animal.Size : 0;
			}
			set
			{
				animal.Size = value;
			}
		}


		public bool ExecuteMotion(ref EntityLogic.Actions actions, Entity currentState, int generation, EntityRandom random, float myYield, ref int spawn)
		{
			bool rs = false;
			switch (generation % 3)
			{
				case 0:
					//receive unicast, send broadcast
					if (animal == null)
					{
						foreach (var msg in currentState.EnumInboundEntityMessages(channel))
						{
							if (!msg.IsBroadcast) //coming around
							{
								if (animal != null)
									throw new ExecutionException(currentState.ID, "Trying to import multiple animals");
								animal = new Animal(msg.Payload);
								rs = true;
							}
						}
					}

					if (animal != null)
						actions.Broadcast(channel, null);   //can i go there?
					break;
				case 1:
					//receive broadcast, send unicast
					if (animal == null)
					{
						LazyList<Actor> competitors = new LazyList<Actor>();
						foreach (var msg in currentState.EnumInboundEntityMessages(channel))
						{
							if (msg.IsBroadcast && msg.Payload == null) //can i go here?
								competitors.Add(msg.Sender);
						}
						if (competitors.IsNotEmpty)
							actions.Send(competitors[random.Next(competitors.Count)], channel, BitConverter.GetBytes(myYield)); //you can go here
					}
					break;
				case 2:
					//receive unicast, send unicast
					if (animal != null)
					{
						LazyList<Tuple<Actor, float>> options = new LazyList<Tuple<Actor, float>>();
						foreach (var msg in currentState.EnumInboundEntityMessages(channel))
						{
							if (!msg.IsBroadcast && Helper.Length(msg.Payload) == 4) //i can go there
								options.Add(new Tuple<Actor, float>(msg.Sender, BitConverter.ToSingle(msg.Payload, 0)));
						}
						options.Sort((a, b) => { return a.Item2.CompareTo(b.Item2); });
						if (options.IsNotEmpty)
						{
							if (spawn > 0)
							{
								foreach (var o in options)
								{
									actions.Send(o.Item1, channel, new Animal(0).Export()); //coming around
									if (--spawn <= 0)
										break;
								}
								spawn = 0;
							}
							else
								actions.Send(options.Last.Item1, channel, animal.Export()); //coming around
							animal = null;  //in transit
						}
					}
					break;
			}

			return rs;
		}

		public void Consume(GridLogic bug)
		{
			Consume(bug.animal.Size);
			bug.animal = null;
		}

		public void Consume(float size)
		{
			animal.Size += size;
		}
	};



	[Serializable]
	public class Habitat : EntityLogic
	{
		public readonly GridLogic bug,
							predator;
		public float food = 0f;
		public float maxFoodProduction = 2f;
		int spawn = 0;

		public Habitat(EntityRandom random)
		{
			bool putBug = random.NextBool(0.01f);
			bool putPredator = random.NextBool(0.02f);

			bug = new GridLogic(0, putBug ? 0.01f : 0);
			predator = new GridLogic(1, putPredator ? 0.01f : 0);
		}

		protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom random)
		{
			actions.SuppressAdvertisements = true;
			int zero = 0;
			bool killBug = predator.ExecuteMotion(ref actions, currentState, generation - 1, random, bug.AnimalSize, ref zero);

			food += random.NextFloat(maxFoodProduction);

			if (bug.HasAnimal)
			{
				if (killBug)
					predator.Consume(bug);
				else
				{
					float delta = Math.Min(food, 10f);
					bug.Consume(delta);
					food -= delta;
					food = Math.Max(0, food);
					if (bug.AnimalSize > 10f)
					{
						bug.AnimalSize = 0.001f;
						spawn = 5;
					}
				}
			}

			bug.ExecuteMotion(ref actions, currentState, generation, random, food, ref spawn);
		}
	}

}
