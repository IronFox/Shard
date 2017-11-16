using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using VectorMath;

namespace Shard
{
	[Serializable()]
	public class RCS
	{
		public new class Serial
		{
			public string _id;

			public byte[] CS { get; set; }
			public InconsistencyCoverage.Serial IC { get; set; }
			public int[] NumericID { get; set; }
			public int Generation { get; set; }
		}

		public readonly InconsistencyCoverage IC;

		public readonly EntityChangeSet CS;


		public RCS(Serial rcs)
		{
			IC = new InconsistencyCoverage(rcs.IC);

			using (var ms = new MemoryStream(rcs.CS))
			{
				CS = (EntityChangeSet) new BinaryFormatter().Deserialize(ms);
			}
		}

		public RCS(EntityChangeSet cs, InconsistencyCoverage ic)
		{
			CS = cs;
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

			public ID(int[] numericID, int offset)
			{
				FromShard = new Int3(numericID, offset);
				ToShard = new Int3(numericID, offset + 3);
			}

			public override string ToString()
			{
				return FromShard.Encoded + "->" + ToShard.Encoded;
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

			public GenID(int[] numericID, int offset)
			{
				ID  = new ID(numericID,offset);
				Generation = numericID[offset + ID.ExportInts];
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

		public Serial Export(GenID genID)
		{
			Serial rs = new Serial();
			rs.Generation = genID.Generation;
			rs.NumericID = genID.IntArray;
			rs.IC = IC.Export();
			using (var ms = new MemoryStream())
			{
				new BinaryFormatter().Serialize(ms, CS);
				rs.CS = ms.ToArray();
			}

			rs._id = genID.ToString();

			return rs;
		}


		public override bool Equals(object obj)
		{
			if (obj == this)
				return true;
			var other = obj as RCS;
			return other != null && base.Equals(obj) && IC.Equals(other.IC) && CS.Equals(other.CS);
		}

		public override int GetHashCode()
		{
			return new Helper.HashCombiner().Add(base.GetHashCode()).Add(IC).GetHashCode();
		}
	}
}