using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VectorMath
{
    public struct Matrix4
    {
        public Vec4 x, y, z, w;
        //public static Matrix4 Identity = new Matrix4(1);

        public Matrix3 Orientation { get { return new Matrix3(x.xyz, y.xyz, z.xyz); } set { x.xyz = value.x; y.xyz = value.y; z.xyz = value.z; } }
        public Vec3 Position { get { return w.xyz; } set { w.xyz = value; } }
        public static readonly Matrix4 Identity = new Matrix4(1);

        public Matrix4(float scalar)
        {
            x = new Vec4(scalar, 0, 0, 0);
            y = new Vec4(0, scalar, 0, 0);
            z = new Vec4(0, 0, scalar, 0);
            w = new Vec4(0, 0, 0, scalar);
        }
        public Matrix4(Vec4 newX, Vec4 newY, Vec4 newZ, Vec4 newW)
        {
            x = newX;
            y = newY;
            z = newZ;
            w = newW;
        }
		public Matrix4(float[] field)
		{
			x = new Vec4(field,0);
			y = new Vec4(field,4);
			z = new Vec4(field,8);
			w = new Vec4(field,12);
		}

        public void ResetBottomRow()
        {
            x.w = y.w = z.w = 0;
            w.w = 1;
        }

        public static Vec4 operator *(Matrix4 m, Vec4 v)
        {
            return m.x * v.x + m.y * v.y + m.z * v.z + m.w * v.w;
        }
        public static Matrix4 operator *(Matrix4 m, Matrix4 n)
        {
            return new Matrix4(m *n.x, m * n.y, m*n.z, m*n.w);
        }


        private bool Eq(Matrix4 other)
        {
            return x == other.x && y == other.y && z == other.z && w == other.w;
        }

        public static bool operator ==(Matrix4 u, Matrix4 v)
        {
            return u.Eq(v);
        }
        public static bool operator !=(Matrix4 u, Matrix4 v)
        {
            return !u.Eq(v);
        }

        public override bool Equals(object obj)
        {
            return obj is Matrix4 && Eq((Matrix4)obj);
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


        public Matrix4 Transform(Matrix4 n)
        {
            return new Matrix4(new Vec4(Rotate(n.x.xyz)),new Vec4(Rotate(n.y.xyz)), new Vec4(Rotate(n.z.xyz)), new Vec4(Transform(n.w.xyz),1));
        }

        public Vec3 Transform(Vec3 v)
        {
            return x.xyz * v.X + y.xyz * v.Y + z.xyz * v.Z + w.xyz;
                //(this * new Vec4(v, 1)).XYZ;
        }
        public Vec3 Rotate(Vec3 v)
        {
            return x.xyz * v.X + y.xyz * v.Y + z.xyz * v.Z;
            //(this * new Vec4(v, 0)).XYZ;
        }

		public void CopyTo(float[] ar, int offset)
		{
			x.CopyTo(ar, offset);
			y.CopyTo(ar, offset + 4);
			z.CopyTo(ar, offset + 8);
			w.CopyTo(ar, offset + 16);
		}

		public Matrix4 Invert
		{
			get
			{
				float[] buffer = new float[2 * 4 * 4];
				CopyTo(buffer, 0);
				Identity.CopyTo(buffer, 16);
				for (int line = 0; line < 4; line++)
				{
					int targetline = line;
					while (Math.Abs(buffer[line * 4 + targetline]) <= float.Epsilon && targetline < 4)
						targetline++;
					if (targetline == 4)
						throw new InversionFailedException(this);
					if (targetline != line)
					{
						for (int i = 0; i < 8; i++)
						{
							int from = i * 4 + targetline;
							int to = i * 4 + line;
							float save = buffer[from];
							buffer[from] = buffer[to];
							buffer[to] = save;
						}
					}
					for (int i = line + 1; i < 4; i++)
					{
						float a = buffer[line * 4 + i] / buffer[line * 4 + line];
						for (int j = line + 1; j < 8; j++)
							buffer[j * 4 + i] -= buffer[j * 4 + line] * a;
						buffer[line * 4 + i] = 0;
					}
				}
				float[] result = new float[16];
				for (int i = 0; i < 4; i++)
					for (int line = 3; line >= 0; line--)
					{
						float a = 0;
						for (int inner = 0; inner < 4 - line; inner++)
							a += buffer[(inner + line) * 4 + line] * result[i*4+(inner+line)];
						if (buffer[line * 4 + line] == 0)
							throw new InversionFailedException(this);
						result[i*4+line]
							=
							(buffer[(4 + i) * 4 + line]-a)/buffer[line * 4 + line];
					}
				return new Matrix4(result);
			}
		}


        public static Matrix4 Assemble(Vec3 orientationX, Vec3 orientationY, Vec3 position)
        {
            return new Matrix4(new Vec4(orientationX.Normalized(), 0), new Vec4(orientationY.Normalized(), 0), new Vec4(Vec.Cross(orientationX, orientationY).Normalized(), 0), new Vec4(position, 1));
        }
    }
}
