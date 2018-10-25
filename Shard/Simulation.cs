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
using Consensus;

namespace Shard
{



	public static class Simulation
	{
		public static ShardID ID { get; private set; }

		public static EntityRanges Ranges = new EntityRanges(1f / 8, -1, Box.OffsetSize(Vec3.Zero, Vec3.One,Bool3.True));

		private static Neighborhood neighbors,
									siblings;

		private static Listener listener;
		private static ObservationLink.Listener observationListener;

		private static SDSStack stack = new SDSStack();
		private static MessageHistory messages = new MessageHistory();

		public static MessageHistory Messages => messages;

		public static Neighborhood Neighbors { get { return neighbors; } }



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

			if (Consensus != null)	//tests
				Consensus.TrimOut(gen - 1);
			Messages.TrimGenerations(gen - 1);
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

		private static bool Complete(SDSComputation comp, TimingInfo timing, bool force)
		{
			if (Clock.Now >= comp.Deadline || force)
			{
				var rs = comp.Complete();
				stack.Insert(rs);
				if (rs.Item1.IsFullyConsistent)
					DB.PutNow(new SerialSDS(rs.Item1, ID.XYZ),true);
				comp = null;
				return true;
			}
			return false;
		}

		private class DefaultNotify : Consensus.INotifiable
		{

			
			public void OnMessageCommit(Address clientAddress, ClientMessage message)
			{
				if (!clientAddress.IsEmpty)
					InteractionLink.OnMessageCommit(clientAddress, message.ID);
				Messages.Add(message);
			}

			public void OnGenerationEnd(int generation)
			{
				Messages.EndGeneration(generation);
			}

			public void OnAddressMismatchConsensusLoss(Address locallyBound, Address globallyRegistered)
			{
				Log.Error("Terminal: Consensus address mismatch: Bound address: " + locallyBound + ", public registration: " + globallyRegistered);
				Environment.Exit(-1);
			}

			public void OnOutOfConfig(Configuration newConfig, Configuration.Member memberID)
			{
				Log.Error("Terminal: Consensus member ID " + memberID+" not found in new configuration "+newConfig);
				Environment.Exit(-1);
			}
		}

		public static void Run(ShardID myID)
		{
			//Host.Domain = ;
			listener = new Listener(h => FindLink(h));
			Consensus = new Consensus.Interface(myID, listener.Port, 0,true,new DefaultNotify());
			Configure(myID, BaseDB.Config,false);

			AdvertiseOldestGeneration(0);


			observationListener = new ObservationLink.Listener(listener.Port - 1000);

			Log.Message("Polling SDS state...");
			SimulationContext ctx = new SimulationContext(false);

			SDS sds;
			while (true)
			{
				var data = DB.Begin(myID.XYZ);
				if (data.Item2 != null)
					Messages.Insert(data.Item2.Generation, data.Item2.Deserialize());
				if (data.Item1 != null)
				{
					sds = data.Item1.Deserialize();
					break;
				}
				CheckIncoming(TimingInfo.Current.TopLevelGeneration,ctx);
				Thread.Sleep(1000);
				Console.Write('.');
				Console.Out.Flush();
			}
			Consensus.ForwardMessageGeneration(sds.Generation + 1);
			Messages.TrimGenerations(sds.Generation);
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
				int currentGen = stack.NewestFinishedSDSGeneration;
				int nextGen = currentGen + 1;
				ctx.SetGeneration(nextGen);
				stack.Append(new SDS(nextGen));
				Debug.Assert(stack.NewestRegisteredEntry.IsFinished);
				stack.Insert(new SDSComputation(Clock.Now,Messages.GetMessages(currentGen), TimingInfo.Current.EntityEvaluationTimeWindow,ctx).Complete());
				Debug.Assert(stack.NewestRegisteredEntry.IsFinished);
				CheckIncoming(TimingInfo.Current.TopLevelGeneration,ctx);
			}
			Log.Message("done. Starting main loop...");


			SDSComputation mainComputation = null, recoveryComputation = null; //main computation plus one recovery computation max

