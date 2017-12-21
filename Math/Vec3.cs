using System;


namespace VectorMath
{
	[Serializable]
	public struct Vec3 : IComparable<Vec3>
    {
        public readonly float X, Y, Z;
        public Vec2 XY { get { return new Vec2(X, Y); } }
        public Vec2 ZY { get { return new Vec2(Y, Z); } }

        public static readonly Vec3 Zero = new Vec3(0);
		public static readonly Vec3 One = new Vec3(1);
		public static readonly Vec3 XAxis = new Vec3(1, 0, 0);
        public static readonly Vec3 YAxis = new Vec3(0, 1, 0);
        public static readonly Vec3 ZAxis = new Vec3(0, 0, 1);

		public float this[int key]
        {
            get
            {
                switch (key)
                {
                    case 0:
                        return X;
                    case 1:
                        return Y;
                    case 2:
                        return Z;
                }
                throw new ArgumentOutOfRangeException("Unexpected index for Vec3[]");
            }
        }


        public Vec3(float x, float y, float z)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
        }
        public Vec3(float v)
        {
            this.X = v;
            this.Y = v;
            this.Z = v;
        }
        public Vec3(Vec2 xy, float z)
        {
            this.X = xy.x;
            this.Y = xy.y;
            this.Z = z;
        }
        public Vec3(float x, Vec2 yz)
        {
            this.X = x;
            this.Y = yz.x;
            this.Z = yz.y;
        }

		public Vec3(Int3 i3)
		{
			this.X = i3.X;
			this.Y = i3.Y;
			this.Z = i3.Z;
		}

		public Vec3(float[] position, int offset)
		{
			X = position[offset];
			Y = position[offset+1];
			Z = position[offset+2];
		}

		public float Length { get { return (float)System.Math.Sqrt(Vec.Dot(this, this)); } }

		public Int3 FloorInt3 { get { return new Int3((int)Math.Floor(X), (int)Math.Floor(Y), (int)Math.Floor(Z)); } }

		public Vec3 Normalized() { return this / Length; }

		public static Vec3 Min(Vec3 a, Vec3 b)
		{
			return new Vec3(
					Math.Min(a.X, b.X),
					Math.Min(a.Y, b.Y),
					Math.Min(a.Z, b.Z)
				);
		}
		public static Vec3 Max(Vec3 a, Vec3 b)
		{
			return new Vec3(
					Math.Max(a.X, b.X),
					Math.Max(a.Y, b.Y),
					Math.Max(a.Z, b.Z)
				);
		}

		public float[] ToArray()
		{
			return new float[3] { X, Y, Z };
		}

		public static Vec3 Min(Vec3 a, float b)
		{
			return new Vec3(
					Math.Min(a.X, b),
					Math.Min(a.Y, b),
					Math.Min(a.Z, b)
				);
		}
		public static Vec3 Max(Vec3 a, float b)
		{
			return new Vec3(
					Math.Max(a.X, b),
					Math.Max(a.Y, b),
					Math.Max(a.Z, b)
				);
		}

		public Vec3 Clamp(Vec3 min, Vec3 max)
		{
			return Max(Min(this, max), min);
		}
		public Vec3 Clamp(float min, float max)
		{
			return Max(Min(this, max), min);
		}

		public static Vec3 operator+(Vec3 u, Vec3 v)
        {
            return new Vec3(u.X + v.X,u.Y + v.Y,u.Z + v.Z);
        }
        public static Vec3 operator -(Vec3 u, Vec3 v)
        {
            return new Vec3(u.X - v.X, u.Y - v.Y, u.Z - v.Z);
        }
        public static Vec3 operator +(Vec3 u, float v)
        {
            return new Vec3(u.X + v, u.Y + v, u.Z + v);
        }
        public static Vec3 operator +(float v, Vec3 u)
        {
            return new Vec3(u.X + v, u.Y + v, u.Z + v);
        }
        public static Vec3 operator -(Vec3 u, float v)
        {
            return new Vec3(u.X - v, u.Y - v, u.Z - v);
        }
        public static Vec3 operator /(Vec3 u, float v)
        {
            return new Vec3(u.X / v, u.Y / v, u.Z / v);
        }
        public static Vec3 operator *(Vec3 u, float v)
        {
            return new Vec3(u.X * v, u.Y * v, u.Z * v);
        }
        public static Vec3 operator *(float v, Vec3 u)
        {
            return new Vec3(u.X * v, u.Y * v, u.Z * v);
        }

