using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VectorMath
{
    public struct TVec2<T>
    {
        public T x, y;

        public TVec2(T x_, T y_)
        {
            x = x_;
            y = y_;
        }

        public TVec2(T v)
        {
            x = v;
            y = v;
        }
    }

    public struct Vec2
    {
        public float x, y;

        public Vec2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public Vec2(float v)
        {
            this.x = v;
            this.y = v;
        }

        public Vec2(Resolution res)
        {
            this.x = (float)res.Width;
            this.y = (float)res.Height;
        }

        public float Length { get { return (float)System.Math.Sqrt(Vec.Dot(this, this)); } set { this *= value / Length; } }
        public Vec2 Normalize() { return this / Length; }

        public static Vec2 operator +(Vec2 u, Vec2 v)
        {
            return new Vec2(u.x + v.x, u.y + v.y);
        }
        public static Vec2 operator -(Vec2 u, Vec2 v)
        {
            return new Vec2(u.x - v.x, u.y - v.y);
        }
        public static Vec2 operator +(Vec2 u, float v)
        {
            return new Vec2(u.x + v, u.y + v);
        }
        public static Vec2 operator +(float v, Vec2 u)
        {
            return new Vec2(u.x + v, u.y + v);
        }
        public static Vec2 operator -(Vec2 u, float v)
        {
            return new Vec2(u.x - v, u.y - v);
        }
        public static Vec2 operator /(Vec2 u, float v)
        {
            return new Vec2(u.x / v, u.y / v);
        }
        public static Vec2 operator *(Vec2 u, float v)
        {
            return new Vec2(u.x * v, u.y * v);
        }
        public static Vec2 operator *(float v, Vec2 u)
        {
            return new Vec2(u.x * v, u.y * v);
        }
        //public static implicit operator string(Vec2 v)
        //{
        //    return Convert.ToString(v.x) + ", " + Convert.ToString(v.y);
        //}
        private bool Eq(Vec2 other)
        {
            return x == other.x && y == other.y;
        }

        public static bool operator ==(Vec2 u, Vec2 v)
        {
            return u.Eq(v);
        }
        public static bool operator !=(Vec2 u, Vec2 v)
        {
            return !u.Eq(v);
        }

        public override int GetHashCode()
        {
            int hash = 17;
            hash = hash * 31 + x.GetHashCode();
            hash = hash * 31 + y.GetHashCode();
            return hash;
        }

        public override bool Equals(object obj)
        {
            return obj is Vec2 && Eq((Vec2)obj);
        }

        public override string ToString()
        {
            return "("+Convert.ToString(x) + ", " + Convert.ToString(y)+")";
        }
    }

	public struct Int2
	{
		public int X, Y;

		public static readonly Int2 Zero = new Int2(0);


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
				}
				throw new IndexOutOfRangeException("Unexpected index for Int2[]: " + key);
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
					default:
						throw new IndexOutOfRangeException("Unexpected index for Int2[]: "+key);
				}
			}
		}


		public Int2(int x, int y)
		{
			this.X = x;
			this.Y = y;
		}
		public Int2(int v)
		{
			this.X = v;
			this.Y = v;
		}

		public static Int2 operator -(Int2 v)
		{
			return new Int2(-v.X, -v.Y);
		}

		public int OrthographicCompare(Int2 other)
		{
			if (X < other.X)
				return -1;
			if (X > other.X)
				return 1;

			if (Y < other.Y)
				return -1;
			if (Y > other.Y)
				return 1;

			return 0;
		}


		public static Int2 operator *(Int2 u, int v)
		{
			return new Int2(u.X * v, u.Y * v);
		}
		public static Int2 operator *(int v, Int2 u)
		{
			return new Int2(u.X * v, u.Y * v);
		}

		public static Int2 operator +(Int2 u, Int2 v)
		{
			return new Int2(u.X + v.X, u.Y + v.Y);
		}
		public static Int2 operator -(Int2 u, Int2 v)
		{
			return new Int2(u.X - v.X, u.Y - v.Y);
		}
		public static Int2 operator +(Int2 u, int v)
		{
			return new Int2(u.X + v, u.Y + v);
		}
		public static Int2 operator +(int v, Int2 u)
		{
			return new Int2(u.X + v, u.Y + v);
		}
		public static Int2 operator -(Int2 u, int v)
		{
			return new Int2(u.X - v, u.Y - v);
		}

		public static Bool2 operator >(Int2 a, Int2 b)
		{
			return new Bool2(a.X > b.X, a.Y > b.Y);
		}
		public static Bool2 operator <(Int2 a, Int2 b)
		{
			return new Bool2(a.X < b.X, a.Y < b.Y);
		}
		public static Bool2 operator >=(Int2 a, Int2 b)
		{
			return new Bool2(a.X >= b.X, a.Y >= b.Y);
		}
		public static Bool2 operator <=(Int2 a, Int2 b)
		{
			return new Bool2(a.X <= b.X, a.Y <= b.Y);
		}

		private bool Eq(Int2 other)
		{
			return X == other.X && Y == other.Y;
		}

		public static bool operator ==(Int2 u, Int2 v)
		{
			return u.Eq(v);
		}
		public static bool operator !=(Int2 u, Int2 v)
		{
			return !u.Eq(v);
		}

		public override bool Equals(object obj)
		{
			return obj is Int2 && Eq((Int2)obj);
		}

		public override int GetHashCode()
		{
			int hash = 17;
			hash = hash * 31 + X.GetHashCode();
			hash = hash * 31 + Y.GetHashCode();
			return hash;
		}

		public static Int2 Decode(string str)
		{
			string[] parts = str.Split('_');
			if (parts.Length != 2)
				throw new FormatException("Expected two parts in vector expression '" + str + '\'');

			return new Int2(
					int.Parse(parts[0]),
					int.Parse(parts[1])
				);
		}

		public string Encoded
		{
			get
			{
				return Convert.ToString(X) + '_' + Convert.ToString(Y);
			}
		}

		public int Product { get { return X * Y; } }

		public override string ToString()
		{
			return "(" + Convert.ToString(X) + ", " + Convert.ToString(Y) + ")";
		}

		public void Export(int[] ar, int offset)
		{
			ar[offset] = X;
			ar[offset + 1] = Y;
		}
	}


	public struct Bool2
	{
		public readonly bool X, Y;

		public static readonly Bool2 Zero = new Bool2(false);


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
				}
				throw new IndexOutOfRangeException("Unexpected index for Bool2[]: " + key);
			}
		}


		public Bool2(bool x, bool y)
		{
			this.X = x;
			this.Y = y;
		}
		public Bool2(bool v)
		{
			this.X = v;
			this.Y = v;
		}

		public bool Any
		{
			get
			{
				return X || Y;
			}
		}
		public bool All
		{
			get
			{
				return X && Y;
			}
		}

		public static Bool2 operator !(Bool2 v)
		{
			return new Bool2(!v.X, !v.Y);
		}

		public static Bool2 operator &(Bool2 u, Bool2 v)
		{
			return new Bool2(u.X && v.X, u.Y && v.Y);
		}
		public static Bool2 operator |(Bool2 u, Bool2 v)
		{
			return new Bool2(u.X || v.X, u.Y || v.Y);
		}
		public static Bool2 operator &(Bool2 u, bool v)
		{
			return new Bool2(u.X && v, u.Y && v);
		}
		public static Bool2 operator &(bool v, Bool2 u)
		{
			return new Bool2(u.X && v, u.Y && v);
		}
		public static Bool2 operator |(bool v, Bool2 u)
		{
			return new Bool2(u.X || v, u.Y || v);
		}
		public static Bool2 operator |(Bool2 u, bool v)
		{
			return new Bool2(u.X || v, u.Y || v);
		}

		private bool Eq(Bool2 other)
		{
			return X == other.X && Y == other.Y;
		}

		public static bool operator ==(Bool2 u, Bool2 v)
		{
			return u.Eq(v);
		}
		public static bool operator !=(Bool2 u, Bool2 v)
		{
			return !u.Eq(v);
		}

		public override bool Equals(object obj)
		{
			return obj is Bool2 && Eq((Bool2)obj);
		}

		public override int GetHashCode()
		{
			int hash = 17;
			hash = hash * 31 + X.GetHashCode();
			hash = hash * 31 + Y.GetHashCode();
			return hash;
		}

		public override string ToString()
		{
			return "(" + Convert.ToString(X) + ", " + Convert.ToString(Y) + ")";
		}

	}


}
