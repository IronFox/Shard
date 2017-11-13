using System;
using VectorMath;

namespace Shard
{
	public struct SpaceCube
	{
		public readonly Vec3 Min;
		public readonly Vec3 Max;
		public readonly Bool3 MaxIsInclusive;

		public Vec3 Size { get { return Max - Min; } }


		public SpaceCube(Vec3 min, Vec3 size, Bool3 maxIsInclusive)
		{
			Min = min;
			Max = min + size;
			MaxIsInclusive = maxIsInclusive;
		}

		private static bool IsLess(float x, float max, bool inclusive)
		{
			if (x > max)
				return false;
			if (x == max)
				return inclusive;
			return true;
		}

		public bool Contains(Vec3 p)
		{
			return p.X >= Min.X && IsLess(p.X, Max.X, MaxIsInclusive.X)
				&& p.Y >= Min.Y && IsLess(p.Y, Max.Y, MaxIsInclusive.Y)
				&& p.Z >= Min.Z && IsLess(p.Z, Max.Z, MaxIsInclusive.Z)
				;
		}

		private static float Clamp(float v, float min, float max, bool maxIsInclusive)
		{
			v = Math.Max(v, min);
			if (!maxIsInclusive)
				max -= float.Epsilon * 2;
			v = Math.Min(v, max);
			return v;
		}

		private static float Relativate(float v, float min, float max)
		{
			return (v - min) / (max - min);
		}

		public Vec3 Relativate(Vec3 p)
		{
			return new Vec3(
				Relativate(p.X, Min.X, Max.X),
				Relativate(p.Y, Min.Y, Max.Y),
				Relativate(p.Z, Min.Z, Max.Z)
				);
		}


		public Vec3 Clamp(Vec3 p)
		{
			return new Vec3(
					Clamp(p.X, Min.X, Max.X, MaxIsInclusive.X),
					Clamp(p.Y, Min.Y, Max.Y, MaxIsInclusive.Y),
					Clamp(p.Z, Min.Z, Max.Z, MaxIsInclusive.Z)
				);
		}
	}
}