using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{

	public static class Extensions
	{

		public static T Get<T>(this Task<T> task)
		{
			try
			{
				return task.Result;
			}
			catch (AggregateException ex)
			{
				throw ex.InnerException;
			}
		}

		public static T[] Subarray<T>(this T[] array, int offset)
		{
			if (array == null)
				return null;
			if (array.Length == 0 || offset <= 0)
				return array;
			if (offset >= array.Length)
				return new T[0];
			T[] rs = new T[array.Length - offset];
			for (int i = offset; i < array.Length; i++)
				rs[i - offset] = array[i];
			return rs;
		}

		public static bool TryRemove<K, T>(this ConcurrentDictionary<K, T> dict, K key)
		{
			T temp;
			return dict.TryRemove(key, out temp);
		}


		public static async Task DoLockedAsync(this SemaphoreSlim sem, Func<Task> action)
		{
			await sem.WaitAsync();
			try
			{
				await action();
				sem.Release();
			}
			catch
			{
				sem.Release();
				throw;
			}
		}
		public static async Task DoLockedAsync(this SemaphoreSlim sem, Action action)
		{
			await sem.WaitAsync();
			try
			{
				action();
				sem.Release();
			}
			catch
			{
				sem.Release();
				throw;
			}
		}

		public static void DoLocked(this SpinLock lck, Action action)
		{
			Helper.Enter(ref lck);    //if fails, throws exception, not locked, all good
			try
			{
				action();
				lck.Exit();
			}
			catch
			{
				lck.Exit();
				throw;
			}
		}




		public static int FloorDiv(this TimeSpan a, TimeSpan b)
		{
			return (int)Math.Floor(a.TotalSeconds / b.TotalSeconds);
		}

		public static TimeSpan NotNegative(this TimeSpan s)
		{
			if (s.Ticks >= 0)
				return s;
			return TimeSpan.FromTicks(0);
		}


		/// <summary>
		/// https://stackoverflow.com/questions/273313/randomize-a-listt
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="random"></param>
		/// <param name="list"></param>
		public static void Shuffle<T>(this Random random, IList<T> list)
		{
			int n = list.Count;
			while (n > 1)
			{
				n--;
				int k = random.Next(n + 1);
				T value = list[k];
				list[k] = list[n];
				list[n] = value;
			}
		}

		public static T GetLast<T>(this List<T> list)
		{
			return list[list.Count - 1];
		}

		public static T PickRandom<T>(this List<T> list, Random random)
		{
			return list[random.Next(list.Count)];
		}

		public static T PickRandom<T>(this T[] array, Random random)
		{
			return array[random.Next(array.Length)];
		}

		public static bool NextBool(this Random random)
		{
			return random.Next(2) == 1;
		}

		public static bool NextBool(this Random random, float p)
		{
			return random.NextDouble() <= p;
		}

		public static Bool3 NextBool3(this Random random)
		{
			return new Bool3(random.NextBool(), random.NextBool(), random.NextBool());
		}

		public static float NextFloat(this Random random, float min, float max)
		{
			return (float)random.NextDouble() * (max - min) + min;
		}
		public static float NextFloat(this Random random, Box.Range range)
		{
			return random.NextFloat(range.Min,range.InclusiveMax);
		}

		public static VectorMath.Vec3 NextVec3(this Random random, float min, float max)
		{
			return new VectorMath.Vec3(random.NextFloat(min, max), random.NextFloat(min, max), random.NextFloat(min, max));
		}
		public static VectorMath.Vec3 NextVec3(this Random random, Box cube)
		{
			return new VectorMath.Vec3(
				random.NextFloat(cube.X),
				random.NextFloat(cube.Y),
				random.NextFloat(cube.Z));
		}
		public static Int3 NextInt3(this Random random, int min, int exclusiveMax)
		{
			return new Int3(random.Next(min, exclusiveMax), random.Next(min, exclusiveMax), random.Next(min, exclusiveMax));
		}

		public static char NextChar(this Random random, string alphabet)
		{
			return alphabet[random.Next(alphabet.Length)];
		}

		public static byte[] NextBytes(this Random random, int minLength, int maxLength)
		{
			int length = random.Next(minLength, maxLength + 1);
			byte[] rs = new byte[length];
			random.NextBytes(rs);
			return rs;
		}

		public static string NextString(this Random random, string alphabet, int minLength = 3, int maxLength = 16)
		{
			int length = random.Next(minLength, maxLength + 1);
			char[] field = new char[length];
			for (int i = 0; i < length; i++)
				field[i] = random.NextChar(alphabet);
			return new string(field);
		}

		public static string NextString(this Random random, int minLength = 3, int maxLength = 16)
		{
			return random.NextString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 _",minLength,maxLength);
		}

		public static T[] ToArray<T>(this ICollection<T> collection)
		{
			T[] rs = new T[collection.Count];
			collection.CopyTo(rs, 0);
			return rs;
		}


		public static void ForceRemove<K,V>(this ConcurrentDictionary<K,V> dict, K key)
		{
			V temp;
			if (!dict.TryRemove(key, out temp))
				throw new IntegrityViolation("Unable to remove '"+key+"' from dictionary");
		}
		public static void ForceAdd<K, V>(this ConcurrentDictionary<K, V> dict, K key, V value)
		{
			if (!dict.TryAdd(key, value))
				throw new IntegrityViolation("Unable to add '" + key + "'=>'"+value+"' to dictionary");
		}




		public static void Read(this NetworkStream source, byte[] data, int bytes)
		{
			int offset = 0;
			int remaining = bytes;
			while (remaining > 0)
			{
				int read = source.Read(data, offset, remaining);
				if (read <= 0)
					throw new Exception("stream.Read() returned " + read);
				offset += read;
				remaining -= read;
			}
		}
		public static void Read(this NetworkStream source, byte[] buffer, Stream outData, int bytes)
		{
			int remaining = bytes;
			while (remaining > 0)
			{
				int read = source.Read(buffer, 0, Math.Min(remaining, buffer.Length));
				if (read <= 0)
					throw new Exception("stream.Read() returned " + read);
				remaining -= read;
				outData.Write(buffer, 0, read);
			}
		}



		public static T GetOrCreate<K, T>(this Dictionary<K, T> dict, K key)
		{
			T rs;
			if (dict.TryGetValue(key, out rs))
				return rs;
			rs = (T)Activator.CreateInstance(typeof(T));
			dict[key] = rs;
			return rs;
		}








		public static void Write(this Stream stream, string str)
		{
			var ar = Encoding.ASCII.GetBytes(str);
			stream.Write(ar, 0, ar.Length);
		}


	}
}
