using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using VectorMath;

namespace Shard
{
	public class SerialRCSStack : DB.Entity
	{
		public int[] NumericID;
		public RCS.SerialData[] Entries { get; set; }

		public struct Destination
		{
			public int OldestGeneration { get; set; }
			public int LastUpdateTimeStep { get; set; }
		}

		public Destination[] Destinations { get; set; }


		public static int GetNewestTimeStep(Destination[] fld)
		{
			int newest = 0;
			if (fld != null)
				foreach (var dest in fld)
					newest = Math.Max(newest, dest.LastUpdateTimeStep);
			return newest;
		}
		public int GetNewestTimeStep()
		{
			return GetNewestTimeStep(Destinations);
		}

		public static int GetOldestGeneration(Destination[] fld)
		{
			if (fld == null || fld.Length == 0)
				return 0;
			int newest = GetNewestTimeStep(fld);
			int oldest = int.MaxValue;
			foreach (var dest in fld)
				if (dest.LastUpdateTimeStep >= newest - 5)
					oldest = Math.Min(oldest, dest.OldestGeneration);
			return oldest;
		}

		public RCS.SerialData? FindGeneration(int generation)
		{
			int at = generation - GetOldestGeneration();
			return at < Count(Entries) ? (RCS.SerialData?)Entries[at] : null;
		}
		public int GetOldestGeneration()
		{
			return GetOldestGeneration(Destinations);
		}

		private static int Count<T>(T[] array)
		{
			return array != null ? array.Length : 0;
		}

		private int GetDestCount()
		{
			return Count(Destinations);
		}

		public void IncludeNewerVersion(SerialRCSStack other)
		{
			int myOldest = GetOldestGeneration();
			int otherOldest = other.GetOldestGeneration();

			int mc = GetDestCount();
			int oc = other.GetDestCount();
			int len = Math.Max(mc, oc);
			Destination[] newDest = new Destination[len];
			int merge = Math.Min(mc, oc);
			for (int i = 0; i < merge; i++)
				newDest[i] = Destinations[i].LastUpdateTimeStep > other.Destinations[i].LastUpdateTimeStep ? Destinations[i] : other.Destinations[i];
			for (int i = merge; i < len; i++)
				newDest[i] = i < mc ? Destinations[i] : other.Destinations[i];

			int newOldest = GetOldestGeneration(newDest);


			int mh = myOldest + Count(Entries) - newOldest;
			int oh = otherOldest + Count(other.Entries) - newOldest;
			int h = Math.Max(mh, oh);
			var newEntries = new RCS.SerialData[h];

			for (int i = 0; i < newEntries.Length; i++)
			{
				int mi = (i + newOldest) - myOldest;
				if (mi >= 0 && mi < mc)
					newEntries[i] = Entries[mi];
				else
				{
					int oi = (i + newOldest) - otherOldest;
					if (oi >= 0 && oi < oc)
						newEntries[i] = other.Entries[oi];
				}
			}

			Entries = newEntries;
			Destinations = newDest;
			_rev = other._rev;
		}
	}



	[Serializable()]
	public class RCS
	{

		public readonly InconsistencyCoverage IC;

		public readonly EntityChangeSet CS;

		public struct SerialData
		{
			public byte[] CS { get; set; }
			public InconsistencyCoverage.Serial IC { get; set; }
		}

		public class Serial
		{
			public readonly SerialData Data;
			public readonly int Generation;

			public Serial(RCS rcs, int generation)
			{
				Generation = generation;
				Data = rcs.Export();
			}
		}



		public RCS(SerialData rcs)
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
			return other != null && base.Equals(obj) && IC.Equals(other.IC) && CS.Equals(other.CS);
		}

		public override int GetHashCode()
		{
			return new Helper.HashCombiner().Add(base.GetHashCode()).Add(IC).GetHashCode();
		}
	}
}