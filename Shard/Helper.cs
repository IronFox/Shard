using System;
using System.Collections.Generic;

namespace Shard
{
	internal static class Helper
	{
		internal static T[] ToArray<T>(ICollection<T> collection)
		{
			T[] rs = new T[collection.Count];
			collection.CopyTo(rs, 0);
			return rs;
		}
	}
}