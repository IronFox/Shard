using System.Collections;
using System.Collections.Generic;

namespace Shard
{
	public struct LazyList<T> : IEnumerable<T>
	{
		List<T> internalList;

		private class Empty : IEnumerator<T>
		{
			public T Current => throw new System.NotImplementedException();

			object IEnumerator.Current => throw new System.NotImplementedException();

			public void Dispose()
			{}

			public bool MoveNext()
			{
				return false;
			}

			public void Reset()
			{}
		}

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

		public IEnumerator<T> GetEnumerator()
		{
			if (internalList != null)
				return internalList.GetEnumerator();
			return new Empty();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			if (internalList != null)
				return internalList.GetEnumerator();
			return new Empty();
		}
	}
}
