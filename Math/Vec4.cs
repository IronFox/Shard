using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VectorMath
{
    public struct Vec4
    {
        public float x, y, z, w;

        public Vec3 xyz { get { return new Vec3(x, y, z); } set { x = value.X; y = value.Y; z = value.Z; } }
        public Vec2 xy { get { return new Vec2(x, y); } set { x = value.x; y = value.y; } }
        public Vec2 zw { get { return new Vec2(z, w); } set { z = value.x; w = value.y; } }


        public Vec4(Vec3 v, float w = 1)
        {
            this.x = v.X;
            this.y = v.Y;
            this.z = v.Z;
            this.w = w;
        }
        public Vec4(Vec2 xy, Vec2 zw)
        {
            this.x = xy.x;
            this.y = xy.y;
            this.z = zw.x;
            this.w = zw.y;
        }
        public Vec4(Vec2 xy, float z, float w=1)
        {
            this.x = xy.x;
            this.y = xy.y;
            this.z = z;
            this.w = w;
        }
         
        public Vec4(float x, float y, float z, float w=1)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public Vec4(float v)
        {
            this.x = v;
            this.y = v;
            this.z = v;
            this.w = v;
        }

        public float Length { get { return (float)System.Math.Sqrt(Vec.Dot(this, this)); } set { this *= value / Length; } }
        public Vec4 Normalize() { return this / Length; }

        public static Vec4 operator +(Vec4 u, Vec4 v)
        {
            return new Vec4(u.x + v.x, u.y + v.y, u.z + v.z, u.w + v.w);
        }
        public static Vec4 operator -(Vec4 u, Vec4 v)
        {
            return new Vec4(u.x - v.x, u.y - v.y, u.z - v.z, u.w - v.w);
        }
        public static Vec4 operator +(Vec4 u, float v)
        {
            return new Vec4(u.x + v, u.y + v, u.z + v, u.w + v);
        }
        public static Vec4 operator +(float v, Vec4 u)
        {
            return new Vec4(u.x + v, u.y + v, u.z + v, u.w + v);
        }
        public static Vec4 operator -(Vec4 u, float v)
        {
            return new Vec4(u.x - v, u.y - v, u.z - v, u.w - v);
        }
        public static Vec4 operator /(Vec4 u, float v)
        {
            return new Vec4(u.x / v, u.y / v, u.z / v, u.w / v);
        }
        public static Vec4 operator *(Vec4 u, float v)
        {
            return new Vec4(u.x * v, u.y * v, u.z * v, u.w * v);
        }
        public static Vec4 operator *(float v, Vec4 u)
        {
            return new Vec4(u.x * v, u.y * v, u.z * v, u.w * v);
        }
        //public static implicit operator string(Vec4 v)
        //{
        //    return Convert.ToString(v.x) + ", " + Convert.ToString(v.y) + ", " + Convert.ToString(v.z) + ", " + Convert.ToString(v.w);
        //}

        private bool Eq(Vec4 other)
        {
			return x == other.x && y == other.y && z == other.z && w == other.w;
        }
        public static bool operator ==(Vec4 u, Vec4 v)
        {
            return u.Eq(v);
        }
        public static bool operator !=(Vec4 u, Vec4 v)
        {
            return !u.Eq(v);
        }

        public override bool Equals(object obj)
        {
            return obj is Vec4 && Eq((Vec4)obj);
        }

        public override int GetHashCode()
        {
            int hash = 17;
            hash = hash * 31 + x.GetHashCode();
            hash = hash * 31 + y.GetHashCode();
            hash = hash * 31 + z.GetHashCode();
            hash = hash * 31 + w.GetHashCode();
            return hash;
        }


        public override string ToString()
        {
            return "(" + Convert.ToString(x) + ", " + Convert.ToString(y)+ ", " + Convert.ToString(z) + ", " + Convert.ToString(w) + ")";
        }

    }



	public struct Bool4
	{
		public readonly bool X, Y, Z, W;

		public static readonly Bool4 Zero = new Bool4(false);


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
					case 3:
						return W;
				}
				throw new ArgumentOutOfRangeException("Unexpected index for Bool4[]");
			}
		}


		public Bool4(bool x, bool y, bool z, bool w)
		{
			X = x;
			Y = y;
			Z = z;
			W = w;
		}
		public Bool4(Bool3 xyz, bool w)
		{
			X = xyz.X;
			Y = xyz.Y;
			Z = xyz.Z;
			W = w;
		}
		public Bool4(bool v)
		{
			X = v;
			Y = v;
			Z = v;
			W = v;
		}

		public bool Any
		{
			get
			{
				return X || Y || Z || W;
			}
		}
		public bool All
		{
			get
			{
				return X && Y && Z && W;
			}
		}

		public static Bool4 operator !(Bool4 v)
		{
			return new Bool4(!v.X, !v.Y, !v.Z, !v.W);
		}

		public static Bool4 operator &(Bool4 u, Bool4 v)
		{
			return new Bool4(u.X && v.X, u.Y && v.Y, u.Z && v.Z, u.W && v.W);
		}
		public static Bool4 operator |(Bool4 u, Bool4 v)
		{
			return new Bool4(u.X || v.X, u.Y || v.Y, u.Z || v.Z, u.W || v.W);
		}
		public static Bool4 operator &(Bool4 u, bool v)
		{
			return new Bool4(u.X && v, u.Y && v, u.Z && v, u.Z && v);
		}
		public static Bool4 operator &(bool v, Bool4 u)
		{
			return new Bool4(u.X && v, u.Y && v, u.Z && v, u.W && v);
		}
		public static Bool4 operator |(bool v, Bool4 u)
		{
			return new Bool4(u.X || v, u.Y || v, u.Z || v, u.W || v);
		}
		public static Bool4 operator |(Bool4 u, bool v)
		{
			return new Bool4(u.X || v, u.Y || v, u.Z || v, u.W || v);
		}

		private bool Eq(Bool4 other)
		{
			return X == other.X && Y == other.Y && Z == other.Z && W == other.W;
		}

		public static bool operator ==(Bool4 u, Bool4 v)
		{
			return u.Eq(v);
		}
		public static bool operator !=(Bool4 u, Bool4 v)
		{
			return !u.Eq(v);
		}

		public override bool Equals(object obj)
		{
			return obj is Bool4 && Eq((Bool4)obj);
		}

		public override int GetHashCode()
		{
			int hash = 17;
			hash = hash * 31 + X.GetHashCode();
			hash = hash * 31 + Y.GetHashCode();
			hash = hash * 31 + Z.GetHashCode();
			hash = hash * 31 + W.GetHashCode();
			return hash;
		}

		public override string ToString()
		{
			return "(" + Convert.ToString(X) + ", " + Convert.ToString(Y) + ", " + Convert.ToString(Z) + ", " + Convert.ToString(W) + ")";
		}

	}

}
