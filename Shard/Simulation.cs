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
	public struct TimingInfo
	{
		public readonly int StepsPerGeneration, StartGeneration, MaxGeneration;
		public readonly TimeSpan
							GenerationTimeWindow,
							StepTimeWindow,
							StepComputationTimeWindow;
		public readonly DateTime
							Start;

		public TimingInfo(DB.TimingContainer t)
		{
			StepsPerGeneration = 1 + t.recoverySteps;
			GenerationTimeWindow = TimeSpan.FromMilliseconds(t.msStep * StepsPerGeneration);
			StepTimeWindow = TimeSpan.FromMilliseconds(t.msStep);
			StepComputationTimeWindow = TimeSpan.FromMilliseconds(t.msComputation);
			Start = Convert.ToDateTime(t.startTime);
			StartGeneration = t.startGeneration;
			MaxGeneration = t.maxGeneration;
		}

		public static TimingInfo Current
		{
			get
			{
				return new TimingInfo(DB.Timing);
			}
		}

		public int TopLevelGeneration
		{
			get
			{
				var rs = StartGeneration + Math.Max(0, (int)(SimulationTime.TotalSeconds / GenerationTimeWindow.TotalSeconds));
				if (MaxGeneration >= 0)
					rs = Math.Min(rs, MaxGeneration);
				return rs;
			}
		}

		public TimeSpan SimulationTime
		{
			get
			{
				return Clock.Now - Start;
			}
		}

		public DateTime LatestGenerationStart
		{
			get
			{
				return Start + TimeSpan.FromTicks(GenerationTimeWindow.Ticks * TopLevelGeneration);
			}
		}

		public TimeSpan LatestGenerationElapsed
		{
			get
			{
				return Clock.Now - LatestGenerationStart;
			}
		}

		public DateTime NextStepDeadline
		{
			get
			{
				var remainingRelative = 1.0 - Helper.Frac(SimulationTime.TotalSeconds / StepTimeWindow.TotalSeconds);
				return Clock.Now + TimeSpan.FromTicks((long)( remainingRelative * StepTimeWindow.Ticks ));
			}
		}

		/// <summary>
		/// Determines the generation step.
		/// 0 indicates the main processing step, >0 a recovery step.
		/// Note that this index can exceed the configured number of recovery steps
		/// if the simulation has reached the maximum generation
		/// </summary>
		public int LatestStepIndex
		{
			get
			{
				return LatestGenerationElapsed.FloorDiv(StepTimeWindow);
			}
		}

	}



	public static class Simulation
	{
		public static ShardID ID { get; private set; }
		private static ShardID ext = new ShardID(Int3.One, 1);
		public static float R { get; private set; } = 1f / 8;
		public static float M { get; private set; } = 1f / 16;

		private static Neighborhood neighbors,
									siblings;

		private static Listener listener;

		private static SDSStack stack = new SDSStack();


		public static Neighborhood Neighbors { get { return neighbors; } }


		public static ClientMessageQueue ClientMessageQueue { get; private set; } = new ClientMessageQueue();


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




		public static SDSStack Stack
		{
			get
			{
				return stack;
			}
		}


		public static void FetchNeighborUpdate(SDSStack.Entry target, Link neighbor, RCS.SerialData data)
		{
			var candidate = new RCS(data);
			var existing = target.InboundRCS[neighbor.LinearIndex];
			bool significant = existing != null && candidate.IC.OneCount < existing.IC.OneCount;
			if (existing != null && candidate.IC.OneCount > existing.IC.OneCount)
			{
				Console.Error.WriteLine("Unable to incorportate RCS from " + neighbor + ": RCS at generation " + target.Generation + " is worse than known");
				return;
			}
			target.InboundRCS[neighbor.LinearIndex] = candidate;
			if (significant)
				target.SignificantInboundChange = true;
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
				var data = DB.Begin(addr.XYZ);
				if (data != null)
				{
					sds = data.Deserialize();
					break;
				}
				CheckIncoming(TimingInfo.Current.TopLevelGeneration);
				Thread.Sleep(1000);
				Console.Write('.');
				Console.Out.Flush();
			}
			Log.Message(" done. Waiting for logic assemblies to finish loading...");

			foreach (var e in sds.FinalEntities)
			{
				var logic = e.MyLogic as DynamicCSLogic;
				if (logic != null)
					logic.FinishLoading(e.ID, TimeSpan.FromMinutes(5));
			}
			Log.Message(" done");

			stack.Append(sds);

			Console.WriteLine("Start Date="+DB.Timing.startTime);

			{
				foreach (var link in neighbors)
					DB.BeginFetch(link.InboundRCS);
			}

			SimulationContext ctx = new SimulationContext();

//			Log.Message("Catching up to g"+ TimingInfo.Current.TopLevelGeneration);
			while (stack.NewestSDSGeneration < TimingInfo.Current.TopLevelGeneration)
			{
				Log.Message("Catching up to g" + TimingInfo.Current.TopLevelGeneration);
//				Console.Write(".");
	//			Console.Out.Flush();
				int nextGen = stack.NewestSDSGeneration + 1;
				ctx.SetGeneration(nextGen);
				stack.Append(new SDS(nextGen));
				stack.Insert(new SDSComputation(Clock.Now,ClientMessageQueue, TimingInfo.Current.StepComputationTimeWindow,ctx).Complete());
				CheckIncoming(TimingInfo.Current.TopLevelGeneration);
			}
			Console.WriteLine("done. Starting main loop...");


			SDSComputation comp = null;

			while (true)
			{
				var timing = TimingInfo.Current;
				CheckIncoming(timing.TopLevelGeneration);
				Log.Message("TLG "+stack.NewestSDSGeneration+"/"+timing.TopLevelGeneration+" @stepIndex "+timing.LatestStepIndex);

				if (comp != null)
				{
					if (Clock.Now >= comp.Deadline)
					{
						Log.Message("Completing g"+comp.Generation);
						stack.Insert(comp.Complete());
						comp = null;
					}
					else
					{
						Clock.SleepUntil(comp.Deadline);
						continue;
					}
				}

				int newestSDSGeneration = stack.NewestSDSGeneration;
				if (timing.TopLevelGeneration > newestSDSGeneration)
				{
					//fast forward: process now. don't care if we're at the beginning
					int nextGen = newestSDSGeneration + 1;
					Log.Message("Processing next TLG g" + nextGen);
					stack.Append(new SDS(nextGen));
					ctx.SetGeneration(nextGen);
					comp = new SDSComputation(timing.NextStepDeadline , ClientMessageQueue, timing.StepComputationTimeWindow,ctx);
				}
				else
				{
					//see if we can recover something
					int oldestInconsistentSDSIndex = stack.NewestConsistentSDSIndex + 1;	//must be > 0
					if (oldestInconsistentSDSIndex < stack.Size)
					{
						int recoverAtIndex = oldestInconsistentSDSIndex;
						int currentGen = stack[recoverAtIndex].Generation;
						for (; recoverAtIndex < stack.Size; recoverAtIndex++)
						{
							var current = stack[recoverAtIndex];
							if (current.SignificantInboundChange)
								break;

							var check = CheckMissingRCS(current);
							
							if (check.ShouldRecoverThis)
								break;
						}
						if (recoverAtIndex < stack.Size)
						{
							Log.Message("Recovering #"+recoverAtIndex+"/"+stack.Size+", g" + stack[recoverAtIndex].Generation);
							//precompute:
							ctx.SetGeneration(stack[recoverAtIndex].Generation);
							comp = new SDSComputation(timing.NextStepDeadline, ClientMessageQueue, timing.StepComputationTimeWindow,ctx);
							//now wait for remote RCS...
						}
					}
					if (comp == null)
					{
						//nothing to recover
						Log.Message("Nothing to do");
						Clock.SleepUntil(timing.NextStepDeadline);
					}
				}
			}


		}

		public struct RecoveryCheck
		{
			public int missingRCS,
							rcsAvailableFromNeighbor,
							rcsRestoredFromDB;
			public bool predecessorIsConsistent,
							thisIsConsistent;

			public bool AllThere { get { return missingRCS == 0; } }
			public bool MissingAvailableFromNeighbors { get { return missingRCS == rcsAvailableFromNeighbor; } }
			//public bool MissingAvailableFromAnywhere { get { return MissingRCS == RCSAvailableFromNeighbor + RCSAvailableFromDatabase; }  }
			public bool AnyAvailableFromNeighbors { get { return rcsAvailableFromNeighbor > 0; } }
			//public bool AnyAvailableFromAnywhere { get { return rcsAvailableFromNeighbor > 0 || RCSAvailableFromDatabase > 0; } }
			public bool ShouldRecoverThis
			{
				get
				{
					return !thisIsConsistent
							&&
							(
								AnyAvailableFromNeighbors
								|| rcsRestoredFromDB > 0
								|| (missingRCS == 0 && predecessorIsConsistent)
								|| rcsRestoredFromDB > 0
							);
				}
			}
		}



		public static RecoveryCheck CheckMissingRCS(SDSStack.Entry sds)
		{
			RecoveryCheck rs = new RecoveryCheck();
			rs.predecessorIsConsistent = sds.IntermediateSDS.inputConsistent;
			rs.thisIsConsistent = sds.IsFullyConsistent;

			foreach (var other in Simulation.Neighbors)
			{
				var inbound = sds.InboundRCS[other.LinearIndex];
				if (inbound != null && inbound.IsFullyConsistent)
					continue;
				//try get from database:
				SerialRCSStack rcsStack = DB.TryGet(other.InboundRCS);
				var rcs = rcsStack?.FindGeneration(sds.Generation);
				if (rcs.HasValue)
				{
					sds.InboundRCS[other.LinearIndex] = new RCS(rcs.Value);
					rs.rcsRestoredFromDB++;
					continue;
				}
				rs.missingRCS++;

				//try to get from neighbor:
				if (other.IsResponsive)
					rs.rcsAvailableFromNeighbor++;  //optimisitic guess
			}
			return rs;
		}



		public static void Configure(ShardID addr, DB.ConfigContainer config, bool forceAllLinksPassive)
		{
			CSLogicProvider.AsyncFactory = DB.GetLogicProviderAsync;

			ID = addr;
			ext = config.extent;
			R = config.r;
			M = config.m;

			MySpace =  Box.OffsetSize(new Vec3(ID.XYZ), new Vec3(1), ID.XYZ + 1 >= ext.XYZ);


			InconsistencyCoverage.CommonResolution = (int)Math.Ceiling(1f / R);

			if (ext.ReplicaLevel > 1)
				siblings = Neighborhood.NewSiblingList(addr, ext.ReplicaLevel, forceAllLinksPassive);
			neighbors = Neighborhood.NewNeighborList(addr, ext.XYZ, forceAllLinksPassive);

			ClientMessageQueue = new ClientMessageQueue();
		}

		public static bool Owns(Vec3 position)
		{
			if (position.FloorInt3 == ID.XYZ)
				return true;
			return MySpace.Contains(position);
		}

		public static ShardID Extent
		{
			get
			{
				return ext;
			}
		}

		public static Box MySpace { get; private set; } = Box.OffsetSize(Vec3.Zero, new Vec3(1), Bool3.True);

		public static float SensorRange { get { return R - M; } }

		public static Box FullSimulationSpace
		{
			get
			{
				return Box.FromMinAndMax(Vec3.Zero, new Vec3(ext.XYZ), Bool3.True);
			}
		}





		internal static void FetchIncoming(Link lnk, object obj)
		{
			incoming.Add(new Tuple<Link, object>(lnk, obj));
		}

		private static void CheckIncoming(int currentTLG)
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
						lnk.SetOldestGeneration(gen, currentTLG);
						lnk.Filter((id, o) =>
						{
							SerialSDS sds = o as SerialSDS;
							if (sds != null)
								return sds.Generation >= gen;
							RCS.Serial rcs = o as RCS.Serial;
							if (rcs != null)
								return rcs.Generation >= gen;
							return true;
						});
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
					FetchNeighborUpdate(stack.AllocateGeneration(rcs.Generation),lnk, rcs.Data);
					return;
				}

				if (obj is SerialSDS)
				{
					//Debug.Assert(HaveSibling(lnk));
					SerialSDS raw = (SerialSDS)obj;
					if (raw.Generation <= stack.OldestSDSGeneration)
					{
						Console.Error.WriteLine("SDS update from sibling or DB: Rejected. Already moved past generation " + raw.Generation);
						return;
					}
					var existing = stack.FindGeneration(raw.Generation);
					if (existing.IsFullyConsistent)
					{
						Console.Error.WriteLine("SDS update from sibling or DB: Rejected. Generation already consistent: " + raw.Generation);
						return;
					}
					SDS sds = raw.Deserialize();
					if (sds.IsFullyConsistent)
					{
						Console.Out.WriteLine("SDS update from sibling or DB: Accepted generation " + raw.Generation);
						stack.Insert(sds);
						return;
					}
					SDS merged = existing.SDS.MergeWith(sds);
					Console.Out.WriteLine("SDS update from sibling " + lnk + ": Merged generation " + raw.Generation);
					if (merged.Inconsistency < existing.SDS.Inconsistency)
						stack.Insert(merged);
					if (merged.Inconsistency < sds.Inconsistency)
						lnk.Set(new SDS.ID(ID.XYZ, raw.Generation).P2PKey, new SerialSDS(merged));
					return;
				}

				Console.Error.WriteLine("Unsupported update from sibling " + lnk + ": " + obj.GetType());
			}
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