using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shard.EntityChange;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using VectorMath;

namespace Shard.Tests
{
	[TestClass()]
	public class EntityChangeSetTests
	{

		static Random random = new Random();


		public static EntityID RandomID()
		{
			return new EntityID(Guid.NewGuid(), random.NextVec3(Simulation.MySpace));
		}

		public static EntityAppearanceCollection RandomAppearance()
		{
			return null;    //later
		}



		static byte[] RandomByteArray(bool mayBeNull = false)
		{
			if (mayBeNull && random.NextBool(0.125f))
				return null;
			return random.NextBytes(0, 16);
		}


		public static Instantiation RandomInstantiation()
		{
			var id = RandomID();
			var inst = ClampedDestination(id.Position);
			return new Instantiation(id, inst, RandomAppearance(), RandomLogic());
		}

		private static EntityLogic RandomLogic()
		{
			return null;	//for now
		}

		public static Removal RandomRemoval()
		{
			var id = RandomID();
			var id2 = new EntityID(Guid.NewGuid(), ClampedDestination(id.Position));
			return new Removal(id, id2);
		}

		public static Vec3 ClampedDestination(Vec3 origin)
		{
			return Simulation.FullSimulationSpace.Clamp(random.NextVec3(Box.Centered(origin, Simulation.M)));
		}

		public static Motion RandomMotion()
		{
			var id = RandomID();
			var to = ClampedDestination(id.Position);
			return new Motion(id, to, RandomAppearance(), RandomLogic());
		}

		public static Broadcast RandomBroadcast()
		{
			return new Broadcast(RandomID(), RandomByteArray(true), random.Next(16));
		}

		public static Message RandomMessage()
		{
			return new Message(RandomID(), random.Next(16), Guid.NewGuid(), RandomByteArray());
		}


		public static EntityChangeSet RandomSet()
		{
			EntityChangeSet rs = new EntityChangeSet();
			int numInserts = random.Next(0, 3);
			for (int i = 0; i < numInserts; i++)
				rs.Add(RandomInstantiation());

			int numRemoval = random.Next(0, 3);
			for (int i = 0; i < numRemoval; i++)
				rs.Add(RandomRemoval());

			int numMotions = random.Next(0, 3);
			for (int i = 0; i < numMotions; i++)
				rs.Add(RandomMotion());

			int numBroadcasts = random.Next(0, 3);
			for (int i = 0; i < numBroadcasts; i++)
				rs.Add(RandomBroadcast());

			int numMessages = random.Next(0, 3);
			for (int i = 0; i < numMessages; i++)
				rs.Add(RandomMessage());

			return rs;
		}


		[TestMethod()]
		public void OrderTest()
		{
			for (int i = 0; i < 100; i++)
			{
				var m0 = RandomMessage();
				var m1 = RandomMessage();
				Assert.AreEqual(m0.CompareTo(m1), -m1.CompareTo(m0));
			}
			for (int i = 0; i < 100; i++)
			{
				var m0 = RandomBroadcast();
				var b1 = RandomBroadcast();
				Assert.AreEqual(m0.CompareTo(b1), -b1.CompareTo(m0));
			}
			for (int i = 0; i < 100; i++)
			{
				var m = RandomMessage();
				var b = RandomBroadcast();
				Assert.AreEqual(m.CompareTo(b), -b.CompareTo(m));
			}
		}

		[TestMethod()]
		public void CloneTest()
		{
			for (int i = 0; i < 100; i++)
			{
				EntityChangeSet set = RandomSet();
				EntityChangeSet copy = set.Clone();

				Assert.AreEqual(set, copy);


				copy.Add(RandomInstantiation());
				Assert.AreNotEqual(set, copy);
			}
		}

		[TestMethod()]
		public void CSSerializationTest()
		{
			var f = new BinaryFormatter();
			for (int i = 0; i < 100; i++)
			{
				EntityChangeSet original = RandomSet();
				using (var ms = new MemoryStream())
				{
					f.Serialize(ms, original);
					ms.Seek(0, SeekOrigin.Begin);

					EntityChangeSet deserialized = (EntityChangeSet)f.Deserialize(ms);
					Assert.AreEqual(original, deserialized);
				}
			}
		}

	}
}