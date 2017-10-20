using System;
using System.Diagnostics;
using VectorMath;

namespace Shard
{
	public struct EntityAppearance
	{
		public readonly Vec3 Position;

	}


	public abstract class EntityLogic
	{
		public abstract class State
		{
			public abstract State Evolve(EntityAppearance oldState, out EntityAppearance newState);
			public abstract byte[] BinaryState { get; }
		}

		public abstract State Instantiate(byte[] binaryState);
	}
	

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


		public readonly Entity[] Entities;
		public readonly int Generation;
		public readonly bool InputConsistent;
		public readonly InconsistencyCoverage IC;
		public readonly string Revision;

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
			Entities = Entity.Import(dbSDS.Entities);

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
			public Computation(int generation)
			{
				throw new NotImplementedException();
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
			rs.Entities = Entity.Export(Entities);
			rs.Generation = Generation;
			rs.IC = IC.Export();
			rs._rev = Revision;
			rs._id = Simulation.ID.XYZ.Encoded;
			return rs;
		}
	}
}