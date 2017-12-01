using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using VectorMath;

namespace Shard
{
	public class SerialRCSStack : DB.Entity
	{
		public int[] NumericID;
		public RCS.SerialData[] Entries { get; set; }

		/// <summary>
		/// Maximum difference from the time-step of the most up-to-date destination for a destination to be included
		/// </summary>
		public const int MaxDestinationAgeTolerance = 5;

		public struct Destination
		{
			public int OldestGeneration { get; set; }
			public int LastUpdateTimeStep { get; set; }


			public static Destination Newest(Destination a, Destination b)
			{
				if (a.LastUpdateTimeStep == b.LastUpdateTimeStep)
					return a.OldestGeneration > b.OldestGeneration ? a : b;
				return a.LastUpdateTimeStep > b.LastUpdateTimeStep ? a : b;
			}

			public static bool operator ==(Destination a, Destination b)
			{
				return a.OldestGeneration == b.OldestGeneration && a.LastUpdateTimeStep == b.LastUpdateTimeStep;
			}
			public static bool operator !=(Destination a, Destination b)
			{
				return !(a == b);
			}


			public override bool Equals(object obj)
			{
				return obj is Destination && ((Destination)obj) == this;
			}

			public override int GetHashCode()
			{
				return Helper.Hash(this).Add(OldestGeneration).Add(LastUpdateTimeStep).GetHashCode();
			}

			public override string ToString()
			{
				return "old="+OldestGeneration+",up="+LastUpdateTimeStep;
			}
		}

		public struct DestinationTable
		{

			public DestinationTable(int size) : this()
			{
				if (size > 0)
					All = new Destination[size];
			}

			public Destination[] All { get; set; }

			public int GetOldestGeneration()
			{
				if (All == null || All.Length == 0)
					return 0;
				int newest = GetLatestUpdateTimeStep();
				int oldest = int.MaxValue;
				foreach (var dest in All)
				{
					if (dest.LastUpdateTimeStep < 0)
						throw new IntegrityViolation("Last update time step is negative: "+ dest.LastUpdateTimeStep);
					if (dest.LastUpdateTimeStep >= newest - MaxDestinationAgeTolerance)
						oldest = Math.Min(oldest, dest.OldestGeneration);
				}
				if (oldest == int.MaxValue)
					throw new IntegrityViolation("newest ("+newest+") not contained by destination field ["+Count()+"]");
				return oldest;
			}
			public int GetLatestUpdateTimeStep()
			{
				int newest = 0;
				if (All != null)
					foreach (var dest in All)
						newest = Math.Max(newest, dest.LastUpdateTimeStep);
				return newest;
			}

			public int Count()
			{
				return All != null ? All.Length : 0;
			}

			public static DestinationTable Merge(DestinationTable a, DestinationTable b)
			{
				int oldestA = a.GetOldestGeneration();
				int oldestB = b.GetOldestGeneration();

				int destCountA = a.Count();
				int destCountB = b.Count();
				int destCount = Math.Max(destCountA, destCountB);
				var newDest = new Destination[destCount];
				int destMergeCount = Math.Min(destCountA, destCountB);
				for (int i = 0; i < destMergeCount; i++)
					newDest[i] = Destination.Newest(a.All[i], b.All[i]);
				for (int i = destMergeCount; i < destCount; i++)
					newDest[i] = i < destCountA ? a.All[i] : b.All[i];

				return new DestinationTable() { All = newDest };
			}

			public void Set(int replicationIndex, int simulationTopGeneration, int oldestGeneration)
			{
				int old = Count();
				if (old <= replicationIndex)
				{
					var nd = new SerialRCSStack.Destination[replicationIndex + 1];
					for (int i = 0; i < old; i++)
						nd[i] = All[i];
					All = nd;
				}
				All[replicationIndex].LastUpdateTimeStep = simulationTopGeneration;
				All[replicationIndex].OldestGeneration = oldestGeneration;
			}

			internal void AppendTo(StringBuilder builder)
			{
				for (int i = 0; i < Count(); i++)
				{
					if (i != 0)
						builder.Append(',');
					builder.Append(All[i]);
				}
			}

			public override bool Equals(object obj)
			{
				if (!(obj is DestinationTable))
					return false;
				DestinationTable t = (DestinationTable)obj;
				return t == this;
			}

