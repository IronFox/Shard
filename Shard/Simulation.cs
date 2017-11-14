using System;
using VectorMath;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Globalization;
using System.Collections.Concurrent;

namespace Shard
{
	public static class Simulation
	{
		public static ShardID ID { get; private set; }
		private static ShardID ext;
		public static float R { get; private set; } = 1f / 8;
		public static float M { get; private set; } = 1f / 16;

		private static Neighborhood neighbors,
									siblings;

		private static Listener listener;
		private static DateTime startDate;

		private static SDSStack stack = new SDSStack();


		public static IEnumerable<Link> Neighbors { get { return neighbors; } }





		public static void AdvertiseOldestGeneration(int gen)
		{
			var data = new OldestGeneration(gen);
			siblings.AdvertiseOldestGeneration(data);
			neighbors.AdvertiseOldestGeneration(data);
		}

		public static Link FindLink(ShardID id)
		{
			if (id.XYZ == ID.XYZ)
				return siblings.Find(id);
			return neighbors.Find(id);
		}

		public static bool NeighborExists(Int3 coordinates)
		{
			return neighbors.Find(coordinates) != null;
		}



		public static int TimeStep
		{
			get
			{
				return Math.Max(0, (int)((Clock.Now - startDate).TotalMilliseconds / DB.Config.msPerTimeStep));
			}
		}

		public static SDSStack Stack
		{
			get
			{
				return stack;
			}
		}




		public static int NeighborCount { get { return neighbors.Count; } }


		private static ConcurrentBag<Tuple<Link, object>> incoming = new ConcurrentBag<Tuple<Link, object>>();


		public static void Init(ShardID addr)
		{
			//Host.Domain = ;
			Configure(addr, DB.Config);
			AdvertiseOldestGeneration(0);

			listener = new Listener(h => Simulation.FindLink(h.ID));

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
				CheckIncoming();
				Thread.Sleep(1000);
				Console.Write('.');
				Console.Out.Flush();
			}
			Console.WriteLine(" done");
			stack.Append(sds);

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
			while (stack.NewestSDSGeneration < TimeStep)
			{
				Console.Write(".");
				Console.Out.Flush();
				int nextGen = stack.NewestSDSGeneration + 1;
				stack.Append(new SDS(nextGen));
				stack.Insert(new SDS.Computation(nextGen).Complete());
				CheckIncoming();
			}
			Console.WriteLine("done. Starting main loop...");

			int msPerSubStep = DB.Config.msPerTimeStep / (1 + DB.Config.recoverySteps);
			int msCompletion = msPerSubStep / 10;

			SDS.Computation comp = null;
			while (true)
			{
				CheckIncoming();

				int timeStep = (int)((Clock.Now - startDate).TotalMilliseconds / DB.Config.msPerTimeStep);
				Console.WriteLine("at " + timeStep);

				int msIntoStep = (int)((Clock.Now - (startDate + new TimeSpan(TimeSpan.TicksPerMillisecond * DB.Config.msPerTimeStep))).TotalMilliseconds);
				int recoveryIndex = msIntoStep / msPerSubStep;
				int msIntoSubStep = msIntoStep - recoveryIndex * msPerSubStep;
				int msRemainingInSubStep = (recoveryIndex + 1) * msPerSubStep - msIntoStep;
				int msRemainingUntilCompletion = (recoveryIndex + 1) * msPerSubStep - msCompletion - msIntoStep;

				if (timeStep != stack.NewestSDSGeneration)
				{
					//fast forward: process now. don't care if we're at the beginning
					int nextGen = stack.NewestSDSGeneration + 1;
					stack.Append(new SDS(nextGen));
					comp = new SDS.Computation(nextGen+1);
				}
				else
				{
					//see if we can recover something

					int at = stack.NewestConsistentSDSIndex + 1;
					if (at < stack.Size)
					{
						int currentGen = stack[at].Generation;
						for (; at < stack.Size; at++)
						{
							SDS current = stack[at];
							if (current.SignificantInboundChange)
								break;

							var check = current.CheckMissingRCS();
							
							if (check.ShouldRecoverThis)
								break;
						}
						if (at < stack.Size)
							comp = new SDS.Computation(stack[at].Generation);
					}
				}

				if (msRemainingUntilCompletion <= 0 && comp != null)
				{
					stack.Insert(comp.Complete());
					comp = null;
				}

				if (comp != null)
					Clock.Sleep(Clock.Milliseconds(msRemainingUntilCompletion));
				else
					Clock.Sleep(Clock.Milliseconds(msRemainingInSubStep));
			}
		}

