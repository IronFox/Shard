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
using Base;

namespace Shard.Tests
{
	[TestClass()]
	public class EntityChangeSetTests
	{

		static Random random = new Random();


		public static EntityID RandomID(SimulationContext ctx)
		{
			return RandomID(ctx.Ranges.World);
		}
		public static EntityID RandomID(Box scope)
		{
			return new EntityID(Guid.NewGuid(), random.NextVec3(scope));
		}

#if STATE_ADV
		public static EntityAppearanceCollection RandomAppearance()
		{
			return null;    //later
		}
#endif


		public static byte[] RandomByteArray(bool mayBeNull = false)
		{
			if (mayBeNull && random.NextBool(0.125f))
				return null;
			return random.NextBytes(0, 16);
		}


		public static Instantiation RandomInstantiation(SimulationContext ctx)
		{
			return RandomInstantiation(ctx.Ranges);
		}
		public static Instantiation RandomInstantiation(EntityRanges ranges)
		{
			var id = RandomID(ranges.World);
			var inst = ClampedDestination(id.Position, ranges);
			return new Instantiation(id, inst,
#if STATE_ADV
				RandomAppearance(), 
#endif
				null, RandomLogic());
		}

		private static byte[] RandomLogic()
		{
			return null;    //for now
		}

		public static Removal RandomRemoval(EntityRanges ranges)
		{
			var id = RandomID(ranges.World);
			var id2 = new EntityID(Guid.NewGuid(), ClampedDestination(id.Position, ranges));
			return new Removal(id, id2);
		}

		public static Vec3 ClampedDestination(Vec3 origin, EntityRanges range)
		{
			return range.World.Clamp(random.NextVec3(Box.Centered(origin, range.Motion)));
		}

		public static Motion RandomMotion(SimulationContext ctx)
		{
			return RandomMotion(ctx.Ranges);
		}
		public static Motion RandomMotion(EntityRanges range)
		{
			var id = RandomID(range.World);
			var to = ClampedDestination(id.Position, range);
			return new Motion(id, to,
#if STATE_ADV
				RandomAppearance(), 
#endif
				null, RandomLogic());
		}

		public static Broadcast RandomBroadcast(SimulationContext ctx)
		{
			return RandomBroadcast(ctx.Ranges);
		}
		public static Broadcast RandomBroadcast(EntityRanges ranges)
		{
			return new Broadcast(RandomID(ranges.World), random.Next(16), random.NextFloat(0.0001f, ranges.Transmission), random.Next(3), RandomByteArray(true));
		}

		public static Message RandomMessage(SimulationContext ctx)
		{
			return RandomMessage(ctx.Ranges.World);
		}
		public static Message RandomMessage(Box scope)
		{
			return new Message(RandomID(scope), random.Next(16), Guid.NewGuid(), random.Next(3), RandomByteArray());
		}

		public static EntityChangeSet RandomSet(SimulationContext ctx)
		{
			return RandomSet(ctx.Ranges);
		}

		public static EntityChangeSet RandomSet(EntityRanges ranges)
		{
			EntityChangeSet rs = new EntityChangeSet();
			int numInserts = random.Next(0, 3);
			for (int i = 0; i < numInserts; i++)
				rs.Add(RandomInstantiation(ranges));

			int numRemoval = random.Next(0, 3);
			for (int i = 0; i < numRemoval; i++)
				rs.Add(RandomRemoval(ranges));

			int numMotions = random.Next(0, 3);
			for (int i = 0; i < numMotions; i++)
				rs.Add(RandomMotion(ranges));

			int numBroadcasts = random.Next(0, 3);
			for (int i = 0; i < numBroadcasts; i++)
				rs.Add(RandomBroadcast(ranges));

			int numMessages = random.Next(0, 3);
			for (int i = 0; i < numMessages; i++)
				rs.Add(RandomMessage(ranges.World));

			return rs;
		}

		public static SimulationContext RandomContext()
		{
			return RandomContext(Int3.One);
		}
		public static SimulationContext RandomContext(Int3 ext)
		{
			float r0 = random.NextFloat(0.5f, 1f);
			float m0 = random.NextFloat(0f, 1f);
			SimulationContext ctx = new SimulationContext(
				new BaseDB.ConfigContainer()
				{
					extent = ext,
					r = r0,
					m = m0
				}, 
				Box.FromMinAndMax(Vec3.Zero,Vec3.One,Bool3.False),
				true);
			Assert.AreEqual(ctx.Ranges.DisplacedTransmission, m0 > 0 && m0 < r0);
			return ctx;
		}

		[TestMethod()]
		public void RangeTest()
		{
			for (int i = 0; i < 100; i++)
			{
				var ctx = RandomContext();
				for (int j = 0; j < 10; j++)
				{
					var msg = RandomMessage(ctx.Ranges.World);
					Box check = Box.CenterExtent(random.NextVec3(ctx.Ranges.World), ctx.Ranges.Transmission);
					Assert.AreEqual(msg.Affects(check, ctx), check.Intersects(Box.CenterExtent(msg.Origin.Position, ctx.Ranges.Transmission)));
				}
			}
		}

		[TestMethod()]
		public void OrderTest()
		{
			var ctx = RandomContext();
			for (int i = 0; i < 100; i++)
			{
				var m0 = RandomMessage(ctx);
				var m1 = RandomMessage(ctx);
				Assert.AreEqual(m0.CompareTo(m1), -m1.CompareTo(m0));
			}
			for (int i = 0; i < 100; i++)
			{
				var m0 = RandomBroadcast(ctx);
				var b1 = RandomBroadcast(ctx);
				Assert.AreEqual(m0.CompareTo(b1), -b1.CompareTo(m0));
			}
			for (int i = 0; i < 100; i++)
			{
				var m = RandomMessage(ctx);
				var b = RandomBroadcast(ctx);
				Assert.AreEqual(m.CompareTo(b), -b.CompareTo(m));
			}
		}

		[TestMethod()]
		public void CloneTest()
		{
			var ctx = RandomContext();
			for (int i = 0; i < 100; i++)
			{
				EntityChangeSet set = RandomSet(ctx);
				EntityChangeSet copy = set.Clone();

				Assert.AreEqual(set, copy);


				copy.Add(RandomInstantiation(ctx));
				Assert.AreNotEqual(set, copy);
			}
		}

		[TestMethod()]
		public void CSSerializationTest()
		{
			var ctx = RandomContext();
			var f = new BinaryFormatter();
			for (int i = 0; i < 100; i++)
			{
				EntityChangeSet original = RandomSet(ctx);
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