		public static Bool3 operator <(Vec3 u, Vec3 v)
		{
			return new Bool3(
				u.X < v.X,
				u.Y < v.Y,
				u.Z < v.Z
				);
		}
		public static Bool3 operator >(Vec3 u, Vec3 v)
		{
			return v < u;
		}
		public static Bool3 operator <=(Vec3 u, Vec3 v)
		{
			return new Bool3(
				u.X <= v.X,
				u.Y <= v.Y,
				u.Z <= v.Z
				);
		}
		public static Bool3 operator >=(Vec3 u, Vec3 v)
		{
			return v <= u;
		}

		private bool Eq(Vec3 other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public static bool operator ==(Vec3 u, Vec3 v)
        {
            return u.Eq(v);
        }
        public static bool operator !=(Vec3 u, Vec3 v)
        {
            return !u.Eq(v);
        }

        public override bool Equals(object obj)
        {
            return obj is Vec3 && Eq((Vec3)obj);
        }



		public static Vec3 Decode(string str)
		{
			string[] parts = str.Split('_');
			if (parts.Length != 3)
				throw new FormatException("Expected three parts in vector expression '"+str+'\'');

			return new Vec3(
					float.Parse(parts[0]),
					float.Parse(parts[1]),
					float.Parse(parts[2])
				);
		}

		public string Encode()
		{
			return Common.ToString(X)+ '_' + Common.ToString(Y) + '_' + Common.ToString(Z);
		}

        //public static implicit operator string(Vec3 v)
        //{
        //    return Common.ToString(v.x) + ", " + Common.ToString(v.y) + ", " + Common.ToString(v.z);
        //}
        public override string ToString()
        {
            return "(" + Common.ToString(X) + ", " + Common.ToString(Y) + ", " + Common.ToString(Z) + ")";
        }

		public int CompareTo(Vec3 other)
		{
			int cmp;
			cmp = X.CompareTo(other.X);
			if (cmp != 0)
				return cmp;
			cmp = Y.CompareTo(other.Y);
			if (cmp != 0)
				return cmp;
			cmp = Z.CompareTo(other.Z);
			return cmp;
		}

		public override int GetHashCode()
		{
			var hashCode = -307843816;
			hashCode = hashCode * -1521134295 + base.GetHashCode();
			hashCode = hashCode * -1521134295 + X.GetHashCode();
			hashCode = hashCode * -1521134295 + Y.GetHashCode();
			hashCode = hashCode * -1521134295 + Z.GetHashCode();
			return hashCode;
		}

		public static float GetChebyshevDistance(Vec3 a, Vec3 b)
		{
			return Math.Max(
					Math.Max(
						Math.Abs(a.X - b.X),
						Math.Abs(a.Y - b.Y)
						),
					Math.Abs(a.Z - b.Z)
				);
		}
	}

	[Serializable]
	public struct Int3
	{
		public int X, Y, Z;

		public static readonly Int3 Zero = new Int3(0);
		public static readonly Int3 One = new Int3(1);
		public static readonly Int3 XAxis = new Int3(1, 0, 0);
		public static readonly Int3 YAxis = new Int3(0, 1, 0);
		public static readonly Int3 ZAxis = new Int3(0, 0, 1);

		public int this[int key]
		{
			get
			{
				switch (key)
				{
					case 0:
						return X;
					case 1:
						return Y;
					case 2:
						return Z;
				}
				throw new ArgumentOutOfRangeException("Unexpected index for Int3[]: " + key);
			}
			set
			{
				switch (key)
				{
					case 0:
						X = value;
						break;
					case 1:
						Y = value;
						break;
					case 2:
						Z = value;
						break;
					default:
						throw new ArgumentOutOfRangeException("Unexpected index for Int3[]: " + key);
				}
			}
		}


		public Int3(int[] ar, int offset)
		{
			X = ar[offset];
			Y = ar[offset + 1];
			Z = ar[offset + 2];
		}
		public Int3(int x, int y, int z)
		{
			this.X = x;
			this.Y = y;
			this.Z = z;
		}
		public Int3(int v)
		{
			this.X = v;
			this.Y = v;
			this.Z = v;
		}

		public static Int3 operator-(Int3 v)
		{
			return new Int3(-v.X, -v.Y, -v.Z);
		}

		public int OrthographicCompare(Int3 other)
		{
			if (X < other.X)
				return -1;
			if (X > other.X)
				return 1;

			if (Y < other.Y)
				return -1;
			if (Y > other.Y)
				return 1;

			if (Z < other.Z)
				return -1;
			if (Z > other.Z)
				return 1;

			return 0;
		}

