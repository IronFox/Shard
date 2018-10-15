using System;
using VectorMath;

namespace Base
{
	public struct Box
	{
		public struct Range
		{
			public readonly float Min,
								Max;
			public readonly bool MaxIsInclusive;

			public Range(float min, float max, bool maxIsInclusive) : this()
			{
				Min = min;
				Max = max;
				MaxIsInclusive = maxIsInclusive;
			}

			public Range Grow(float r)
			{
				return new Range(Min - r, Max + r, MaxIsInclusive);
			}

			public override string ToString()
			{
				if (MaxIsInclusive)
					return "[" + Min + "," + Max + "]";
				return "[" + Min + "," + Max + ")";
			}

			public float Size
			{
				get
				{
					return Max - Min;
				}
			}

			public float InclusiveMax
			{
				get
				{
					if (MaxIsInclusive)
						return Max;
					if (Max == 0)
						return -float.Epsilon;
					if (Max > 0)
						return Helper.IntToFloat(Helper.FloatToInt(Max) - 1);
					else
						return Helper.IntToFloat(Helper.FloatToInt(Max) + 1);
				}
			}

			private static bool IsLess(float x, float max, bool inclusive)
			{
				if (x > max)
					return false;
				if (x == max)
					return inclusive;
				return true;
			}

			public bool Contains(float x)
			{
				return x >= Min && IsLess(x, Max, MaxIsInclusive);
			}

			public float Clamp(float v)
			{
				v = Math.Max(v, Min);
				v = Math.Min(v, InclusiveMax);
				return v;
			}

			public float Relativate(float v)
			{
				return (v - Min) / Size;
			}

			public float DeRelativate(float v)
			{
				return v * Size + Min;
			}


			public bool Intersects(Range other)
			{
				if (Min > other.Max || Max < other.Min)
					return false;
				if (Min == other.Max && !other.MaxIsInclusive)
					return false;
				if (Max == other.Min && !MaxIsInclusive)
					return false;
				return true;
			}
		}

		public static Box Centered(Vec3 center, float r)
		{
			return new Box(
				new Range(center.X - r, center.X + r, true),
				new Range(center.Y - r, center.Y + r, true),
				new Range(center.Z - r, center.Z + r, true)
				);
		}

		public Box Grow(float r)
		{
			return new Box(X.Grow(r), Y.Grow(r), Z.Grow(r));
		}

		public readonly Range X, Y, Z;

		public Box(Range x, Range y, Range z)
		{
			X = x;
			Y = y;
			Z = z;
		}

		public Vec3 Size { get { return new Vec3(X.Size,Y.Size,Z.Size); } }

		public Vec3 Center { get { return (Min + Max) * 0.5f; } }
		public Vec3 Min { get { return new Vec3(X.Min, Y.Min, Z.Min); } }
		public Vec3 Max { get { return new Vec3(X.Max, Y.Max, Z.Max); } }

		public static Box OffsetSize(Vec3 min, Vec3 size, Bool3 maxIsInclusive)
		{
			return new Box(
				new Range(min.X, min.X + size.X, maxIsInclusive.X),
				new Range(min.Y, min.Y + size.Y, maxIsInclusive.Y),
				new Range(min.Z, min.Z + size.Z, maxIsInclusive.Z)
			);
		}
		public static Box FromMinAndMax(Vec3 min, Vec3 max, Bool3 maxIsInclusive)
		{
			return new Box(
				new Range(min.X, max.X, maxIsInclusive.X),
				new Range(min.Y, max.Y, maxIsInclusive.Y),
				new Range(min.Z, max.Z, maxIsInclusive.Z)
			);
		}

		public bool Contains(Vec3 p)
		{
			return X.Contains(p.X) && Y.Contains(p.Y) && Z.Contains(p.Z);
				;
		}


		public Vec3 Relativate(Vec3 p)
		{
			return new Vec3(
				X.Relativate(p.X),
				Y.Relativate(p.Y),
				Z.Relativate(p.Z)
				);
		}
		public Vec3 DeRelativate(Vec3 p)
		{
			return new Vec3(
				X.DeRelativate(p.X),
				Y.DeRelativate(p.Y),
				Z.DeRelativate(p.Z)
				);
		}


