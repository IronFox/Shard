using System;
using VectorMath;

namespace Shard
{

	public class RCS
	{
		public class Serial
		{
			public int Generation { get; set; }
		}

		public readonly int Generation;

		public RCS(Serial rcs)
		{
			Generation = rcs.Generation;
		}

		public InconsistencyCoverage IC { get; private set; }
		public bool IsFullyConsistent { get { return IC.IsFullyConsistent; } }

		public struct ID
		{
			public readonly Int3 FromShard, ToShard;

			public ID(Int3 fromShard, Int3 toShard)
			{
				FromShard = fromShard;
				ToShard = toShard;
			}

			public override string ToString()
			{
				return FromShard.Encoded + '-' + ToShard.Encoded;
			}

			public override int GetHashCode() => (FromShard.GetHashCode() * 31 + ToShard.GetHashCode());
			public static bool operator ==(ID a, ID b) => a.FromShard == b.FromShard && a.ToShard == b.ToShard;
			public static bool operator !=(ID a, ID b) => !(a == b);
			public override bool Equals(object obj) => (obj is ID) && ((ID)obj) == (this);
		}

		public struct IDG
		{
			public readonly ID ID;
			public readonly int Generation;

			public string Key {
				get
				{
					return ID + "g"+(Generation%100);
				}
			}
			public string P2PKey
			{
				get
				{
					return ID + "g" + Generation;
				}
			}

			public IDG(Int3 fromShard, Int3 toShard, int generation)
			{
				ID = new ID(fromShard, toShard);
				Generation = generation;
			}

			public override string ToString()
			{
				return P2PKey;
			}
			public override int GetHashCode()
			{
				return ID.GetHashCode() * 31 +  Generation.GetHashCode();
			}

			public static bool operator ==(IDG a, IDG b)
			{
				return a.ID == b.ID && a.Generation == b.Generation;
			}
			public static bool operator !=(IDG a, IDG b)
			{
				return !(a == b);
			}

			public override bool Equals(object obj)
			{
				return (obj is IDG) && ((IDG)obj) == (this);
			}
		}

		public Serial Export()
		{
			throw new NotImplementedException();
		}
	}
}