using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Shard
{
	public static class Helper
	{
		public static T[] ToArray<T>(ICollection<T> collection)
		{
			T[] rs = new T[collection.Count];
			collection.CopyTo(rs, 0);
			return rs;
		}

		public static T[] ToArray<T>(IEnumerable<T> enumerable)
		{
			List<T> list = new List<T>();
			foreach (var e in enumerable)
				list.Add(e);
			return list.ToArray();
		}

		[StructLayout(LayoutKind.Explicit)]
		public struct FloatAndUIntUnion
		{
			[FieldOffset(0)]
			public int Int32Bits;
			[FieldOffset(0)]
			public float FloatValue;
		}

		public static float IntToFloat(int v)
		{
			FloatAndUIntUnion f2i = default(FloatAndUIntUnion);
			f2i.Int32Bits = v; 
			return f2i.FloatValue;
		}

		public static int FloatToInt2(float f)
		{
			FloatAndUIntUnion f2i = default(FloatAndUIntUnion);
			f2i.FloatValue = f;    // write as float
			return f2i.Int32Bits;    // read back as int			
		}


		public static int FloatToInt(float f)
		{
			return f.GetHashCode();
		}


		public static unsafe bool AreEqual(byte[] a1, byte[] a2)
		{
			if (a1 == a2) return true;
			if (a1 == null || a2 == null || a1.Length != a2.Length)
				return false;
			fixed (byte* p1 = a1, p2 = a2)
			{
				byte* x1 = p1, x2 = p2;
				int l = a1.Length;
				int l8 = l / 8;
				for (int i = 0; i < l8; i++, x1 += 8, x2 += 8)
					if (*((long*)x1) != *((long*)x2)) return false;
				if ((l & 4) != 0) { if (*((int*)x1) != *((int*)x2)) return false; x1 += 4; x2 += 4; }
				if ((l & 2) != 0) { if (*((short*)x1) != *((short*)x2)) return false; x1 += 2; x2 += 2; }
				if ((l & 1) != 0) if (*((byte*)x1) != *((byte*)x2)) return false;
				return true;
			}
		}


		internal class Comparator
		{
			int state = 0;
			public Comparator()
			{
			}

			public Comparator Append<T>(T a, T b) where T : IComparable<T>
			{
				if (state == 0)
				{
					if (a == null && b == null)
						return this;
					if (a == null && b != null)
						state = -1;
					else
						if (a != null && b == null)
						state = 1;
					else
						state = a.CompareTo(b);
				}
				return this;
			}

			public Comparator Append(int comparisonResult)
			{
				if (state == 0)
					state = comparisonResult;
				return this;
			}

			public Comparator Append(byte[] a, byte[] b)
			{
				if (state == 0)
				{
					if (a == null && b == null)
						return this;
					if (a == null && b != null)
						state = -1;
					else
					if (a != null && b == null)
						state = 1;
					else
					{
						if (a.Length < b.Length)
							state = -1;
						else
							if (a.Length > b.Length)
							state = 1;
						else
						{
							for (int i = 0; i < a.Length; i++)
								if (a[i] < b[i])
								{
									state = -1;
									break;
								}
							else
								if (a[i] > b[i])
								{
									state = 1;
									break;
								}
						}
					}
				}
				return this;
				//return this;
			}

			public int Finish()
			{
				return state;
			}
		}

		internal class HashCombiner
		{
			int hashCode = 2035686911;

			public HashCombiner()
			{
			}

			public HashCombiner Add(int hashCode)
			{
				this.hashCode = this.hashCode * -1521134295 + hashCode;
				return this;
			}

			public HashCombiner Add<T>(T obj)
			{
				return Add(obj.GetHashCode());
			}

			public override int GetHashCode()
			{
				return hashCode;
			}
		}
	}
}