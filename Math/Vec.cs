using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VectorMath
{
    public static class Vec
    {
        public static float Dot(Vec2 u, Vec2 v)
        {
            return u.x * v.x + u.y * v.y;
        }
        public static float Dot(Vec3 u, Vec3 v)
        {
            return u.X * v.X + u.Y * v.Y + u.Z * v.Z;
        }
        public static float Dot(Vec4 u, Vec4 v)
        {
            return u.x * v.x + u.y * v.y + u.z * v.z + u.w * v.w;
        }

        public static float Sqr(Vec2 v)
        {
            return v.x * v.x + v.y * v.y;
        }
        public static float Sqr(Vec3 v)
        {
            return v.X * v.X + v.Y * v.Y + v.Z * v.Z;
        }
        public static float Sqr(Vec4 v)
        {
            return v.x * v.x + v.y * v.y + v.z * v.z + v.w * v.w;
        }

        public static Vec3 Cross(Vec3 u, Vec3 v)
        {
            return new Vec3(u.Y * v.Z - u.Z * v.Y, u.Z * v.X - u.X * v.Z, u.X * v.Y - u.Y * v.X);
        }

        public static float Distance(Vec3 u, Vec3 v)
        {
            return (u - v).Length;
        }

        public static float QuadraticDistance(Vec3 u, Vec3 v)
        {
            return Sqr(u - v);
        }
    }
}
