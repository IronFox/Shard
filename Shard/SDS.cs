using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{

	public class SDS
	{
		public class Serial
		{
			public string _id;
			public int Generation { get; set; }
			public Entity.Serial[] Entities { get; set; }
			public InconsistencyCoverage.Serial IC { get; set; }
		}


		public struct ID
		{
			public readonly Int3 shardID;
			public readonly int generation;

			public ID(Int3 shardID, int generation)
			{
				this.shardID = shardID;
				this.generation = generation;
			}

			public override string ToString()
			{
				return shardID + "g" + generation;
			}
			public string P2PKey
			{
				get
				{
					return shardID.Encoded + "g" + generation;
				}
			}

			public override int GetHashCode()
			{
				return shardID.GetHashCode() * 31 + generation.GetHashCode();
			}

			public static bool operator ==(ID a, ID b)
			{
				return a.shardID == b.shardID && a.generation == b.generation;
			}
			public static bool operator !=(ID a, ID b)
			{
				return !(a == b);
			}

			public override bool Equals(object obj)
			{
				return (obj is ID) && ((ID)obj) == (this);
			}
		}


		public struct IntermediateData
		{
			public EntityPool entities;
			public Hasher.Hash inputHash;
			public EntityChangeSet localChangeSet;
			public InconsistencyCoverage ic;
			public bool inputConsistent;


			public static bool operator==(IntermediateData a, IntermediateData b)
			{
				return a.entities == b.entities 
					&& a.inputHash == b.inputHash 
					&& a.localChangeSet == b.localChangeSet 
					&& a.ic == b.ic 
					&& a.inputConsistent == b.inputConsistent;
			}

			public static bool operator!=(IntermediateData a, IntermediateData b)
			{
				return !(a == b);
			}
		};

		public readonly Entity[] FinalEntities;
		public readonly int Generation;
		public readonly InconsistencyCoverage IC;
		public readonly IntermediateData Intermediate;

		public bool SignificantInboundChange { get; private set; }
		public RCS[] InboundRCS { get; private set; } = new RCS[Simulation.NeighborCount];


		public void FetchNeighborUpdate(Link neighbor, RCS.Serial entry)
		{
			var candidate = new RCS(entry);
			var existing = InboundRCS[neighbor.LinearIndex];
			bool significant = existing != null && candidate.IC.OneCount < existing.IC.OneCount;
			if (existing!= null && candidate.IC.OneCount > existing.IC.OneCount)
			{
				Console.Error.WriteLine("Unable to incorportate RCS from "+neighbor+": RCS at generation "+entry.Generation+" is worse than known");
				return;
			}
			InboundRCS[neighbor.LinearIndex] = candidate;
			if (significant)
				SignificantInboundChange = true;
		}

		public SDS(Serial dbSDS)
		{
			foreach (var e in dbSDS.Entities)
				e.BeginFetchLogic();

			FinalEntities = Entity.Import(dbSDS.Entities);

			Generation = dbSDS.Generation;
			IC = new InconsistencyCoverage(dbSDS.IC);
		}


		public SDS(int generation)
		{
			Generation = generation;
		}

		public SDS(int generation, Entity[] entities, InconsistencyCoverage ic, IntermediateData intermediate, RCS[] inbound)
		{
			Intermediate = intermediate;
			Generation = generation;
			FinalEntities = entities;
			IC = ic;
			if (inbound != null)
				InboundRCS = inbound;
		}

		public bool IsFullyConsistent { get { return IC != null && !IC.AnySet; } }

		public bool IsSet { get { return IC != null; } }

		public int Inconsistency { get { return IC != null ? IC.OneCount : -1; } }

		public Hasher.Hash HashDigest
		{
			get
			{
				using (Hasher hasher = new Hasher())
				{
					hasher.Add(IsFullyConsistent);
					foreach (var e in FinalEntities)
						e.Hash(hasher);
					IC.Hash(hasher);
					hasher.Add(Generation);
					return hasher.Finish();
				}
			}
		}

		public class Computation
		{
			//private SDS output;
			IntermediateData data;
			int generation;
			SDS old;

			public IntermediateData Intermediate { get { return data; } }
			public int Generation { get { return generation; } }


			public Computation(int generation)
			{
				SDSStack stack = Simulation.Stack;
				this.generation = generation;
				SDS input = stack.FindGeneration(generation - 1);
				old = stack.FindGeneration(generation);
				if (old == null)
					throw new IntegrityViolation("Unable to locate original SDS at generation "+generation);
				data.inputConsistent = input.IsFullyConsistent;
				//output = new SDS(generation);
				data.inputHash = input.HashDigest;
				
				if (old.Intermediate.inputHash == data.inputHash)
				{
					data = old.Intermediate;
					return;
				}
				//			rs.processed = input->entities;


				//			foreach (userMessages,msg)
				//{
				//				Entity* e = rs.processed.FindEntity(msg->target.guid);
				//				if (!e)
				//					LogUnexpected("User Message: Unable to find target entity", msg->target);
				//				else
				//				{
				//					auto ws = e->FindLogic(msg->targetProcess);
				//					if (!ws)
				//						LogUnexpected("User Message: Unable to find target logic process", msg->target);
				//					else
				//						ws->receiver.Append().data = msg->message;
				//				}
				//			}

				InconsistencyCoverage untrimmed = input.IC.Grow(false);
				if (untrimmed.Size != InconsistencyCoverage.CommonResolution + 2)
					throw new IntegrityViolation("IC of unsupported size: "+untrimmed.Size);
				data.entities = new EntityPool(input.FinalEntities);
				data.localChangeSet = new EntityChangeSet();

				Parallel.For(0, input.FinalEntities.Length, (i) =>
				{
					try
					{
						input.FinalEntities[i].Evolve(data.localChangeSet,generation);
					}
					catch (Exception ex)
					{
						untrimmed.FlagInconsistentR(Simulation.MySpace.Relativate(input.FinalEntities[i].ID.Position), Int3.One);
						Log.Error(input.FinalEntities[i] + ": " + ex);
					}
				});

				data.ic = untrimmed.Sub(new Int3(1), new Int3(InconsistencyCoverage.CommonResolution));

				foreach (var n in Simulation.Neighbors)
				{
					IntBox remoteBox = n.ICExportRegion;
					var ic = untrimmed.Sub(remoteBox);
					RCS rcs = new RCS(new EntityChangeSet(data.localChangeSet, n.WorldSpace),ic);
					var oID = n.OutboundRCS(generation);
					if (generation >= n.OldestGeneration)
						n.Set(oID.ID.ToString(), rcs);
					if (rcs.IsFullyConsistent)
						DB.Put(rcs.Export(oID));
				}
				data.localChangeSet.FilterByTargetLocation(Simulation.MySpace);
			}

			public SDS Complete()
			{
				//Log.Message("Finalize SDS g" + generation); 

				var cs = data.localChangeSet.Clone();
				InconsistencyCoverage ic = data.ic.Clone();
				foreach (var n in Simulation.Neighbors)
				{
					var delta = n.ID.XYZ - Simulation.ID.XYZ;
					Int3 offset = (delta * InconsistencyCoverage.CommonResolution).Clamp(0, InconsistencyCoverage.CommonResolution - 1);
					Int3 end = (delta * InconsistencyCoverage.CommonResolution + InconsistencyCoverage.CommonResolution - 1).Clamp(0, InconsistencyCoverage.CommonResolution - 1);

					var rcs = old.InboundRCS[n.LinearIndex];
					if (rcs != null)
					{
						cs.Include(rcs.CS);
						ic.Include(rcs.IC, offset);
					}
					else
					{
						ic.SetOne(offset, end - offset + 1);
					}
				}
				EntityPool p2 = data.entities.Clone();
				cs.Execute(p2);

				SDS rs = new SDS(generation, p2.ToArray(), ic, data, old.InboundRCS);

				if (!ic.AnySet)
				{
					DB.Put(rs.Export());
				}
				return rs;
			}
		}

		public bool HasEntity(Guid guid)
		{
			foreach (var e in FinalEntities)
				if (e.ID.Guid == guid)
					return true;
			return false;
		}

		public struct RecoveryCheck
		{
			public int		missingRCS,
							rcsAvailableFromNeighbor,
							rcsRestoredFromDB;
			public bool		predecessorIsConsistent,
							thisIsConsistent;

			public bool AllThere { get { return missingRCS == 0; } }
			public bool MissingAvailableFromNeighbors { get { return missingRCS == rcsAvailableFromNeighbor; } }
			//public bool MissingAvailableFromAnywhere { get { return MissingRCS == RCSAvailableFromNeighbor + RCSAvailableFromDatabase; }  }
			public bool AnyAvailableFromNeighbors { get { return rcsAvailableFromNeighbor > 0; }  }
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

		public RecoveryCheck CheckMissingRCS()
		{
			RecoveryCheck rs = new RecoveryCheck();
			rs.predecessorIsConsistent = Intermediate.inputConsistent;
			rs.thisIsConsistent = IsFullyConsistent;

			foreach (var other in Simulation.Neighbors)
			{
				var inbound = InboundRCS[other.LinearIndex];
				if (inbound != null && inbound.IsFullyConsistent)
					continue;
				//try get from database:
				RCS.Serial rcs = DB.TryGet(other.InboundRCS(Generation));
				if (rcs != null)
				{
					InboundRCS[other.LinearIndex] = new RCS(rcs);
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

		public SDS MergeWith(SDS sds)
		{
			throw new NotImplementedException();
		}

		public Serial Export()
		{
			Serial rs = new Serial();
			rs.Entities = Entity.Export(FinalEntities);
			rs.Generation = Generation;
			rs.IC = IC.Export();
			rs._id = Simulation.ID.XYZ.Encoded;
			return rs;
		}
	}
}