		public static Int3 Min(Int3 a, Int3 b)
		{
			return new Int3(
					Math.Min(a.X, b.X),
					Math.Min(a.Y, b.Y),
					Math.Min(a.Z, b.Z)
				);
		}
		public static Int3 Max(Int3 a, Int3 b)
		{
			return new Int3(
					Math.Max(a.X, b.X),
					Math.Max(a.Y, b.Y),
					Math.Max(a.Z, b.Z)
				);
		}
		public static Int3 Min(Int3 a, int b)
		{
			return new Int3(
					Math.Min(a.X, b),
					Math.Min(a.Y, b),
					Math.Min(a.Z, b)
				);
		}
		public static Int3 Max(Int3 a, int b)
		{
			return new Int3(
					Math.Max(a.X, b),
					Math.Max(a.Y, b),
					Math.Max(a.Z, b)
				);
		}

		public Int3 Clamp(Int3 min, Int3 max)
		{
			return Max(Min(this, max), min);
		}
		public Int3 Clamp(int min, int max)
		{
			return Max(Min(this, max), min);
		}


		public static Int3 operator /(Int3 u, int v)
		{
			return new Int3(u.X / v, u.Y / v, u.Z / v);
		}
		public static Int3 operator %(Int3 u, int v)
		{
			return new Int3(u.X % v, u.Y % v, u.Z % v);
		}

		public static Int3 operator*(Int3 u, int v)
		{
			return new Int3(u.X * v, u.Y * v, u.Z * v);
		}
		public static Int3 operator *(int v, Int3 u)
		{
			return new Int3(u.X * v, u.Y * v, u.Z * v);
		}

		public static Int3 operator +(Int3 u, Int3 v)
		{
			return new Int3(u.X + v.X, u.Y + v.Y, u.Z + v.Z);
		}
		public static Int3 operator -(Int3 u, Int3 v)
		{
			return new Int3(u.X - v.X, u.Y - v.Y, u.Z - v.Z);
		}
		public static Int3 operator +(Int3 u, int v)
		{
			return new Int3(u.X + v, u.Y + v, u.Z + v);
		}
		public static Int3 operator +(int v, Int3 u)
		{
			return new Int3(u.X + v, u.Y + v, u.Z + v);
		}
		public static Int3 operator -(Int3 u, int v)
		{
			return new Int3(u.X - v, u.Y - v, u.Z - v);
		}

		public static Bool3 operator>(Int3 a, Int3 b)
		{
			return new Bool3(a.X > b.X, a.Y > b.Y, a.Z > b.Z);
		}
		public static Bool3 operator <(Int3 a, Int3 b)
		{
			return new Bool3(a.X < b.X, a.Y < b.Y, a.Z < b.Z);
		}
		public static Bool3 operator >=(Int3 a, Int3 b)
		{
			return new Bool3(a.X >= b.X, a.Y >= b.Y, a.Z >= b.Z);
		}
		public static Bool3 operator <=(Int3 a, Int3 b)
		{
			return new Bool3(a.X <= b.X, a.Y <= b.Y, a.Z <= b.Z);
		}
		public static Bool3 operator >(Int3 a, int b)
		{
			return new Bool3(a.X > b, a.Y > b, a.Z > b);
		}
		public static Bool3 operator <(Int3 a, int b)
		{
			return new Bool3(a.X < b, a.Y < b, a.Z < b);
		}
		public static Bool3 operator >=(Int3 a, int b)
		{
			return new Bool3(a.X >= b, a.Y >= b, a.Z >= b);
		}
		public static Bool3 operator <=(Int3 a, int b)
		{
			return new Bool3(a.X <= b, a.Y <= b, a.Z <= b);
		}

		private bool Eq(Int3 other)
		{
			return X == other.X && Y == other.Y && Z == other.Z;
		}
		private bool Eq(int other)
		{
			return X == other && Y == other && Z == other;
		}

		public static bool operator ==(Int3 u, Int3 v)
		{
			return u.Eq(v);
		}
		public static bool operator !=(Int3 u, Int3 v)
		{
			return !u.Eq(v);
		}
		public static bool operator ==(Int3 u, int v)
		{
			return u.Eq(v);
		}
		public static bool operator !=(Int3 u, int v)
		{
			return !u.Eq(v);
		}
		public static bool operator ==(int u, Int3 v)
		{
			return v.Eq(u);
		}
		public static bool operator !=(int u, Int3 v)
		{
			return !v.Eq(u);
		}

		public override bool Equals(object obj)
		{
			return obj is Int3 && Eq((Int3)obj);
		}


		public static Int3 Decode(string str)
		{
			string[] parts = str.Split('_');
			if (parts.Length != 3)
				throw new FormatException("Expected three parts in vector expression '" + str + '\'');

			return new Int3(
					int.Parse(parts[0]),
					int.Parse(parts[1]),
					int.Parse(parts[2])
				);
		}

		public string Encoded
		{
			get
			{
				return Common.ToString(X) + '_' + Common.ToString(Y) + '_' + Common.ToString(Z);
			}
		}

