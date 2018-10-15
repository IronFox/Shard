using System;
using VectorMath;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Globalization;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net;
using System.Linq;
using Base;

namespace Shard
{
	public struct TimingInfo
	{
		public readonly int StepsPerGeneration, StartGeneration, MaxGeneration;
		public readonly TimeSpan
							GenerationTimeWindow,
							StepTimeWindow,
							MessageProcessingTimeWindow,
							StepComputationTimeWindow;
		public readonly DateTime
							Start;

		public TimingInfo(BaseDB.TimingContainer t)
		{
			StepsPerGeneration = 1 + t.recoverySteps;
			GenerationTimeWindow = TimeSpan.FromMilliseconds(t.msStep * StepsPerGeneration);
			StepTimeWindow = TimeSpan.FromMilliseconds(t.msStep);
			StepComputationTimeWindow = TimeSpan.FromMilliseconds(t.msComputation);
			MessageProcessingTimeWindow = TimeSpan.FromMilliseconds(t.msMessageProcessing);

			Start = Convert.ToDateTime(t.startTime);
			StartGeneration = t.startGeneration;
			MaxGeneration = t.maxGeneration;
		}

		public static TimingInfo Current
		{
			get
			{
				return new TimingInfo(BaseDB.Timing);
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
		public static EntityRanges Ranges = new EntityRanges(1f / 8, -1, Box.OffsetSize(Vec3.Zero, Vec3.One,Bool3.True));

		private static Neighborhood neighbors,
									siblings;

		private static Listener listener;
		private static ObservationLink.Listener observationListener;

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
			DB.StopFetchingRCSs(gen);

			if (siblings.AllResponsive && siblings.OldestGeneration >= gen)
				DB.RemoveInboundRCSsAsync(neighbors.Select(sibling => sibling.InboundRCSStackID),gen).Wait();

		}

		public static Link FindLink(ShardID id)
		{
			if (id.XYZ == ID.XYZ)
				return siblings?.Find(id);
			return neighbors?.Find(id);
		}
		public static Link FindLink(Address addr)
		{
			var s = siblings?.Find(addr);
			if (s == null)
				s = neighbors?.Find(addr);
			return s;
		}

		public static bool NeighborExists(Int3 coordinates)
		{
			return neighbors?.Find(coordinates) != null;
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
			FetchNeighborUpdate(target,neighbor,new RCS(data));
		}

		public static void FetchNeighborUpdate(SDSStack.Entry target, Link neighbor, RCS candidate)
		{
			var existing = target.InboundRCS[neighbor.LinearIndex];
			bool significant = existing != null && candidate.IC.OneCount < existing.IC.OneCount;
			if (existing != null && candidate.IC.OneCount > existing.IC.OneCount)
			{
				Log.Error("Unable to incorportate RCS from " + neighbor + ": RCS at generation " + target.Generation + " is worse than known");
				return;
			}
			target.InboundRCS[neighbor.LinearIndex] = candidate;
			if (significant)
				target.SignificantInboundChange = true;
			Log.Message(neighbor.Name + ": RCS[" + neighbor.LinearIndex + "] @g" + target.Generation + " IC ones: " + candidate.IC.OneCount);
		}





		public static int NeighborCount { get { return neighbors != null ? neighbors.Count : 0; } }


		private static ConcurrentBag<Tuple<Link, object>> incoming = new ConcurrentBag<Tuple<Link, object>>();

		private static Stopwatch titleWatch = Stopwatch.StartNew();
		private static void UpdateTitle(string title)
		{
			try
			{
				if (titleWatch.Elapsed > TimeSpan.FromMilliseconds(500))
				{
					Console.Title = title;
					titleWatch.Restart();
				}
			}
			catch	//as it turns out, this operation can throw very odd exceptions. Just ignore for now
			{ }
		}

		

		public static void Run(ShardID myID)
		{
			//Host.Domain = ;
			listener = new Listener(h => FindLink(h));
			Consensus.Access.Begin(myID, listener.Port);
			Configure(myID, BaseDB.Config,false);

			AdvertiseOldestGeneration(0);


			observationListener = new ObservationLink.Listener(listener.Port - 1000);

			Log.Message("Polling SDS state...");
			SimulationContext ctx = new SimulationContext(false);

			SDS sds;
			while (true)
			{
				var data = DB.Begin(myID.XYZ);
				if (data != null)
				{
					sds = data.Deserialize();
					break;
				}
				CheckIncoming(TimingInfo.Current.TopLevelGeneration,ctx);
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

			Log.Message("Start Date="+BaseDB.Timing.startTime);

			//{
			//	foreach (var link in neighbors)
			//		DB.BeginFetch(link.InboundRCSStackID);
			//}


//			Log.Message("Catching up to g"+ TimingInfo.Current.TopLevelGeneration);
			while (stack.NewestFinishedSDSGeneration < TimingInfo.Current.TopLevelGeneration)
			{
				UpdateTitle("Catching up g" + stack.NewestFinishedSDSGeneration + "/" + TimingInfo.Current.TopLevelGeneration);
				//Log.Message("Catching up to g" + TimingInfo.Current.TopLevelGeneration);
				Console.Write(".");
				Console.Out.Flush();
				int nextGen = stack.NewestFinishedSDSGeneration + 1;
				ctx.SetGeneration(nextGen);
				stack.Append(new SDS(nextGen));
				Debug.Assert(stack.NewestRegisteredEntry.IsFinished);
				stack.Insert(new SDSComputation(Clock.Now,ClientMessageQueue, TimingInfo.Current.StepComputationTimeWindow,ctx).Complete());
				Debug.Assert(stack.NewestRegisteredEntry.IsFinished);
				CheckIncoming(TimingInfo.Current.TopLevelGeneration,ctx);

				ClientMessageQueue.Trim(stack.NewestFinishedSDSGeneration - 2, stack.NewestConsistentSDSGeneration + 1);

			}
			Log.Message("done. Starting main loop...");


			SDSComputation comp = null;

			while (true)
			{
				var timing = TimingInfo.Current;
				CheckIncoming(timing.TopLevelGeneration,ctx);
				Log.Minor("TLG "+stack.NewestFinishedSDSGeneration + "/"+timing.TopLevelGeneration+" @stepIndex "+timing.LatestStepIndex);
				{
					var newest = stack.NewestFinishedSDS;
					string title  = ID+" g" + newest.Generation + " " + (float)(newest.IC.Size.Product - newest.IC.OneCount)*100/ newest.IC.Size.Product+"% consistent";
					var con = stack.NewestConsistentSDS;
					if (con != newest)
						title += ", newest consistent at g" + con.Generation;
					title += ", " + timing.LatestStepIndex;
					UpdateTitle(title);
				}

				int newestSDSGeneration = stack.NewestFinishedSDSGeneration;
				if (comp == null)
				{
					Debug.Assert(stack.NewestRegisteredEntry.IsFinished);
					Debug.Assert(newestSDSGeneration == stack.NewestRegisteredSDSGeneration);
					Debug.Assert(stack.NewestConsistentSDSIndex != -1);
				}
				if (comp != null)
				{
					if (Clock.Now >= comp.Deadline || (timing.TopLevelGeneration != newestSDSGeneration && timing.TopLevelGeneration > comp.Generation))
					{
						stack.Insert(comp.Complete());
						ClientMessageQueue.Trim(stack.NewestFinishedSDSGeneration - 2, stack.NewestConsistentSDSGeneration + 1);
						comp = null;
						newestSDSGeneration = stack.NewestFinishedSDSGeneration;
						Debug.Assert(stack.NewestRegisteredEntry.IsFinished);
						Debug.Assert(newestSDSGeneration == stack.NewestRegisteredSDSGeneration);
						Debug.Assert(stack.NewestConsistentSDSIndex != -1);
					}
					else
					{
						if (timing.TopLevelGeneration == newestSDSGeneration)
							Clock.SleepUntil(comp.Deadline);
						continue;
					}
				}

				if (timing.TopLevelGeneration > newestSDSGeneration)
				{
					//fast forward: process now. don't care if we're at the beginning
					Debug.Assert(stack.NewestRegisteredEntry.IsFinished);
					Debug.Assert(newestSDSGeneration == stack.NewestRegisteredSDSGeneration);
					Debug.Assert(stack.NewestConsistentSDSIndex != -1);
					int nextGen = newestSDSGeneration + 1;
					Log.Minor("Processing next TLG g" + nextGen);
					stack.Insert(new SDS(nextGen));
					ctx.SetGeneration(nextGen);
					Debug.Assert(comp == null);
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
							Debug.Assert(comp == null);
							comp = new SDSComputation(timing.NextStepDeadline, ClientMessageQueue, timing.StepComputationTimeWindow,ctx);
							//now wait for remote RCS...
						}
					}
					if (comp == null && timing.TopLevelGeneration == newestSDSGeneration)
					{
						//nothing to recover
						Log.Minor("Nothing to do");
						Clock.SleepUntil(timing.NextStepDeadline);
					}
				}
			}


		}

