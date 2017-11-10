using System;
using VectorMath;

namespace Shard
{

	public class RCS : EntityChangeSet
	{
		public new class Serial
		{
			public string _id, _rev;

			public EntityChangeSet.Serial CS { get; set; }
			public InconsistencyCoverage.Serial IC { get; set; }
			public int[] NumericID { get; set; }
			public int Generation { get; set; }
		}

		public readonly string Revision;
		public readonly InconsistencyCoverage IC;

		public RCS(Serial rcs) : base(rcs.Generation)
		{
			Revision = rcs._rev;
			IC = new InconsistencyCoverage(rcs.IC);
		}

		public RCS(int generation, EntityChangeSet localCS, SpaceCube cube, InconsistencyCoverage ic) : base(generation)
		{
			base.Load(localCS, cube);
			IC = ic;
		}

		public bool IsFullyConsistent { get { return !IC.AnySet; } }

		public struct ID
		{
			public readonly Int3 FromShard, ToShard;
			public const int ExportInts = 6;

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

			public void Export(int[] ar, int offset)
			{
				FromShard.Export(ar, offset);
				ToShard.Export(ar, offset+3);
			}
			public int[] IntArray
			{
				get
				{
					int[] rs = new int[ExportInts];
					Export(rs, 0);
					return rs;
				}
			}
		}

		public struct GenID
		{
			public readonly ID ID;
			public readonly int Generation;
			public const int ExportInts = ID.ExportInts + 1;

			public GenID(Int3 fromShard, Int3 toShard, int generation)
			{
				ID = new ID(fromShard, toShard);
				Generation = generation;
			}

			public GenID(ID myID, int generation)
			{
				ID = myID;
				Generation = generation;
			}

			public override string ToString()
			{
				return ID + "g" + Generation;
			}
			public override int GetHashCode()
			{
				return ID.GetHashCode() * 31 +  Generation.GetHashCode();
			}

			public static bool operator ==(GenID a, GenID b)
			{
				return a.ID == b.ID && a.Generation == b.Generation;
			}
			public static bool operator !=(GenID a, GenID b)
			{
				return !(a == b);
			}

			public override bool Equals(object obj)
			{
				return (obj is GenID) && ((GenID)obj) == (this);
			}

			public void Export(int[] ar, int offset)
			{
				ID.Export(ar, offset);
				ar[offset + ID.ExportInts] = Generation;
			}

			public int[] IntArray
			{
				get
				{
					int[] rs = new int[ExportInts];
					Export(rs, 0);
					return rs;
				}
			}
		}

		public Serial Export(ID myID)
		{
			var genID = new GenID(myID, Generation);
			Serial rs = new Serial();
			rs.Generation = Generation;
			rs.NumericID = genID.IntArray;
			rs.IC = IC.Export();
			rs.CS = base.Export();

			rs._id = genID.ToString();
			rs._rev = Revision;

			return rs;
		}
	}
}