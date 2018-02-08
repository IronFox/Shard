#if STATE_ADV

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	[Serializable]
	public abstract class EntityAppearance : IComparable<EntityAppearance>
	{
		public abstract int CompareTo(EntityAppearance other);
		public override bool Equals(object obj)
		{
			EntityAppearance other = obj as EntityAppearance;
			return other != null && CompareTo(other) == 0;
		}
		public override abstract int GetHashCode();
	}

	[Serializable]
	public class EntityAppearanceCollection : IComparable<EntityAppearanceCollection>, ISerializable, IEnumerable<EntityAppearance>
	{
		private SortedList<Type, EntityAppearance> members = new SortedList<Type, EntityAppearance>();

		public void Add(EntityAppearance app)
		{
			Type t = app.GetType();
			if (!t.IsSerializable)
				throw new IntegrityViolation("Trying to add non-serializable appearance to collection: " + t);
			if (Contains(t))
				throw new IntegrityViolation("This appearance already exists in this collection");
			members.Add(t, app);
		}
		public void AddOrReplace(EntityAppearance app)
		{
			Type t = app.GetType();
			if (!t.IsSerializable)
				throw new IntegrityViolation("Trying to add non-serializable appearance to collection: " + t);
			members[app.GetType()] = app;
		}

		public bool Remove(Type t)
		{
			return members.Remove(t);
		}

		public bool Remove<T>()
		{
			return members.Remove(typeof(T));
		}

		public void Clear()
		{
			members.Clear();
		}


		public int CompareTo(EntityAppearanceCollection other)
		{
			var h = new Helper.Comparator();
			h.Append(members.Values, other.members.Values);
			return h.Finish();
		}

		public bool Contains(Type appearanceType)
		{
			return members.ContainsKey(appearanceType);
		}

		public bool Contains<T>() where T : EntityAppearance
		{
			return members.ContainsKey(typeof(T));
		}

		public T Get<T>() where T : EntityAppearance
		{
			EntityAppearance rs;
			if (!members.TryGetValue(typeof(T), out rs))
				return null;
			return (T)rs;
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("members", members.Values.ToArray());
		}

		public EntityAppearanceCollection()
		{ }

		public EntityAppearanceCollection(SerializationInfo info, StreamingContext context)
		{
			EntityAppearance[] field = (EntityAppearance[])info.GetValue("members", typeof(EntityAppearance[]));
			foreach (var a in field)
				Add(a);
		}


		public override string ToString()
		{
			if (members.Count == 0)
				return "{}";
			if (members.Count == 1)
				return members.Values[0].ToString();

			StringBuilder builder = new StringBuilder();


			foreach (var a in members.Values)
			{
				if (builder.Length != 0)
					builder.Append(',');
				builder.Append(a.ToString());
			}

			return "{" + builder.ToString() + "}";
		}

		public override bool Equals(object obj)
		{
			var other = obj as EntityAppearanceCollection;
			return other != null && CompareTo(other) == 0;
		}

		public override int GetHashCode()
		{
			var h = Helper.Hash(this);
			foreach (var app in members.Values)
				h.Add(app);
			return h.GetHashCode();
		}

		public IEnumerator<EntityAppearance> GetEnumerator()
		{
			return members.Values.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return members.Values.GetEnumerator();
		}

		public EntityAppearanceCollection Duplicate()
		{
			return (EntityAppearanceCollection)Helper.Deserialize(Helper.SerializeToArray(this));
		}
	}
}

#endif
