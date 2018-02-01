using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VectorMath
{
    public struct Matrix3 : IComparable<Matrix3>
    {
        public Vec3 x, y, z;
        public static Matrix3 Identity { get { return new Matrix3(1); } }

        public Matrix3(float scalar)
        {
            x = new Vec3(scalar, 0, 0);
            y = new Vec3(0, scalar, 0);
            z = new Vec3(0, 0, scalar);
        }
        public Matrix3(Vec3 x, Vec3 y, Vec3 z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        private bool Eq(Matrix3 other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        public static bool operator ==(Matrix3 u, Matrix3 v)
        {
            return u.Eq(v);
        }
        public static bool operator !=(Matrix3 u, Matrix3 v)
        {
            return !u.Eq(v);
        }

        public override bool Equals(object obj)
        {
            return obj is Matrix3 && Eq((Matrix3)obj);
        }



		public int CompareTo(Matrix3 other)
		{
			int rs = x.CompareTo(other.x);
			if (rs == 0)
				rs = y.CompareTo(other.y);
			if (rs == 0)
				rs = z.CompareTo(other.z);
			return rs;
		}

		public override int GetHashCode()
		{
			var hashCode = 373119288;
			hashCode = hashCode * -1521134295 + x.GetHashCode();
			hashCode = hashCode * -1521134295 + y.GetHashCode();
			hashCode = hashCode * -1521134295 + z.GetHashCode();
			return hashCode;
		}

		public static Vec3 operator *(Matrix3 m, Vec3 v)
        {
            return m.x * v.X + m.y * v.Y + m.z * v.Z;
        }
        public static Matrix3 operator *(Matrix3 m, Matrix3 n)
        {
            return new Matrix3(m *n.x, m * n.y, m*n.z);
        }
    }
}