		public static int EstimateNextSuitableMessageTargetGeneration()
		{
			int step = 1;
			var t0 = BaseDB.Timing;
			if (t0 != null)
			{
				var t = new TimingInfo(t0);
				var remainingWindow = t.StepTimeWindow - t.LatestGenerationElapsed;
				var deliveryTimeEstimate = t.MessageProcessingTimeWindow;
				var at = t.LatestGenerationElapsed + deliveryTimeEstimate;
				while (at > t.StepTimeWindow)
				{
					at -= t.StepTimeWindow;
					step++;
				}
			}
			return stack.NewestRegisteredSDSGeneration + step;
		}



		/// <summary>
		/// Relays an incoming user message to all known siblings
		/// </summary>
		/// <returns>Number of siblings that must relay this message to the local shard</returns>
		public static int RelayMessageToSiblings(ClientMessage msg)
		{
			if (siblings == null || siblings.Count == 0)
				return 0;

			int rs = 0;
			foreach (var s in siblings)
			{
				if (s.ShouldBeConnected)
				{
					if (!s.IsResponsive)
						return -1;
					s.Set(msg.ID.ToString(),msg);
				}
			}
			return rs;
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
							);
				}
			}
		}



		public static RecoveryCheck CheckMissingRCS(SDSStack.Entry sds)
		{
			RecoveryCheck rs = new RecoveryCheck();
			rs.predecessorIsConsistent = stack.FindGeneration(sds.Generation - 1).IsFullyConsistent;
				//sds.IntermediateSDS.inputConsistent;
			rs.thisIsConsistent = sds.IsFullyConsistent;

			foreach (var other in Simulation.Neighbors)
			{
				var inbound = sds.InboundRCS[other.LinearIndex];
				if (inbound != null && inbound.IsFullyConsistent)
					continue;
				//try get from database:
				SerialRCS rcs = DB.TryGetInbound(other.InboundRCSStackID.Generation(sds.Generation));
				//SerialRCSStack rcsStack = BaseDB.TryGet(other.InboundRCSStackID);
				if (rcs != null)
				{
					sds.InboundRCS[other.LinearIndex] = rcs.Deserialize();
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



		public static EntityRanges ToRanges(BaseDB.ConfigContainer config)
		{
			return new EntityRanges(config.r, config.m, ExtToWorld(config.extent.XYZ));
		}

		public static Box ShardIDToBox(ShardID addr, ShardID ext)
		{
			return Box.OffsetSize(new Vec3(ID.XYZ), new Vec3(1), ID.XYZ + 1 >= ext.XYZ);
		}

		public static void Configure(ShardID addr, BaseDB.ConfigContainer config, bool forceAllLinksPassive)
		{
			CSLogicProvider.AsyncFactory = DB.GetLogicProviderAsync;

			ID = addr;
			ext = config.extent;
			Ranges = ToRanges(config);
			MySpace = ShardIDToBox(addr, ext);


			InconsistencyCoverage.CommonResolution = (int)Math.Ceiling(1f / Ranges.R);

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

		public static Box ExtToWorld(Int3 extent)
		{
			return Box.FromMinAndMax(Vec3.Zero, new Vec3(extent), Bool3.True);
		}

		public static Box MySpace { get; private set; } = Box.OffsetSize(Vec3.Zero, new Vec3(1), Bool3.True);


		public static Box FullSimulationSpace
		{
			get
			{
				return ExtToWorld(ext.XYZ);
			}
		}





		internal static void FetchIncoming(Link lnk, object obj)
		{
			if (obj is RCS.Serial)
			{
				//Stopwatch w = Stopwatch.StartNew();
				var r = (RCS.Serial)obj;
				obj = new Tuple<int, RCS>(r.Generation, new RCS(r.Data));
				//var t = w.Elapsed;
				//Log.Message(lnk.Name + ": Translated incoming RCS in " + t);
			}
			incoming.Add(new Tuple<Link, object>(lnk, obj));
		}

		private static void CheckIncoming(int currentTLG, SimulationContext ctx)
		{
			Tuple<Link, object> pair;
			while (incoming.TryTake(out pair))
			{
				object obj = pair.Item2;
				Link lnk = pair.Item1;

				if (obj is ClientMessage)
				{
					var msg = (ClientMessage)obj;
					ClientMessageQueue.Confirm(msg);
					return;
				}

				if (obj is OldestGeneration)
				{
					int gen = ((OldestGeneration)obj).Generation;
					if (gen == lnk.OldestGeneration)
					{
						if (siblings.AllResponsive && siblings.OldestGeneration >= gen)
							DB.RemoveInboundRCSsAsync(neighbors.Select(sibling => sibling.InboundRCSStackID), gen).Wait();	//maybe only now responsive

						Log.Minor("OldestGen update from sibling " + lnk + ": Warning: Already moved to generation " + gen);
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
							ClientMessage msg = o as ClientMessage;
							if (msg != null)
								return msg.Body.RecordedTLG + 2 >= gen;
							return true;
						});
						if (siblings.AllResponsive && siblings.OldestGeneration >= gen)
							DB.RemoveInboundRCSsAsync(neighbors.Select(sibling => sibling.InboundRCSStackID), gen).Wait();
					}
					return;
				}
				if (obj is Tuple<int, RCS>)
				{
					var rcs = (Tuple<int, RCS>)obj;
					if (rcs.Item1 <= stack.NewestConsistentSDSGeneration)
					{
						Log.Error("RCS update from sibling " + lnk + ": Rejected. Already moved past generation " + rcs.Item1);
						return;
					}
					FetchNeighborUpdate(stack.AllocateGeneration(rcs.Item1), lnk, rcs.Item2);
					return;
				}
				if (obj is RCS.Serial)
				{
					RCS.Serial rcs = (RCS.Serial)obj;
					if (rcs.Generation <= stack.NewestConsistentSDSGeneration)
					{
						Log.Error("RCS update from sibling " + lnk + ": Rejected. Already moved past generation " + rcs.Generation);
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
						Log.Minor("SDS update from sibling or DB: Rejected. Already moved past generation " + raw.Generation);
						return;
					}
					var existing = stack.FindGeneration(raw.Generation);
					if (existing.IsFullyConsistent)
					{
						Log.Minor("SDS update from sibling or DB: Rejected. Generation already consistent: " + raw.Generation);
						return;
					}
					SDS sds = raw.Deserialize();
					if (existing.SDS.ICMessagesAndEntitiesAreEqual(sds))
					{
						Log.Minor("SDS update from sibling or DB: Equal. Ignoring");
						return;
					}
					if (sds.IsFullyConsistent)
					{
						Log.Minor("SDS update from sibling or DB: Accepted generation " + raw.Generation);
						stack.Insert(sds);
						return;
					}
					SDS merged = existing.SDS.MergeWith(sds,SDS.MergeStrategy.ExclusiveWithPositionCorrection,ctx);
					Log.Minor("SDS update from sibling " + lnk + ": Merged generation " + raw.Generation);
					if (merged.Inconsistency < existing.SDS.Inconsistency)
						stack.Insert(merged);
					if (merged.Inconsistency < sds.Inconsistency)
						lnk.Set(new SDS.ID(ID.XYZ, raw.Generation).P2PKey, new SerialSDS(merged, Simulation.ID.XYZ));
					return;
				}

				Log.Error("Unsupported update from sibling " + lnk + ": " + obj.GetType());
			}
		}


		public static bool HaveSibling(Link lnk)
		{
			foreach (var s in siblings)
				if (s == lnk)
					return true;
			return false;
		}

		internal static void Shutdown()
		{
			if (neighbors != null)
				foreach (var n in neighbors)
					n.Dispose();
			if (siblings != null)
				foreach (var s in siblings)
					s.Dispose();
			siblings = null;
			neighbors = null;
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