			while (true)
			{
				var timing = TimingInfo.Current;
				CheckIncoming(timing.TopLevelGeneration,ctx);
				Log.Minor("TLG "+stack.NewestFinishedSDSGeneration + "/"+timing.TopLevelGeneration+" @recoveryStepIndex "+timing.LatestRecoveryStepIndex);
				{
					var newest = stack.NewestFinishedSDS;
					string title  = ID+" g" + newest.Generation + " " + (float)(newest.IC.Size.Product - newest.IC.OneCount)*100/ newest.IC.Size.Product+"% consistent";
					var con = stack.NewestConsistentSDS;
					if (con != newest)
						title += ", newest consistent at g" + con.Generation;
					title += ", rec " + timing.LatestRecoveryStepIndex;
					UpdateTitle(title);
				}

				int newestSDSGeneration = stack.NewestFinishedSDSGeneration;
				if (mainComputation == null)
				{
					Debug.Assert(stack.NewestRegisteredEntry.IsFinished);
					Debug.Assert(newestSDSGeneration == stack.NewestRegisteredSDSGeneration);
					Debug.Assert(stack.NewestConsistentSDSIndex != -1);
				}
				if (recoveryComputation != null && Complete(recoveryComputation,timing, timing.TopLevelGeneration != newestSDSGeneration))
					recoveryComputation = null;
				if (mainComputation != null && Complete(mainComputation, timing, timing.TopLevelGeneration != newestSDSGeneration && timing.TopLevelGeneration > mainComputation.Generation))
				{
					mainComputation = null;
					newestSDSGeneration = stack.NewestFinishedSDSGeneration;
					Debug.Assert(stack.NewestRegisteredEntry.IsFinished);
					Debug.Assert(newestSDSGeneration == stack.NewestRegisteredSDSGeneration);
					Debug.Assert(stack.NewestConsistentSDSIndex != -1);
				}

				if (recoveryComputation != null && mainComputation != null)
					Clock.SleepUntil(Helper.Min(recoveryComputation.Deadline, mainComputation.Deadline));
				else
					if (recoveryComputation != null)
						Clock.SleepUntil(recoveryComputation.Deadline);
				else
					if (mainComputation != null)
						Clock.SleepUntil(mainComputation.Deadline);

				if (recoveryComputation != null)	//recovery computations are analogue to main computation, so main computation will not be done. but recovery must be
					continue;

				if (mainComputation == null && timing.TopLevelGeneration > newestSDSGeneration)
				{
					//fast forward: process now. don't care if we're at the beginning
					Debug.Assert(stack.NewestRegisteredEntry.IsFinished);
					Debug.Assert(newestSDSGeneration == stack.NewestRegisteredSDSGeneration);
					Debug.Assert(stack.NewestConsistentSDSIndex != -1);
					int nextGen = newestSDSGeneration + 1;
					Log.Minor("Processing next TLG g" + nextGen);
					stack.Insert(new SDS(nextGen));
					ctx.SetGeneration(nextGen);
					Debug.Assert(mainComputation == null);
					mainComputation = new SDSComputation(timing.NextGenerationDeadline - timing.CSApplicationTimeWindow, Messages.GetMessages(newestSDSGeneration), timing.EntityEvaluationTimeWindow,ctx);
				}
				
				if (timing.ShouldStartRecovery)
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
							recoveryComputation = new SDSComputation(timing.NextRecoveryStepDeadline, Messages.GetMessages(ctx.GenerationNumber-1), timing.EntityEvaluationTimeWindow,ctx);
							//now wait for remote RCS...
						}
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
			return new EntityRanges(config.r, config.m, ExtToWorld(config.extent));
		}

		public static Box ShardIDToBox(ShardID addr, ShardID ext)
		{
			return Box.OffsetSize(new Vec3(addr.XYZ), new Vec3(1), addr.XYZ + 1 >= ext.XYZ);
		}

		public static Box SDToBox(Int3 addr, Int3 ext)
		{
			return Box.OffsetSize(new Vec3(addr), new Vec3(1), addr + 1 >= ext);
		}

		public static void Configure(ShardID addr, BaseDB.ConfigContainer config, bool forceAllLinksPassive)
		{
			CSLogicProvider.AsyncFactory = DB.GetLogicProviderAsync;

			ID = addr;
			gridExt = config.extent;
			Ranges = ToRanges(config);
			MySpace = SDToBox(addr.XYZ, config.extent);


			InconsistencyCoverage.CommonResolution = (int)Math.Ceiling(1f / Ranges.R);

			if (Extent.ReplicaLevel > 1)
				siblings = Neighborhood.NewSiblingList(addr, Extent.ReplicaLevel, forceAllLinksPassive);
			neighbors = Neighborhood.NewNeighborList(addr, Extent.XYZ, forceAllLinksPassive);
		}

		public static bool Owns(Vec3 position)
		{
			if (position.FloorInt3 == ID.XYZ)
				return true;
			return MySpace.Contains(position);
		}

		private static Int3 gridExt = Int3.One;


		public static int ReplicaCount
		{
			get
			{
				var cfg = BaseDB.SD;
				if (cfg == null)
					return 1;
				return cfg.replicaCount;
			}
		}
		public static ShardID Extent => new ShardID(gridExt, ReplicaCount);

		public static Box ExtToWorld(Int3 extent)
		{
			return Box.FromMinAndMax(Vec3.Zero, new Vec3(extent), Bool3.True);
		}

		public static Box MySpace { get; private set; } = Box.OffsetSize(Vec3.Zero, new Vec3(1), Bool3.True);


		public static Box FullSimulationSpace
		{
			get
			{
				return ExtToWorld(Extent.XYZ);
			}
		}

		public static Consensus.Interface Consensus { get; private set; }

		internal static void FetchIncoming(Link lnk, object obj)
		{
			if (obj is SerialCCS)
			{
				var c = (SerialCCS)obj;
				obj = new Tuple<int, MessagePack>(c.Generation, (MessagePack)Helper.Deserialize(c.Data));
			}
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
					Log.Error("Received client message via peer socket");
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

				if (obj is Tuple<int, MessagePack>)
				{
					//Debug.Assert(HaveSibling(lnk));
					var m = (Tuple<int, MessagePack>)obj;
					if (m.Item1 <= stack.OldestSDSGeneration)
					{
						Log.Minor("CCS update from sibling or DB: Rejected. Already moved past generation " + m.Item1);
						return;
					}
					Messages.Insert(m.Item1, m.Item2);
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