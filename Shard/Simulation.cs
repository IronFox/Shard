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
		private static ShardID ext = new ShardID(Int3.One, 1);
		public static float R { get; private set; } = 1f / 8;
		public static float M { get; private set; } = 1f / 16;

		private static Neighborhood neighbors,
									siblings;

		private static Listener listener;
		private static DateTime startDate;

		private static SDSStack stack = new SDSStack();


		public static Neighborhood Neighbors { get { return neighbors; } }





		public static void AdvertiseOldestGeneration(int gen)
		{
			if (siblings == null)
				return;	//during tests
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
				return Math.Max(0, MSIntoSimulation / DB.Config.msPerTimeStep);
			}
		}
		public static int MSIntoSimulation
		{
			get
			{
				return (int)((Clock.Now - startDate).TotalMilliseconds);
			}
		}

		public static int MSToSimulationStart
		{
			get
			{
				return -MSIntoSimulation;
			}
		}

		public static SDSStack Stack
		{
			get
			{
				return stack;
			}
		}




		public static int NeighborCount { get { return neighbors != null ? neighbors.Count : 0; } }


		private static ConcurrentBag<Tuple<Link, object>> incoming = new ConcurrentBag<Tuple<Link, object>>();


		public static void Init(ShardID addr)
		{
			//Host.Domain = ;
			Configure(addr, DB.Config,false);
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

				foreach (var link in neighbors)
					DB.BeginFetch(link.InboundRCS);
			}

			int msPerSubStep = DB.Config.msPerTimeStep / (1 + DB.Config.recoverySteps);
			int msComputation = msPerSubStep / 2;
			TimeSpan perComputation = TimeSpan.FromMilliseconds(msComputation);

			Console.Write("Catching up...");
			Console.Out.Flush();
			while (stack.NewestSDSGeneration < TimeStep)
			{
				Console.Write(".");
				Console.Out.Flush();
				int nextGen = stack.NewestSDSGeneration + 1;
				stack.Append(new SDS(nextGen));
				stack.Insert(new SDS.Computation(nextGen, nextGen == stack.NewestSDSGeneration,perComputation).Complete());
				CheckIncoming();
			}
			Console.WriteLine("done. Starting main loop...");


			TimeSpan perSubStep = TimeSpan.FromMilliseconds(msPerSubStep);

			SDS.Computation comp = null;
			while (true)
			{
				CheckIncoming();

				int timeStep = TimeStep;
				Console.WriteLine("at " + timeStep);
				int wait = MSToSimulationStart;
				if (wait > 0)
				{
					Console.WriteLine("Must wait "+wait+" more MS...");
					Thread.Sleep(Math.Min(1000, MSToSimulationStart));
					continue;
				}

				DateTime stepStart = startDate + TimeSpan.FromMilliseconds( DB.Config.msPerTimeStep * timeStep);
				TimeSpan elapsed = Clock.Now - stepStart;

				int recoveryIndex = elapsed.FloorDiv(perSubStep);
				DateTime subStart = stepStart + TimeSpan.FromMilliseconds(recoveryIndex * msPerSubStep);
				TimeSpan subElapsed = Clock.Now - subStart;
				TimeSpan subRemaining = perSubStep - subElapsed;
				TimeSpan subCompRemaining = perComputation - subElapsed;


				if (timeStep != stack.NewestSDSGeneration)
				{
					//fast forward: process now. don't care if we're at the beginning
					int nextGen = stack.NewestSDSGeneration + 1;
					stack.Append(new SDS(nextGen));
					comp = new SDS.Computation(nextGen+1, true,subCompRemaining);
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
							comp = new SDS.Computation(stack[at].Generation, stack[at].Generation == stack.NewestSDSGeneration, perComputation);
					}
				}

				if (subCompRemaining.TotalSeconds <= 0 && comp != null)
				{
					stack.Insert(comp.Complete());
					comp = null;
				}

				if (comp != null)
					Clock.Sleep(subCompRemaining);
				else
					Clock.Sleep(subRemaining);
			}


		}

		public static void Configure(ShardID addr, DB.ConfigContainer config, bool forceAllLinksPassive)
		{
			ID = addr;
			ext = config.extent;
			R = config.r;
			M = config.m;

			InconsistencyCoverage.CommonResolution = (int)Math.Ceiling(1f / R);

			if (ext.ReplicaLevel > 1)
				siblings = Neighborhood.NewSiblingList(addr, ext.ReplicaLevel, forceAllLinksPassive);
			neighbors = Neighborhood.NewNeighborList(addr, ext.XYZ, forceAllLinksPassive);


		}

		public static bool Owns(Vec3 position)
		{
			return MySpace.Contains(position);
		}

		public static ShardID Extent
		{
			get
			{
				return ext;
			}
		}

		public static Box MySpace
		{
			get
			{
				return Box.OffsetSize(new Vec3(ID.XYZ), new Vec3(1), ID.XYZ + 1 >= ext.XYZ);
			}
		}

		public static float SensorRange { get { return R - M; } }

		public static Box FullSimulationSpace
		{
			get
			{
				return Box.FromMinAndMax(Vec3.Zero, new Vec3(ext.XYZ), Bool3.True);
			}
		}

		public static Vec3 ClampDestination(string task, Vec3 newPosition, EntityID currentEntityPosition, float maxDistance)
		{
			float dist = GetDistance(newPosition, currentEntityPosition.Position);
			if (dist <= maxDistance)
				return newPosition;

			Log.Error(currentEntityPosition + ": " + task + " exceeded maximum range (" + maxDistance + "): " + dist);
			newPosition = currentEntityPosition.Position + (newPosition - currentEntityPosition.Position) * maxDistance / dist;

			Debug.Assert(GetDistance(newPosition, currentEntityPosition.Position) <= maxDistance);

			return newPosition;
		}

		public static bool CheckDistance(string task, Vec3 taskLocation, EntityID currentEntityPosition, float maxDistance)
		{
			float dist = GetDistance(taskLocation, currentEntityPosition.Position);
			if (dist <= maxDistance)
				return true;
			Log.Error(currentEntityPosition + ": " + task + " exceeded maximum range (" + maxDistance + "): " + dist);
			return false;
		}


		public static bool CheckDistance(string task, Vec3 taskLocation, Entity actor, float maxDistance)
		{
			return CheckDistance(task, taskLocation, actor.ID, maxDistance);
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
						Console.Error.WriteLine("OldestGen update from sibling " + lnk + ": Warning: Already moved to generation " + gen);
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
					stack.AllocateGeneration(rcs.Generation).FetchNeighborUpdate(lnk, rcs.Data);
					return;
				}

				if (obj is SDS.Serial)
				{
					//Debug.Assert(HaveSibling(lnk));
					SDS.Serial raw = (SDS.Serial)obj;
					if (raw.Generation <= stack.OldestSDSGeneration)
					{
						Console.Error.WriteLine("SDS update from sibling or DB: Rejected. Already moved past generation " + raw.Generation);
						return;
					}
					SDS existing = stack.FindGeneration(raw.Generation);
					if (existing.IsFullyConsistent)
					{
						Console.Error.WriteLine("SDS update from sibling or DB: Rejected. Generation already consistent: " + raw.Generation);
						return;
					}
					SDS sds = new SDS(raw);
					if (sds.IsFullyConsistent)
					{
						Console.Out.WriteLine("SDS update from sibling or DB: Accepted generation " + raw.Generation);
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