using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	public class SDSStack : IEnumerable<SDSStack.Entry>
	{
		public class Entry
		{
			public readonly int Generation;
			public SDS SDS { get; set; }
			public IntermediateSDS IntermediateSDS	{get;set;}
			public bool IsFullyConsistent;
			public readonly RCS[] InboundRCS = new RCS[Simulation.NeighborCount];
			public bool SignificantInboundChange { get; set; }

			public Entry(int generation)
			{
				Generation = generation;
			}
			public Entry(SDS sds)
			{
				SDS = sds;
				Generation = sds.Generation;
				IsFullyConsistent = sds.IsFullyConsistent;
			}

			public Entry(SDS sds, IntermediateSDS intermediate) : this(sds)
			{
				IntermediateSDS = intermediate;
			}
		}


		private List<Entry> sdsList = new List<Entry>();

		public void ResetToRoot(Entry root)
		{
			lock (this)
			{
				sdsList.Clear();
				Insert(root);
			}
		}

		private void Trim()
		{
			lock (this)
			{
				int newest = NewestConsistentSDSIndex;
				if (newest == 0)
					return;

				sdsList.RemoveRange(0, newest);

				Simulation.AdvertiseOldestGeneration(OldestSDS.Generation);
			}
		}

		public SDS NewestRegisteredSDS
		{
			get
			{
				lock (this)
					return sdsList[sdsList.Count - 1].SDS;
			}
		}
		public SDS NewestFinishedSDS
		{
			get
			{
				lock (this)
				{
					int at = sdsList.Count - 1;
					while (at > 0 && !sdsList[at].SDS.IsSet)
						at--;
					return sdsList[at].SDS;
				}
			}
		}

		public int NewestRegisteredSDSGeneration
		{
			get
			{
				lock(this)
					return sdsList.Count > 0 ? sdsList.GetLast().Generation : -1;
			}
		}
		public int NewestFinishedSDSGeneration
		{
			get
			{
				lock (this)
				{
					int at = sdsList.Count - 1;
					while (at >= 0 && !sdsList[at].SDS.IsSet)
						at--;
					return at >= 0 ? sdsList[at].Generation : -1;
				}
			}
		}

		public int OldestSDSGeneration
		{
			get
			{
				lock (this)
					return sdsList.Count > 0 ? sdsList[0].Generation : -1;
			}
		}

		public SDS OldestSDS
		{
			get
			{
				return sdsList[0].SDS;
			}
		}

		public Entry AllocateGeneration(int gen)
		{
			lock (this)
			{
				if (gen < OldestSDSGeneration)
					return null;
				while (gen > NewestRegisteredSDSGeneration)
				{
					int gen2 = NewestRegisteredSDSGeneration + 1;
					sdsList.Add(new Entry(gen2));
				}
				return sdsList[gen - sdsList[0].Generation];
			}
		}
		public Entry FindGeneration(int gen)
		{
			lock (this)
			{
				if (gen < OldestSDSGeneration || gen > NewestRegisteredSDSGeneration)
					return null;
				return sdsList[gen - OldestSDS.Generation];
			}
		}

		public void Insert(SDS sds, bool trim = true)
		{
			Insert(new Entry(sds), trim);
		}

		public Entry Insert(Tuple<SDS, IntermediateSDS> tuple, bool trim = true)
		{
			lock (this)
			{
				int gen = tuple.Item1.Generation;
				if (gen >= NewestRegisteredSDSGeneration)
					ObservationLink.SignalUpdate(tuple.Item1);

				Entry rs;
				if (gen > NewestRegisteredSDSGeneration)
				{
					while (gen > NewestRegisteredSDSGeneration + 1)
					{
						int gen2 = NewestRegisteredSDS.Generation + 1;
						sdsList.Add(new Entry(gen2));
					}
					//Debug.Assert(sds.Generation == NewestSDS.Generation + 1);
					sdsList.Add(new Entry(tuple.Item1, tuple.Item2));
					rs = sdsList.GetLast();
				}
				else
				{
					int at = gen - OldestSDS.Generation;
					if (at < 0 || at >= sdsList.Count)
						throw new IntegrityViolation("Cannot insert SDS generation " + gen + ", oldest = " + OldestSDSGeneration);
					Entry e = sdsList[at];
					e.SDS = tuple.Item1;
					e.IntermediateSDS = tuple.Item2;
					e.IsFullyConsistent = tuple.Item1.IsFullyConsistent;
					e.SignificantInboundChange = false;
					rs = e;
				}
				if (trim)
					Trim();
				return rs;
			}
		}

		public void Insert(Entry entry, bool trim = true)
		{
			lock (this)
			{
				if (entry.Generation >= NewestRegisteredSDSGeneration)
					ObservationLink.SignalUpdate(entry.SDS);

				if (entry.Generation > NewestRegisteredSDSGeneration)
				{
					while (entry.Generation > NewestRegisteredSDSGeneration + 1)
					{
						int gen2 = NewestRegisteredSDS.Generation + 1;
						sdsList.Add(new Entry(gen2));
					}
					//Debug.Assert(sds.Generation == NewestSDS.Generation + 1);
					sdsList.Add(entry);
				}
				else
				{
					int at = entry.Generation - OldestSDS.Generation;
					if (at < 0 || at >= sdsList.Count)
						throw new IntegrityViolation("Cannot insert SDS generation " + entry.Generation + ", oldest = " + OldestSDSGeneration);
					sdsList[at] = entry;
				}
				if (trim)
					Trim();
			}
		}

		public int Size
		{
			get
			{
				return sdsList.Count;
			}
		}

		public SDS NewestConsistentSDS
		{
			get
			{
				lock (this)
					return sdsList[NewestConsistentSDSIndex].SDS;
			}
		}

		public void Append(SDS sds)
		{
			Append(new Entry(sds));
		}

		public void Append(Entry entry)
		{
			lock (this)
			{
				if (sdsList.Count > 0 && NewestRegisteredSDSGeneration + 1 != entry.Generation)
					throw new IntegrityViolation("Newest generation in stack is " + NewestRegisteredSDSGeneration + ". Trying to append generation " + entry.Generation);
				sdsList.Add(entry);
			}
		}

		public IEnumerator<Entry> GetEnumerator()
		{
			return sdsList.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return sdsList.GetEnumerator();
		}

		public Entry this[int index]
		{
			get
			{
				return sdsList[index];
			}
		}

		public int NewestConsistentSDSIndex
		{
			get
			{
				lock (this)
				{
					for (int i = sdsList.Count - 1; i >= 0; i--)
					{
						if (sdsList[i].IsFullyConsistent)
							return i;
					}
					return -1;
				}
			}
		}

		public int NewestConsistentSDSGeneration
		{
			get
			{
				lock (this)
					return sdsList[NewestConsistentSDSIndex].Generation;
			}
		}


	}
}
