using VectorMath;

namespace Shard
{
	public class EntityRandom
	{
		uint x, y, z, w;

		public EntityRandom(int seed)
		{
			x = (uint)seed;
			y = (uint)(seed * 2)+13;
			z = (uint)(seed *3 / 2) - 17;
			w = (uint)(seed * 4 / 3) + 23;
		}

		public EntityRandom(Entity currentState, int generation) : this(Helper.Hash(typeof(EntityRandom)).Add(generation).Add(currentState.ID).GetHashCode())
		{}

		public bool NextBool()
		{
			return NextU() >= uint.MaxValue / 2;
		}

		public int Next()
		{
			return (int)NextU();
		}
		public uint NextU()
		{
			uint t = x ^ (x << 11);
			x = y; y = z; z = w;
			w = w ^ (w >> 19) ^ (t ^ (t >> 8));
			return w;
		}

		public int Next(int exclusiveMax)
		{
			return (int)((long)NextU() * exclusiveMax / ((long)uint.MaxValue+1));
		}

		public float NextFloat()
		{
			return (float)NextU() / uint.MaxValue;
		}

		public float NextFloat(float max)
		{
			return NextFloat() * max;
		}

		public float NextFloat(float min, float max)
		{
			return NextFloat() * (max - min) + min;
		}

		public Vec3 NextVec3(float min, float max)
		{
			return new Vec3(NextFloat(min, max), NextFloat(min, max), NextFloat(min, max));
		}


	}
}