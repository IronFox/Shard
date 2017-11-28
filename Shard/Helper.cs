using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Shard
{
	public static class Helper
	{
		public static float Max(float a, float b, float c)
		{
			return Math.Max(Math.Max(a, b), c);
		}
		public static int Max(int a, int b, int c)
		{
			return Math.Max(Math.Max(a, b), c);
		}

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

		public static bool AreEqual<T>(T[] a0, T[] a1)
		{
			int len0 = Length(a0);
			int len1 = Length(a1);
			if (len0 != len1)
				return false;
			for (int i = 0; i < len0; i++)
				if (!a0[i].Equals(a1[i]))
					return false;
			return true;
		}

		public static string ToString(byte[] a)
		{
			string hex = BitConverter.ToString(a);
			return '['+ hex.Replace("-", "")+']';
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
			public Comparator Append<T>(T[] a, T[] b) where T : IComparable<T>
			{
				if (state == 0)
				{
					int lenA = Length(a);
					int lenB = Length(b);
					if (lenA < lenB)
						state = -1;
					else if (lenA > lenB)
						state = 1;
					else
						for (int i = 0; i < lenA && state == 0; i++)
							Append(a[i], b[i]);
				}
				return this;
			}

			public Comparator Append<T>(IList<T> a, IList<T> b) where T : IComparable<T>
			{
				if (state == 0)
				{
					int lenA = Length(a);
					int lenB = Length(b);
					if (lenA < lenB)
						state = -1;
					else if (lenA > lenB)
						state = 1;
					else
						for (int i = 0; i < lenA && state == 0; i++)
							Append(a[i], b[i]);
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

		public class MyCouchSerializer
		{
			MyCouch.Serialization.ISerializer serializer;

			public MyCouchSerializer()
			{
				//				serializer = new MyCouch.Serialization.DefaultSerializer(new MyCouch.Serialization.SerializationConfiguration();
				using (var cl = new MyCouch.MyCouchClient("https://127.0.0.1", "none"))
				{
					serializer = cl.Entities.Serializer;
				}
				Console.WriteLine(serializer.GetType());
			}

			public T Deserialize<T>(string data)
			{
				return serializer.Deserialize<T>(data);
			}

			public T Deserialize<T>(Stream data)
			{
				return serializer.Deserialize<T>(data);
			}

			public void Populate<T>(T item, Stream data) where T : class
			{
				serializer.Populate(item, data);
			}

			public void Populate<T>(T item, string json) where T : class
			{
				serializer.Populate(item, json);
			}

			public string Serialize<T>(T item) where T : class
			{
				return serializer.Serialize(item);
			}

		}

		public static int Length<T>(T[] array)
		{
			return array != null ? array.Length : 0;
		}
		public static int Length<T>(IList<T>list)
		{
			return list != null ? list.Count : 0;
		}
	}
}