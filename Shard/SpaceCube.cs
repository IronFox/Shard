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

	}
}