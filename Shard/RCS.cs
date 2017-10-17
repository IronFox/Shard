using VectorMath;

namespace Shard
{
	public class RCS
	{
		public struct ID
		{
			public readonly Int3 fromShard, toShard;
			public readonly int generation;

			public string Key {
				get
				{
					return fromShard.Encoded + '-' + toShard.Encoded + 'g'+(generation%100);
				}
			}

			public ID(Int3 fromShard, Int3 toShard, int generation)
			{
				this.fromShard = fromShard;
				this.toShard = toShard;
				this.generation = generation;
			}

			public override string ToString()
			{
				return fromShard+"-"+toShard + "g" + generation;
			}
			public override int GetHashCode()
			{
				return (fromShard.GetHashCode() * 31 + toShard.GetHashCode()) * 31 +  generation.GetHashCode();
			}

			public static bool operator ==(ID a, ID b)
			{
				return a.fromShard == b.fromShard && a.toShard == b.toShard && a.generation == b.generation;
			}
			public static bool operator !=(ID a, ID b)
			{
				return !(a == b);
			}

			public override bool Equals(object obj)
			{
				return (obj is ID) && ((ID)obj) == (this);
			}
		}


	}
}