		public int Product { get { return X * Y * Z; } }

		public Int2 YZ
		{
			get
			{
				return new Int2(Y, Z);
			}
			set
			{
				Y = value.X;
				Z = value.Y;
			}
		}
		public Int2 XZ
		{
			get
			{
				return new Int2(X, Z);
			}
			set
			{
				X = value.X;
				Z = value.Y;
			}
		}
		public Int2 XY
		{
			get
			{
				return new Int2(X, Y);
			}
			set
			{
				X = value.X;
				Y = value.Y;
			}
		}

		public override string ToString()
		{
			return "(" + Common.ToString(X) + ", " + Common.ToString(Y) + ", " + Common.ToString(Z) + ")";
		}

		public void Export(int[] ar, int offset)
		{
			ar[offset] = X;
			ar[offset+1] = Y;
			ar[offset+2] = Z;
		}

		public override int GetHashCode()
		{
			var hashCode = -307843816;
			hashCode = hashCode * -1521134295 + X.GetHashCode();
			hashCode = hashCode * -1521134295 + Y.GetHashCode();
			hashCode = hashCode * -1521134295 + Z.GetHashCode();
			return hashCode;
		}

		/// <summary>
		/// Iterates in [0,this) along each axis, so that each combination is passed to <paramref name="action"/> once
		/// </summary>
		/// <param name="action">Action to execute for each coordinate</param>
		public void Cover(Action<Int3> action)
		{
			if ((this <= 0).Any)
				return;
			Int3 cursor;
			for (cursor.X = 0; cursor.X < X; cursor.X++)
				for (cursor.Y = 0; cursor.Y < Y; cursor.Y++)
					for (cursor.Z = 0; cursor.Z < Z; cursor.Z++)
						action(cursor);
		}
	}


	public struct Bool3
	{
		public readonly bool X, Y, Z;

		public static readonly Bool3 Zero = new Bool3(false);
		public static readonly Bool3 True = new Bool3(true);
		public static readonly Bool3 False = new Bool3(false);


		public bool this[int key]
		{
			get
			{
				switch (key)
				{
					case 0:
						return X;
					case 1:
						return Y;
					case 2:
						return Z;
				}
				throw new ArgumentOutOfRangeException("Unexpected index for Bool3[]: " + key);
			}
		}


		public Bool3(bool x, bool y, bool z)
		{
			this.X = x;
			this.Y = y;
			this.Z = z;
		}
		public Bool3(bool v)
		{
			this.X = v;
			this.Y = v;
			this.Z = v;
		}

		public bool Any
		{
			get
			{
				return X || Y || Z;
			}
		}
		public bool All
		{
			get
			{
				return X && Y && Z;
			}
		}


		public static Bool3 operator!(Bool3 v)
		{
			return new Bool3(!v.X, !v.Y, !v.Z);
		}

		public static Bool3 operator &(Bool3 u, Bool3 v)
		{
			return new Bool3(u.X && v.X, u.Y && v.Y, u.Z && v.Z);
		}
		public static Bool3 operator |(Bool3 u, Bool3 v)
		{
			return new Bool3(u.X || v.X, u.Y || v.Y, u.Z || v.Z);
		}
		public static Bool3 operator &(Bool3 u, bool v)
		{
			return new Bool3(u.X && v, u.Y && v, u.Z && v);
		}
		public static Bool3 operator &(bool v,Bool3 u)
		{
			return new Bool3(u.X && v, u.Y && v, u.Z && v);
		}
		public static Bool3 operator |(bool v, Bool3 u)
		{
			return new Bool3(u.X || v, u.Y || v, u.Z || v);
		}
		public static Bool3 operator |(Bool3 u, bool v)
		{
			return new Bool3(u.X || v, u.Y || v, u.Z || v);
		}

		private bool Eq(Bool3 other)
		{
			return X == other.X && Y == other.Y && Z == other.Z;
		}

		public static bool operator ==(Bool3 u, Bool3 v)
		{
			return u.Eq(v);
		}
		public static bool operator !=(Bool3 u, Bool3 v)
		{
			return !u.Eq(v);
		}

		public override bool Equals(object obj)
		{
			return obj is Bool3 && Eq((Bool3)obj);
		}

		public override int GetHashCode()
		{
			int hash = 17;
			hash = hash * 31 + X.GetHashCode();
			hash = hash * 31 + Y.GetHashCode();
			hash = hash * 31 + Z.GetHashCode();
			return hash;
		}

		public override string ToString()
		{
			return "(" + Convert.ToString(X) + ", " + Convert.ToString(Y) + ", " + Convert.ToString(Z) + ")";
		}

	}



}
