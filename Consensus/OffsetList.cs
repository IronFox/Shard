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

		public void RemoveFrontIncreaseOffset(int count)
		{
			int delta = count - offset;
			if (delta <= 0)
				return;
			if (delta > innerList.Count)
				delta = innerList.Count;
			offset += delta;
			innerList.RemoveRange(0, delta);
		}
	}
}