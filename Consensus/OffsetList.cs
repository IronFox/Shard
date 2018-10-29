using System;
using System.Collections;
using System.Collections.Generic;

namespace Consensus
{
	internal class OffsetList<T> : IList<T>
	{
		private int offset=0;
		private readonly List<T> innerList = new List<T>();

		public int Count => innerList.Count + offset;

		public bool IsReadOnly => false;

		public int Offset => offset;

		public int ActualEntryCount => innerList.Count;

		public T this[int index] { get => index >= offset ? innerList[index-offset] : default(T);  set { if (index >= offset) innerList[index - offset] = value; else throw new ArgumentOutOfRangeException("index",index,"Not in ["+offset+","+Count+")"); } }

		public int IndexOf(T item)
		{
			int at = innerList.IndexOf(item);
			if (at >= 0)
				return at + offset;
			return -1;
		}

		public void Insert(int index, T item)
		{
			innerList.Insert(index - offset, item);
		}

		public void RemoveAt(int index)
		{
			if (index < offset)
			{
				offset--;
				return;
			}
			innerList.RemoveAt(index - offset);
		}

		public void Add(T item)
		{
			innerList.Add(item);
		}

		public void Clear()
		{
			innerList.Clear();
			offset = 0;
		}

		public bool Contains(T item)
		{
			return innerList.Contains(item);
		}

		public bool Contains(Func<T, bool> filter)
		{
			foreach (var p in innerList)
				if (filter(p))
					return true;
			return false;
		}

		public void CopyTo(T[] array, int arrayIndex)
		{
			innerList.CopyTo(array, arrayIndex + offset);
		}

		public bool Remove(T item)
		{
			return innerList.Remove(item);
		}

		public IEnumerator<T> GetEnumerator()
		{
			return innerList.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return innerList.GetEnumerator();
		}

		public bool IsWellFormed => innerList.Count > 0;

		/// <summary>
		/// Removes elements from the front of the internal list, and simulataneously moves the internal offset up.
		/// If the local offset already matches or exceeds the given number of elements, then no change occurs.
		/// Unless the number of skiped elements exceeds the boundaries of the local list, this.Count remains unchanged.
		/// </summary>
		/// <param name="count">Number of elements to skip. Unless <paramref name="ignoreBoundaries"/> is set, must be less than this.Count</param>
		/// <param name="ignoreBoundaries">Set true to allow offsets beyond the current maximum, thus potentially increasing Count</param>
		public void SetOffset(int count, bool ignoreBoundaries=false)
		{
			int delta = count - offset;
			if (delta <= 0)
				return;
			if (delta >= innerList.Count && !ignoreBoundaries)
				throw new ArgumentOutOfRangeException("Trying to skip past the number of stored elements: skipTo="+count+", stored elements="+Count);
			offset += delta;
			innerList.RemoveRange(0, Math.Min(innerList.Count,delta));
		}
	}
}