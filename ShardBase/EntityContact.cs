using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	[Serializable]
	public struct EntityContact : IComparable<EntityContact>
	{
		public readonly EntityID ID;
		public readonly EntityAppearanceCollection Appearances;
		public readonly Vec3 Velocity;

		public EntityContact(EntityID id, EntityAppearanceCollection appearances, Vec3 velocity)
		{
			ID = id;
			Appearances = appearances;
			Velocity = velocity;
		}


		public int CompareTo(EntityContact other)
		{
			return new Helper.Comparator()
				.Append(ID, other.ID)
				.Append(Appearances, other.Appearances)
				.Append(Velocity, other.Velocity)
				.Finish();
		}

		public override bool Equals(object obj)
		{
			if (!(obj is EntityContact))
			{
				return false;
			}

			var contact = (EntityContact)obj;
			return ID == contact.ID &&
				   Equals(Appearances, contact.Appearances)
				   && Velocity == contact.Velocity
				   ;
		}

		public override int GetHashCode()
		{
			var hashCode = 2035686911;
			hashCode = hashCode * -1521134295 + ID.GetHashCode();
			hashCode = hashCode * -1521134295 + Appearances.GetHashCode();
			hashCode = hashCode * -1521134295 + Velocity.GetHashCode();
			return hashCode;
		}
	}


}
