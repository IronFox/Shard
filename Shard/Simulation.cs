using System;
using VectorMath;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Globalization;

namespace Shard
{
	public static class Simulation
	{
		public static ShardID ID { get; private set; }
		private static ShardID ext;
		private static float r;
		private static float m;

		private static List<Link> neighbors = new List<Link>();
		private static Link[] siblings;
		private static Listener listener;
		private static DateTime startDate;


		private static List<SDS> sdsList = new List<SDS>();


		public static IEnumerable<Link> Neighbors { get { return neighbors; } }




		private static void Trim()
		{
			int newest = NewestConsistentSDSIndex;
			if (newest == 0)
				return;

			sdsList.RemoveRange(0, newest);

			AdvertiseOldestGeneration(OldestSDS.Generation);
		}

		private static void AdvertiseOldestGeneration(int gen)
		{
			var data = new OldestGeneration(OldestSDS.Generation);
			foreach (var lnk in siblings)
				lnk.Set("OldestGeneration", data);
			foreach (var lnk in neighbors)
				lnk.Set("OldestGeneration", data);
		}

		public static Link FindLink(ShardID id)
		{
			if (id.XYZ == ID.XYZ)
			{
				foreach (var s in siblings)
					if (s.ID == id)
						return s;
				throw new Exception("Unable to find sibling shard with ID " + id);
			}
			foreach (var n in neighbors)
				if (n.ID == id)
					return n;
			throw new Exception("Unable to find neighbor shard with ID " + id);
		}



		public static int TimeStep
		{
			get
			{
				return Math.Max(0, (int)((Clock.Now - startDate).TotalMilliseconds / DB.Config.msPerTimeStep));
			}
		}

		public static SDS NewestSDS
		{
			get
			{
				return sdsList[sdsList.Count - 1];
			}
		}

		public static SDS OldestSDS
		{
			get
			{
				return sdsList[0];
			}
		}


		public static int NewestConsistentSDSIndex
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

		public static SDS AllocateGeneration(int gen)
		{
			if (gen < sdsList[0].Generation)
				return null;
			while (gen > NewestSDS.Generation)
			{
				int gen2 = NewestSDS.Generation + 1;
				sdsList.Add(new SDS(gen2));
			}
			return sdsList[gen - sdsList[0].Generation];
		}
		public static SDS FindGeneration(int gen)
		{
			if (gen < OldestSDS.Generation || gen > NewestConsistentSDS.Generation)
				return null;
			return sdsList[gen - OldestSDS.Generation];
		}

		public static void Insert(SDS sds)
		{
			if (sds.Generation > NewestSDS.Generation)
			{
				while (sds.Generation+1 > NewestSDS.Generation)
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
				sdsList[at] = sds;
			}
			Trim();
		}

		public static SDS NewestConsistentSDS
		{
			get
			{
				return sdsList[NewestConsistentSDSIndex];
			}
		}

		public static int NeighborCount { get { return neighbors.Count; } }

		public static void Init(ShardID addr)
		{
			//Host.Domain = ;

			ID = addr;
			ext = DB.Config.extent;
			r = DB.Config.r;
			m = DB.Config.m;


			if (ext.ReplicaLevel > 1)
			{
				int at = 0;
				siblings = new Link[ext.ReplicaLevel - 1];
				for (int i = 0; i < ext.ReplicaLevel; i++)
					if (i != addr.ReplicaLevel)
					{
						siblings[at] = new Link(new ShardID(addr.XYZ, i), i > addr.ReplicaLevel,at,true);
						at++;
					}
			}

			{
				for (int x = addr.X - 1; x <= addr.X + 1; x++)
					for (int y = addr.Y - 1; y <= addr.Y + 1; y++)
						for (int z = addr.Z - 1; z <= addr.Z + 1; z++)
						{
							Int3 a = new Int3(x, y, z);
							if (a == addr.XYZ)
								continue;

							if ((a >= Int3.Zero).All && (a < ext.XYZ).All)
							{
								int linear = neighbors.Count;
								neighbors.Add(new Link(new ShardID(a, addr.ReplicaLevel), a.OrthographicCompare(addr.XYZ) > 0,linear,false));
							}
						}
			}

			AdvertiseOldestGeneration(0);

			listener = new Listener();

			Console.Write("Polling SDS state...");
			Console.Out.Flush();

			SDS sds;
			while (true)
			{
				var data = DB.LoadLatest(addr.XYZ);
				if (data != null)
				{
					sds = new SDS(data);
					break;
				}
				Thread.Sleep(1000);
				Console.Write('.');
				Console.Out.Flush();
			}
			Console.WriteLine(" done");
			sdsList.Add(sds);

			startDate = DateTime.Parse(DB.Config.start,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal);
			Console.WriteLine("Start Date="+startDate);

			{
				int timeStep = TimeStep;
				Console.WriteLine("Starting at " + timeStep);

				List<RCS.GenID> queryRCS = new List<RCS.GenID>();
				foreach (var link in neighbors)
					for (int i = sds.Generation + 1; i < timeStep; i++)
					{
						queryRCS.Add(new RCS.GenID(link.ID.XYZ, addr.XYZ, i));
					}
				Console.WriteLine("Querying " + queryRCS.Count + " RCS's");
				DB.BeginFetch(queryRCS);
			}

			Console.Write("Catching up...");
			Console.Out.Flush();
			while (NewestSDS.Generation < TimeStep)
			{
				Console.Write(".");
				Console.Out.Flush();
				Insert(new SDS.Computation(NewestSDS.Generation + 1).Complete());
			}
			Console.WriteLine("done. Starting main loop...");

			int msPerSubStep = DB.Config.msPerTimeStep / (1 + DB.Config.recoverySteps);
			int msCompletion = msPerSubStep / 10;

			SDS.Computation comp = null;
			while (true)
			{
				int timeStep = (int)((Clock.Now - startDate).TotalMilliseconds / DB.Config.msPerTimeStep);
				Console.WriteLine("at " + timeStep);

				int msIntoStep = (int)((Clock.Now - (startDate + new TimeSpan(TimeSpan.TicksPerMillisecond * DB.Config.msPerTimeStep))).TotalMilliseconds);
				int recoveryIndex = msIntoStep / msPerSubStep;
				int msIntoSubStep = msIntoStep - recoveryIndex * msPerSubStep;
				int msRemainingInSubStep = (recoveryIndex + 1) * msPerSubStep - msIntoStep;
				int msRemainingUntilCompletion = (recoveryIndex + 1) * msPerSubStep - msCompletion - msIntoStep;

				if (timeStep != NewestSDS.Generation)
				{
					//fast forward: process now. don't care if we're at the beginning
					comp = new SDS.Computation(NewestSDS.Generation+1);
				}
				else
				{
					//see if we can recover something

					int at = NewestConsistentSDSIndex + 1;
					if (at < sdsList.Count)
					{
						int currentGen = sdsList[at].Generation;
						for (; at < sdsList.Count; at++)
						{
							SDS current = sdsList[at];
							if (current.SignificantInboundChange)
								break;

							var check = current.CheckMissingRCS();
							
							if (check.ShouldRecoverThis)
								break;
						}
						if (at < sdsList.Count)
							comp = new SDS.Computation(sdsList[at].Generation);
					}
				}

				if (msRemainingUntilCompletion <= 0 && comp != null)
				{
					Insert(comp.Complete());
					comp = null;
				}

				if (comp != null)
					Clock.Sleep(Clock.Milliseconds(msRemainingUntilCompletion));
				else
					Clock.Sleep(Clock.Milliseconds(msRemainingInSubStep));
			}
		}