		public static void Configure(ShardID addr, DB.ConfigContainer config)
		{
			ID = addr;
			ext = config.extent;
			R = config.r;
			M = config.m;

			InconsistencyCoverage.CommonResolution = (int)Math.Ceiling(1f / R);

			if (ext.ReplicaLevel > 1)
				siblings = Neighborhood.NewSiblingList(addr, ext.ReplicaLevel);
			neighbors = Neighborhood.NewNeighborList(addr, ext.XYZ);

			MySpace = Box.OffsetSize(new Vec3(ID.XYZ), new Vec3(1), new Bool3(NeighborExists(ID.XYZ + Int3.XAxis), NeighborExists(ID.XYZ + Int3.YAxis), NeighborExists(ID.XYZ + Int3.ZAxis)));

		}

		public static bool Owns(Vec3 position)
		{
			return MySpace.Contains(position);
		}

		public static Box MySpace { get; private set; } = Box.OffsetSize(Vec3.Zero, Vec3.One, Bool3.True);
		public static float SensorRange { get { return R - M; } }

		internal static bool CheckDistance(string task, Vec3 referencePosition, Entity e, float maxDistance)
		{
			float dist = GetDistance(referencePosition, e.ID.Position);
			if (dist <= maxDistance)
				return true;
			Log.Error(e + ": " + task + " exceeded maximum range (" + maxDistance + "): " + dist);
			return false;
		}
		internal static bool CheckDistance(string task, Vec3 referencePosition, Vec3 targetPosition, float maxDistance)
		{
			float dist = GetDistance(referencePosition, targetPosition);
			if (dist <= maxDistance)
				return true;
			Log.Error(task + ": exceeded maximum range (" + maxDistance + "): " + dist);
			return false;
		}

		internal static void FetchIncoming(Link lnk, object obj)
		{
			incoming.Add(new Tuple<Link, object>(lnk, obj));
		}

		private static void CheckIncoming()
		{
			Tuple<Link, object> pair;
			while (incoming.TryTake(out pair))
			{
				object obj = pair.Item2;
				Link lnk = pair.Item1;

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
						foreach (var sds in stack)
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
									var id = lnk.OutboundRCS(sds.Generation);
									lnk.Set(id.ToString(), rcs.Export(id));
								}
							}
						}
					}
					return;
				}
				if (obj is RCS.Serial)
				{
					RCS.Serial rcs = (RCS.Serial)obj;
					if (rcs.Generation <= stack.NewestConsistentSDSGeneration)
					{
						Console.Error.WriteLine("RCS update from sibling " + lnk + ": Rejected. Already moved past generation " + rcs.Generation);
						return;
					}
					stack.AllocateGeneration(rcs.Generation).FetchNeighborUpdate(lnk, rcs);
					return;
				}

				if (obj is SDS.Serial)
				{
					Debug.Assert(HaveSibling(lnk));
					SDS.Serial raw = (SDS.Serial)obj;
					if (raw.Generation <= stack.OldestSDSGeneration)
					{
						Console.Error.WriteLine("SDS update from sibling " + lnk + ": Rejected. Already moved past generation " + raw.Generation);
						return;
					}
					SDS existing = stack.FindGeneration(raw.Generation);
					if (existing.IsFullyConsistent)
					{
						Console.Error.WriteLine("SDS update from sibling " + lnk + ": Rejected. Generation already consistent: " + raw.Generation);
						return;
					}
					SDS sds = new SDS(raw);
					if (sds.IsFullyConsistent)
					{
						Console.Out.WriteLine("SDS update from sibling " + lnk + ": Accepted generation " + raw.Generation);
						stack.Insert(sds);
						return;
					}
					SDS merged = existing.MergeWith(sds);
					Console.Out.WriteLine("SDS update from sibling " + lnk + ": Merged generation " + raw.Generation);
					if (merged.Inconsistency < existing.Inconsistency)
						stack.Insert(merged);
					if (merged.Inconsistency < sds.Inconsistency)
						lnk.Set(new SDS.ID(ID.XYZ, raw.Generation).P2PKey, merged.Export());
					return;
				}

				Console.Error.WriteLine("Unsupported update from sibling " + lnk + ": " + obj.GetType());
			}
		}

		public static float GetDistance(Vec3 a, Vec3 b)
		{
			return Vec3.GetChebyshevDistance(a, b);
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