		public Vec3 Clamp(Vec3 p)
		{
			return new Vec3(
					X.Clamp(p.X),
					Y.Clamp(p.Y),
					Z.Clamp(p.Z)
				);
		}

		public bool Intersects(Box other)
		{
			return X.Intersects(other.X) && Y.Intersects(other.Y) && Z.Intersects(other.Z);
				
		}

		public override string ToString()
		{
			return "[" + Min.ToString() + "," + Max.ToString() + "]";
		}

		public static Box CenterExtent(Vec3 center, float extent)
		{
			return CenterExtent(center, extent, Bool3.True);
		}

		public static Box CenterExtent(Vec3 center, float extent, Bool3 maxIsInclusive)
		{
			return FromMinAndMax(center - extent, center + extent, maxIsInclusive);
		}
	}



	public struct IntBox
	{
		public struct Range
		{
			public readonly int Min,
								Max;
			public readonly bool MaxIsInclusive;

			public Range(int min, int max, bool maxIsInclusive) : this()
			{
				Min = min;
				Max = max;
				MaxIsInclusive = maxIsInclusive;
			}

			public Range Grow(int r)
			{
				return new Range(Min - r, Max + r, MaxIsInclusive);
			}

			public override string ToString()
			{
				if (MaxIsInclusive)
					return "[" + Min + "," + Max + "]";
				return "[" + Min + "," + Max + ")";
			}

			public int Size
			{
				get
				{
					return InclusiveMax - Min + 1;
				}
			}

			public int InclusiveMax
			{
				get
				{
					if (MaxIsInclusive)
						return Max;
					return Max - 1;
				}
			}

			private bool IsLess(int x)
			{
				if (x > Max)
					return false;
				if (x == Max)
					return MaxIsInclusive;
				return true;
			}

			public bool Contains(int x)
			{
				return x >= Min && IsLess(x);
			}

			public int Clamp(int v)
			{
				v = Math.Max(v, Min);
				v = Math.Min(v, InclusiveMax);
				return v;
			}


			public bool Intersects(Range other)
			{
				if (Min > other.Max || Max < other.Min)
					return false;
				if (Min == other.Max && !other.MaxIsInclusive)
					return false;
				if (Max == other.Min && !MaxIsInclusive)
					return false;
				return true;
			}
		}

		public IntBox Grow(int r)
		{
			return new IntBox(X.Grow(r), Y.Grow(r), Z.Grow(r));
		}

		public readonly Range X, Y, Z;

		public IntBox(Range x, Range y, Range z)
		{
			X = x;
			Y = y;
			Z = z;
		}

		public Int3 Size { get { return new Int3(X.Size, Y.Size, Z.Size); } }

		public Int3 Center { get { return (Min + Max) /2; } }
		public Int3 Min { get { return new Int3(X.Min, Y.Min, Z.Min); } }
		public Int3 Max { get { return new Int3(X.Max, Y.Max, Z.Max); } }

		public static IntBox OffsetSize(Int3 min, Int3 size, Bool3 maxIsInclusive)
		{
			return new IntBox(
				new Range(min.X, min.X + size.X, maxIsInclusive.X),
				new Range(min.Y, min.Y + size.Y, maxIsInclusive.Y),
				new Range(min.Z, min.Z + size.Z, maxIsInclusive.Z)
			);
		}
		public static IntBox FromMinAndMax(Int3 min, Int3 max, Bool3 maxIsInclusive)
		{
			return new IntBox(
				new Range(min.X, max.X, maxIsInclusive.X),
				new Range(min.Y, max.Y, maxIsInclusive.Y),
				new Range(min.Z, max.Z, maxIsInclusive.Z)
			);
		}

		public bool Contains(Int3 p)
		{
			return X.Contains(p.X) && Y.Contains(p.Y) && Z.Contains(p.Z);
			;
		}


		public Int3 Clamp(Int3 p)
		{
			return new Int3(
					X.Clamp(p.X),
					Y.Clamp(p.Y),
					Z.Clamp(p.Z)
				);
		}

		public bool Intersects(IntBox other)
		{
			return X.Intersects(other.X) && Y.Intersects(other.Y) && Z.Intersects(other.Z);
		}
	}

}