			public static bool operator==(DestinationTable a, DestinationTable b)
			{
				if (a.Count() != b.Count())
					return false;
				for (int i = 0; i < a.Count(); i++)
					if (a.All[i] != b.All[i])
						return false;
				return true;
			}
			public static bool operator !=(DestinationTable a, DestinationTable b)
			{
				return !(a == b);
			}



			public override int GetHashCode()
			{
				var h = Helper.Hash(this);
				for (int i = 0; i < Count(); i++)
					h.Add(All[i]);
				return h.GetHashCode();
			}

			public override string ToString()
			{
				StringBuilder builder = new StringBuilder();
				AppendTo(builder);
				return builder.ToString();
			}
		}

		public int GetNewestGeneration()
		{
			return GetOldestGeneration() + CountEntries() - 1;
		}

		public int CountEntries()
		{
			return Count(Entries);
		}

		public DestinationTable Destinations;



		public int GetLatestUpdateTimeStep()
		{
			return Destinations.GetLatestUpdateTimeStep();
		}


		public RCS.SerialData? FindGeneration(int generation)
		{
			int at = generation - GetOldestGeneration();
			return at < Count(Entries) ? (RCS.SerialData?)Entries[at] : null;
		}
		public int GetOldestGeneration()
		{
			return Destinations.GetOldestGeneration();
		}

		private static int Count<T>(T[] array)
		{
			return array != null ? array.Length : 0;
		}


		public static SerialRCSStack Merge(SerialRCSStack a, SerialRCSStack b)
		{
			if (a._id != b._id)
				throw new IntegrityViolation("Trying to merge two unrelated stacks");
			if (a._rev == b._rev && a._rev != null)
				return a;

			DestinationTable dest = DestinationTable.Merge(a.Destinations, b.Destinations);

			int newOldest = dest.GetOldestGeneration();


			int oldestA = a.GetOldestGeneration();
			int oldestB = b.GetOldestGeneration();
			Console.WriteLine(oldestA+"/"+oldestB+"->"+newOldest);

			int historyA = oldestA + Count(a.Entries) - newOldest;
			int historyB = oldestB + Count(b.Entries) - newOldest;
			int history = Helper.Max(historyA, historyB,0);

			if (history > 100)
				throw new Exception("History too long for oldest=" + newOldest + ": " + history + ", max of oldestA=" + oldestA + ", oldestB=" + oldestB);

			var newEntries = new RCS.SerialData[history];
			int entriesA = Count(a.Entries);
			int entriesB = Count(b.Entries);
			for (int i = 0; i < newEntries.Length; i++)
			{
				int mi = (i + newOldest) - oldestA;
				if (mi >= 0 && mi < entriesA)
					newEntries[i] = a.Entries[mi];
				else
				{
					int oi = (i + newOldest) - oldestB;
					if (oi >= 0 && oi < entriesB)
						newEntries[i] = b.Entries[oi];
				}
			}

			return new SerialRCSStack() { Destinations = dest, Entries = newEntries,_id = a._id, NumericID = a.NumericID };
		}

		public void Load(SerialRCSStack st)
		{
			_id = st._id;
			_rev = st._rev;
			NumericID = st.NumericID;
			Entries = st.Entries;
			Destinations = st.Destinations;
		}


		public void IncludeNewerVersion(SerialRCSStack other)
		{
			Load(Merge(this, other));
			_rev = other._rev;
		}

		public override string ToString()
		{
			StringBuilder builder = new StringBuilder();
			builder.Append("Stack [");
			Destinations.AppendTo(builder);
			builder.Append("] {");
			for (int i = 0; i < CountEntries(); i++)
			{
				if (i > 0)
					builder.Append(",");
				builder.Append(Entries[i]);
			}
			builder.Append("}");
			return builder.ToString();

		}

		public override bool Equals(object obj)
		{
			SerialRCSStack other = obj as SerialRCSStack;
			if (other == null)
				return false;

			if (!Destinations.Equals(other.Destinations))
				return false;
			if (CountEntries() != other.CountEntries())
				return false;
			for (int i = 0; i < CountEntries(); i++)
				if (!Entries[i].Equals(other.Entries[i]))
					return false;
			return true;
		}

		public override int GetHashCode()
		{
			var h = Helper.Hash(this);
			h.Add(Destinations);
			for (int i = 0; i < CountEntries(); i++)
				h.Add(Entries[i]);
			return h.GetHashCode();
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
				return CS == null || IC == null;
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

		public class Serial
		{
			public readonly SerialData Data;
			public readonly int Generation;

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