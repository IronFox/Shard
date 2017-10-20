using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;



namespace VectorMath
{
    public struct Vec3
    {
        public readonly float X, Y, Z;
        public Vec2 xy { get { return new Vec2(X, Y); } }
        public Vec2 yz { get { return new Vec2(Y, Z); } }

        public static readonly Vec3 Zero = new Vec3(0);
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
                throw new IndexOutOfRangeException("Unexpected index for Vec3[]");
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

        public float Length { get { return (float)System.Math.Sqrt(Vec.Dot(this, this)); } }
        public Vec3 Normalize() { return this / Length; }

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

        public override int GetHashCode()
        {
            int hash = 17;
            hash = hash * 31 + X.GetHashCode();
            hash = hash * 31 + Y.GetHashCode();
            hash = hash * 31 + Z.GetHashCode();
            return hash;
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
			return Convert.ToString(X)+ '_' + Convert.ToString(Y) + '_' + Convert.ToString(Z);
		}

        //public static implicit operator string(Vec3 v)
        //{
        //    return Convert.ToString(v.x) + ", " + Convert.ToString(v.y) + ", " + Convert.ToString(v.z);
        //}
        public override string ToString()
        {
            return "(" + Convert.ToString(X) + ", " + Convert.ToString(Y) + ", " + Convert.ToString(Z) + ")";
        }

    }


	public struct Int3
	{
		public int X, Y, Z;

		public static readonly Int3 Zero = new Int3(0);


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
				throw new IndexOutOfRangeException("Unexpected index for Int3[]");
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
				}
				throw new IndexOutOfRangeException("Unexpected index for Int3[]");
			}
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

		private bool Eq(Int3 other)
		{
			return X == other.X && Y == other.Y && Z == other.Z;
		}

		public static bool operator ==(Int3 u, Int3 v)
		{
			return u.Eq(v);
		}
		public static bool operator !=(Int3 u, Int3 v)
		{
			return !u.Eq(v);
		}

		public override bool Equals(object obj)
		{
			return obj is Int3 && Eq((Int3)obj);
		}

		public override int GetHashCode()
		{
			int hash = 17;
			hash = hash * 31 + X.GetHashCode();
			hash = hash * 31 + Y.GetHashCode();
			hash = hash * 31 + Z.GetHashCode();
			return hash;
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
				return Convert.ToString(X) + '_' + Convert.ToString(Y) + '_' + Convert.ToString(Z);
			}
		}

		public override string ToString()
		{
			return "(" + Convert.ToString(X) + ", " + Convert.ToString(Y) + ", " + Convert.ToString(Z) + ")";
		}

		public void Export(int[] ar, int offset)
		{
			ar[offset] = X;
			ar[offset+1] = Y;
			ar[offset+2] = Z;
		}
	}


	public struct Bool3
	{
		public readonly bool X, Y, Z;

		public static readonly Bool3 Zero = new Bool3(false);


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
				throw new IndexOutOfRangeException("Unexpected index for Vec3[]");
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
