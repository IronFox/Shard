﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using VectorMath;

namespace Shard
{


	[Serializable()]
	public class RCS
	{

		public readonly InconsistencyCoverage IC;

		public readonly EntityChangeSet CS;

		[Serializable]
		public struct SerialData
		{
			public byte[] CS { get; set; }
			public BitCube.DBSerial IC { get; set; }

			public override bool Equals(object obj)
			{
				if (!(obj is SerialData))
					return false;
				var other = (SerialData)obj;
				return Helper.AreEqual(CS, other.CS) && IC.Equals(other.IC);
			}

			public override int GetHashCode()
			{
				return Helper.Hash(this).Add(CS).Add(IC).GetHashCode();
			}

			public bool IsUndefined()
			{
				return CS == null || IC.IsEmpty;
			}
			public bool IsDefined()
			{
				return !IsUndefined();
			}

			public override string ToString()
			{
				return "CS["+Helper.Length(CS)+"], IC="+IC;
			}
		}

		[Serializable]
		public class Serial
		{
			public SerialData Data { get; set; }
			public int Generation { get; set; }

			public Serial(RCS rcs, int generation)
			{
				Generation = generation;
				Data = rcs.Export();
			}

			public override bool Equals(object obj)
			{
				Serial other = obj as Serial;
				if (other == null)
					return false;
				return Generation == other.Generation && Data.Equals(other.Data);
			}

			public override int GetHashCode()
			{
				return Helper.Hash(this).Add(Data).Add(Generation).GetHashCode();
			}

			public override string ToString()
			{
				return "g"+Generation+":"+Data;
			}
		}



		public RCS(SerialData rcs)
		{
			IC = new InconsistencyCoverage(rcs.IC);
			if (rcs.CS == null)
				CS = new EntityChangeSet();
			else
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

		public SerialData Export()
		{
			var rs = new SerialData();
			rs.IC = IC.Export();
			using (var ms = new MemoryStream())
			{
				new BinaryFormatter().Serialize(ms, CS);
				rs.CS = ms.ToArray();
			}
			return rs;
		}


		public override bool Equals(object obj)
		{
			if (obj == this)
				return true;
			var other = obj as RCS;
			if (other == null)
				return false;
			if (!IC.Equals(other.IC))
				return false;
			return CS.Equals(other.CS);
		}

		public override int GetHashCode()
		{
			return Helper.Hash(this).Add(base.GetHashCode()).Add(IC).GetHashCode();
		}
	}
}