using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	public class SDSStack : IEnumerable<SDS>
	{
		private List<SDS> sdsList = new List<SDS>();

		public void ResetToRoot(SDS root)
		{
			sdsList.Clear();
			Insert(root);
		}

		private void Trim()
		{
			int newest = NewestConsistentSDSIndex;
			if (newest == 0)
				return;

			sdsList.RemoveRange(0, newest);

			Simulation.AdvertiseOldestGeneration(OldestSDS.Generation);
		}

		public SDS NewestSDS
		{
			get
			{
				return sdsList[sdsList.Count - 1];
			}
		}

		public int NewestSDSGeneration
		{
			get
			{
				return sdsList.Count > 0 ? sdsList.GetLast().Generation : -1;
			}
		}
		public int OldestSDSGeneration
		{
			get
			{
				return sdsList.Count > 0 ? sdsList[0].Generation : -1;
			}
		}

		public SDS OldestSDS
		{
			get
			{
				return sdsList[0];
			}
		}

		public SDS AllocateGeneration(int gen)
		{
			if (gen < OldestSDSGeneration)
				return null;
			while (gen > NewestSDSGeneration)
			{
				int gen2 = NewestSDSGeneration + 1;
				sdsList.Add(new SDS(gen2));
			}
			return sdsList[gen - sdsList[0].Generation];
		}
		public SDS FindGeneration(int gen)
		{
			if (gen < OldestSDSGeneration || gen > NewestSDSGeneration)
				return null;
			return sdsList[gen - OldestSDS.Generation];
		}

		public void Insert(SDS sds)
		{
			if (sds.Generation > NewestSDSGeneration)
			{
				while (sds.Generation > NewestSDSGeneration+1)
				{
					int gen2 = NewestSDS.Generation + 1;
					sdsList.Add(new SDS(gen2));
				}
				//Debug.Assert(sds.Generation == NewestSDS.Generation + 1);
				sdsList.Add(sds);
				Trim();
			}
			else
			{
				int at = sds.Generation - OldestSDS.Generation;
				if (at < 0 || at >= sdsList.Count)
					throw new IntegrityViolation("Cannot insert SDS generation "+sds.Generation+", oldest = "+OldestSDSGeneration);
				sdsList[at] = sds;
			}
			Trim();
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
				return sdsList[NewestConsistentSDSIndex];
			}
		}

		public void Append(SDS sds)
		{
			if (sdsList.Count > 0 && NewestSDSGeneration + 1 != sds.Generation)
				throw new IntegrityViolation("Newest generation in stack is "+NewestSDSGeneration+". Trying to append generation "+sds.Generation);
			sdsList.Add(sds);
		}

		public IEnumerator<SDS> GetEnumerator()
		{
			return sdsList.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return sdsList.GetEnumerator();
		}

		public SDS this[int index]
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
				for (int i = sdsList.Count - 1; i >= 0; i--)
				{
					if (sdsList[i].IsFullyConsistent)
						return i;
				}
				return -1;
			}
		}

		public int NewestConsistentSDSGeneration
		{
			get
			{
				return sdsList[NewestConsistentSDSIndex].Generation;
			}
		}


	}
}
