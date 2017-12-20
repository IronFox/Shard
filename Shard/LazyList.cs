using System;
using System.Collections;
using System.Collections.Generic;

namespace Shard
{
	public struct LazyList<T> : IEnumerable<T>, ICollection<T>, IList<T>,IReadOnlyList<T>
	{
		List<T> internalList;

		//private class Empty : IEnumerator<T>
		//{
		//	public T Current => throw new System.NotImplementedException();

		//	object IEnumerator.Current => throw new System.NotImplementedException();

		//	public void Dispose()
		//	{}

		//	public bool MoveNext()
		//	{
		//		return false;
		//	}

		//	public void Reset()
		//	{}
		//}

		public T this[int index]
		{
			get
			{
				return internalList[index];
			}
			set
			{
				internalList[index] = value;
			}
		}


		public void Add(T item)
		{
			(internalList ?? (internalList = new List<T>())).Add(item);
		}

		public int Count { get { return internalList != null ? internalList.Count : 0; } }

		public List<T> InternalList { get { return internalList;  } }

		public bool IsNotEmpty { get { return internalList != null; } }
		public bool IsEmpty { get { return internalList == null; } }

		public bool IsReadOnly { get { return false; } }

		public void Clear()
		{
			internalList = null;
		}

		public void Sort()
		{
			if (internalList != null)
				internalList.Sort();
		}
		public void Sort(Comparison<T> comp)
		{
			if (internalList != null)
				internalList.Sort(comp);
		}

		

		public IEnumerator<T> GetEnumerator()
		{
			if (internalList != null)
				return internalList.GetEnumerator();
			return Helper.Enumerable<T>.EmptyEnumerator;
				//new Empty();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			if (internalList != null)
				return internalList.GetEnumerator();
			return Helper.Enumerable<T>.EmptyEnumerator;
			//return new Empty();
		}

		public bool Contains(T item)
		{
			return internalList != null && internalList.Contains(item);
		}

		public void CopyTo(T[] array, int arrayIndex)
		{
			if (internalList != null)
				internalList.CopyTo(array, arrayIndex);
		}

		public bool Remove(T item)
		{
			if (internalList != null)
				return internalList.Remove(item);
			return false;
		}

		public int IndexOf(T item)
		{
			if (internalList != null)
				return internalList.IndexOf(item);
			return -1;
		}

		public void Insert(int index, T item)
		{
			if (internalList == null)
			{
				if (index == 0)
				{
					Add(item);
					return;
				}
				throw new ArgumentOutOfRangeException("index", index, "Not in [0,0]");
			}
			internalList.Insert(index, item);
		}

		public void RemoveAt(int index)
		{
			if (internalList == null)
				throw new ArgumentOutOfRangeException("index", index, "List is empty");
			internalList.RemoveAt(index);
		}


		public T Last
		{
			get
			{
				if (internalList == null)
					throw new ArgumentOutOfRangeException("List is empty");
				return internalList[internalList.Count - 1];
			}
		}

		public T First
		{
			get
			{
				if (internalList == null)
					throw new ArgumentOutOfRangeException("List is empty");
				return internalList[0];
			}
		}
	}
}
