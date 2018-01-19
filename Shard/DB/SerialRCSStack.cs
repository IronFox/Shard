using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
				return "old=" + OldestGeneration + ",up=" + LastUpdateTimeStep;
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
						throw new IntegrityViolation("Last update time step is negative: " + dest.LastUpdateTimeStep);
					if (dest.LastUpdateTimeStep >= newest - MaxDestinationAgeTolerance)
						oldest = Math.Min(oldest, dest.OldestGeneration);
				}
				if (oldest == int.MaxValue)
					throw new IntegrityViolation("newest (" + newest + ") not contained by destination field [" + Count() + "]");
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

			public static bool operator ==(DestinationTable a, DestinationTable b)
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
			Console.WriteLine(oldestA + "/" + oldestB + "->" + newOldest);

			int historyA = oldestA + Count(a.Entries) - newOldest;
			int historyB = oldestB + Count(b.Entries) - newOldest;
			int history = Helper.Max(historyA, historyB, 0);

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

			return new SerialRCSStack() { Destinations = dest, Entries = newEntries, _id = a._id, NumericID = a.NumericID };
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


}
