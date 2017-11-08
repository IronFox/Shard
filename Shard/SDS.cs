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
			public string _id, _rev;
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
			public Entity[] entities;
			public Hasher.Hash inputHash;
			public EntityChangeSet localChangeSet;
		};

		public readonly Entity[] FinalEntities;
		public readonly int Generation;
		public readonly bool InputConsistent;
		public readonly InconsistencyCoverage IC;
		public readonly string Revision;
		public readonly IntermediateData Intermediate;

		public bool SignificantInboundChange { get; private set; }
		public RCS[] OutboundRCS { get; private set; } = new RCS[Simulation.NeighborCount];
		public RCS[] InboundRCS { get; private set; } = new RCS[Simulation.NeighborCount];


		public void FetchNeighborUpdate(Link neighbor, RCS.Serial entry)
		{
			var candidate = new RCS(entry);
			var existing = InboundRCS[neighbor.LinearIndex];
			bool significant = existing != null && candidate.IC.Inconsistency < existing.IC.Inconsistency;
			if (existing!= null && candidate.IC.Inconsistency > existing.IC.Inconsistency)
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

			Revision = dbSDS._rev;
			FinalEntities = Entity.Import(dbSDS.Entities);

			Generation = dbSDS.Generation;
			IC = new InconsistencyCoverage(dbSDS.IC);
		}

		public SDS(int generation)
		{
			Generation = generation;
		}

		public bool IsFullyConsistent { get { return IC != null && IC.IsFullyConsistent; } }

		public bool IsSet { get { return IC != null; } }

		public int Inconsistency { get { return IC != null ? IC.Inconsistency : -1; } }



		public class Computation
		{
			//private SDS output;
			InconsistencyCoverage ic;
			IntermediateData data;
			RCS[] outbound;

			public Computation(int generation)
			{
				SDS input = Simulation.FindGeneration(generation - 1);
				SDS old = Simulation.FindGeneration(generation);
				//output = new SDS(generation);
				input.IC.VerifyIntegrity();
				using (Hasher hasher = new Hasher())
				{
					hasher.Add(input.IsFullyConsistent);
					foreach (var e in input.FinalEntities)
						e.AddTo(hasher);
					input.IC.AddTo(hasher);
					hasher.Add(generation);
					data.inputHash = hasher.Finish();
				}
				Debug.Assert(old == null || !old.IsFullyConsistent);
				if (old != null && old.Intermediate.inputHash == data.inputHash)
				{
					data = old.Intermediate;
					ic = input.IC.Grow(true);
					outbound = old.OutboundRCS;
					return;
				}
				outbound = new RCS[Simulation.NeighborCount];
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

				ic = input.IC.Grow(true);
				data.entities = new Entity[input.FinalEntities.Length];
				data.localChangeSet = new EntityChangeSet();

				Parallel.For(0, data.entities.Length, (i) =>
				{
					data.entities[i] = input.FinalEntities[i].Evolve(data.localChangeSet);
				});
				

				foreach (var n in Simulation.Neighbors)
				{
					var delta = n.ID.XYZ - Simulation.ID.XYZ;
					RCS rcs = outbound[n.LinearIndex] = new RCS(generation);
					rcs.Generation = 
				rcs.ref.reset(new RCS(caller));
				rcs.ref->ic.CopyCoreArea(-delta, ic);
				rs.localCS.ExportEdge(delta, shard.gridCoords, rcs.ref->cs);
				input->hGrid.core.ExportEdge(rcs.ref->hGrid, delta);
				rcs.confirmed = shard.neighbors[i].shard == nullptr;
				ASSERT_EQUAL__(rcs.confirmed, !motionSpace.Contains(shard.gridCoords + delta));


				if (consistentSuccessorMatch && rcs.ref->ic.IsFullyConsistent())
						rcs.ref->Verify(consistentSuccessorMatch->outboundRCS[i].ref);


				if (rcs.ref->ic.IsFullyConsistent() && shard.neighbors[i].shard)
					{
					const auto id = DB::ID(&shard, shard.neighbors[i].shard, rs.GetGeneration());
					shard.client.Upload(id, rcs.ref);
				}

				rcs.ref->VerifyIntegrity(CLOCATION);


				if (input->IsFullyConsistent())
					ASSERT__(rcs.ref->ic.IsFullyConsistent());

			}







			//			throw new NotImplementedException();
		}

		public SDS Complete()
			{
				throw new NotImplementedException();
			}
		}

		public struct RecoveryCheck
		{
			public int		missingRCS,
							rcsAvailableFromNeighbor,
							outRCSUpdatable,
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
								|| outRCSUpdatable > 0
								|| (missingRCS == 0 && predecessorIsConsistent)
								|| rcsRestoredFromDB > 0
							);
				}
			}
		}

		internal RecoveryCheck CheckMissingRCS()
		{
			RecoveryCheck rs = new RecoveryCheck();
			rs.predecessorIsConsistent = InputConsistent;
			rs.thisIsConsistent = IsFullyConsistent;

			foreach (var other in Simulation.Neighbors)
			{
				if (InputConsistent && (!OutboundRCS[other.LinearIndex].IsFullyConsistent))
					rs.outRCSUpdatable++;
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
			rs._rev = Revision;
			rs._id = Simulation.ID.XYZ.Encoded;
			return rs;
		}
	}
}