		internal static void FetchIncoming(Link lnk, object obj)
		{
			if (obj is OldestGeneration)
			{
				int gen = ((OldestGeneration)obj).Generation;
				if (gen == lnk.OldestGeneration)
				{
					Console.Error.WriteLine("OldestGen update from sibling " + lnk + ": Warning: Already moved past generation " + gen);
					return;
				}
				if (gen > lnk.OldestGeneration)
				{
					lnk.OldestGeneration = gen;
					{
						lnk.Filter((id, o) =>
						{
							SDS.Serial sds = o as SDS.Serial;
							if (sds != null)
								return sds.Generation >= gen;
							RCS.Serial rcs = o as RCS.Serial;
							if (rcs != null)
								return rcs.Generation >= gen;
							return true;
						});
					}
				}
				else
				{
					foreach (var sds in sdsList)
					{
						if (sds.Generation < gen)
							continue;
						if (lnk.IsSibling && sds.IsFullyConsistent)
							lnk.Set(new SDS.ID(ID.XYZ, sds.Generation).P2PKey, sds.Export());
						if (!lnk.IsSibling)
						{
							var rcs = sds.OutboundRCS[lnk.LinearIndex];
							if (rcs != null)
							{
								var id = lnk.OutboundRCS(rcs.Generation);
								lnk.Set(id.ToString(), rcs.Export(id.ID));
							}
						}
					}
				}
				return;
			}
			if (obj is RCS.Serial)
			{
				RCS.Serial rcs = (RCS.Serial)obj;
				if (rcs.Generation <= NewestConsistentSDS.Generation)
				{
					Console.Error.WriteLine("RCS update from sibling " + lnk + ": Rejected. Already moved past generation " + rcs.Generation);
					return;
				}
				AllocateGeneration(rcs.Generation).FetchNeighborUpdate(lnk, rcs);
				return;
			}

			if (obj is SDS.Serial)
			{
				Debug.Assert(HaveSibling(lnk));
				SDS.Serial raw = (SDS.Serial)obj;
				if (raw.Generation <= OldestSDS.Generation)
				{
					Console.Error.WriteLine("SDS update from sibling "+lnk+": Rejected. Already moved past generation "+raw.Generation);
					return;
				}
				SDS existing = FindGeneration(raw.Generation);
				if (existing.IsFullyConsistent)
				{
					Console.Error.WriteLine("SDS update from sibling " + lnk + ": Rejected. Generation already consistent: " + raw.Generation);
					return;
				}
				SDS sds = new SDS(raw);
				if (sds.IsFullyConsistent)
				{
					Console.Out.WriteLine("SDS update from sibling " + lnk + ": Accepted generation "+raw.Generation);
					Insert(sds);
					return;
				}
				SDS merged = existing.MergeWith(sds);
				Console.Out.WriteLine("SDS update from sibling " + lnk + ": Merged generation " + raw.Generation);
				if (merged.Inconsistency < existing.Inconsistency)
					Insert(merged);
				if (merged.Inconsistency < sds.Inconsistency)
					lnk.Set(new SDS.ID(ID.XYZ,raw.Generation).P2PKey, merged.Export());
				return;
			}

			Console.Error.WriteLine("Unsupported update from sibling " + lnk + ": "+obj.GetType());

		}

		public static bool HaveSibling(Link lnk)
		{
			foreach (var s in siblings)
				if (s == lnk)
					return true;
			return false;
		}
	}

	public class OldestGeneration
	{
		private int generation;

		public OldestGeneration(int generation)
		{
			this.generation = generation;
		}

		public int Generation { get { return generation; } }
	}
}