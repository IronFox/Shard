using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Shard
{

	[Serializable()]
	public class Deque<T> : ICollection<T>, IEnumerable<T>
	{
		private LinkedList<T> container = new LinkedList<T>();

		public int Count => container.Count;

		public bool IsReadOnly => false;

		public void Add(T item)
		{
			container.AddLast(item);
		}

		public void AddFront(T item)
		{
			container.AddFirst(item);
		}

		public T Pop()
		{
			T rs = container.First.Value;
			container.RemoveFirst();
			return rs;
		}

		public T Front
		{
			get
			{
				return container.First.Value;
			}
		}
		public T Back
		{
			get
			{
				return container.Last.Value;
			}
		}

		public void RemoveFront()
		{
			container.RemoveFirst();
		}
		public void RemoveBack()
		{
			container.RemoveLast();
		}

		public T this[int index]
		{
			get
			{
				return container.Skip(index).First();
			}
		}

		public void Clear()
		{
			container.Clear();
		}

		public bool Contains(T item)
		{
			return container.Contains(item);
		}

		public void CopyTo(T[] array, int arrayIndex)
		{
			int at = arrayIndex;
			foreach (var it in container)
				array[at++] = it;
		}

		public IEnumerator<T> GetEnumerator()
		{
			return container.GetEnumerator();
		}

		public bool Remove(T item)
		{
			return container.Remove(item);
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return container.GetEnumerator();
		}


	}

}