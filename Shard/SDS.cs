using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{

	[Serializable]
	public class SDS
	{

		public class Serial : SerialGenerationObject
		{
			public byte[] SerialEntities { get; set; }
			public InconsistencyCoverage.Serial IC { get; set; }

			public override bool Equals(object obj)
			{
				var other = obj as Serial;
				if (other == null)
					return false;
				return Helper.AreEqual(SerialEntities,other.SerialEntities) && IC.Equals(other.IC);
			}

			public override int GetHashCode()
			{
				return Helper.Hash(this).Add(SerialEntities).Add(IC).GetHashCode();
			}

			public override string ToString()
			{
				return "Serial SDS ["+Helper.Length(SerialEntities) +" byte(s)] IC="+IC;
			}
		}

		[Serializable]
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

		public struct Digest
		{
			byte[] data;

			public Digest(byte[] sha256hash) : this()
			{
				data = sha256hash;
			}

			public override bool Equals(object obj)
			{
				if (!(obj is Digest))
				{
					return false;
				}

				var digest = (Digest)obj;
				return Helper.AreEqual(data,digest.data);
			}

			public override int GetHashCode()
			{
				return data != null ? data.GetHashCode() : -663559515;
			}

			public static bool operator ==(Digest a, Digest b)
			{
				return Helper.AreEqual(a.data, b.data);
			}
			public static bool operator !=(Digest a, Digest b)
			{
				return !(a == b);
			}

		}

		public struct IntermediateData
		{
			public EntityPool entities;
			public Digest inputHash;
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

			public override bool Equals(object obj)
			{
				return obj is IntermediateData && ((IntermediateData)obj) == this;
			}

			public override int GetHashCode()
			{
				return Helper.Hash(this)
					.Add(entities)
					.Add(inputHash)
					.Add(localChangeSet)
					.Add(ic)
					.Add(inputConsistent)
					.GetHashCode();
			}
		};

		public readonly Entity[] FinalEntities;
		public readonly int Generation;
		public readonly InconsistencyCoverage IC;
		public readonly IntermediateData Intermediate;

		public bool SignificantInboundChange { get; private set; }
		public RCS[] InboundRCS { get; private set; } = new RCS[Simulation.NeighborCount];
		public ConcurrentDictionary<Guid, ConcurrentBag<OrderedEntityMessage>> ClientMessages = new ConcurrentDictionary<Guid, ConcurrentBag<OrderedEntityMessage>>();


		public void FetchNeighborUpdate(Link neighbor, RCS.SerialData data)
		{
			var candidate = new RCS(data);
			var existing = InboundRCS[neighbor.LinearIndex];
			bool significant = existing != null && candidate.IC.OneCount < existing.IC.OneCount;
			if (existing!= null && candidate.IC.OneCount > existing.IC.OneCount)
			{
				Console.Error.WriteLine("Unable to incorportate RCS from "+neighbor+": RCS at generation "+Generation+" is worse than known");
				return;
			}
			InboundRCS[neighbor.LinearIndex] = candidate;
			if (significant)
				SignificantInboundChange = true;
		}

		public SDS(Serial dbSDS)
		{
			FinalEntities = Entity.Import(dbSDS.SerialEntities);

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

		public void FetchClientMessage(Guid fromClient, Guid toEntity, byte[] data, int orderIndex)
		{
			ConcurrentBag<OrderedEntityMessage>  bag = ClientMessages.GetOrAdd(toEntity,guid => new ConcurrentBag<OrderedEntityMessage>());
			bag.Add(new OrderedEntityMessage(orderIndex, new EntityMessage(new Actor(fromClient, false), data)));
		}

		public bool IsFullyConsistent { get { return IC != null && !IC.AnySet; } }

		public bool IsSet { get { return IC != null; } }

		public int Inconsistency { get { return IC != null ? IC.OneCount : -1; } }

		public Digest HashDigest
		{
			get
			{
				var f = new BinaryFormatter();
				using (var ms = new MemoryStream())
				{
					f.Serialize(ms, IsFullyConsistent);
					f.Serialize(ms, Generation);
					f.Serialize(ms, FinalEntities);
					f.Serialize(ms, IC);
					ms.Seek(0, SeekOrigin.Begin);
					return new Digest(SHA256.Create().ComputeHash(ms));
				}
			}
		}

		public class Computation
		{
			//private SDS output;
			IntermediateData data;
			int generation;
			SDS old;
			List<EntityEvolutionException> errors;

			public List<EntityEvolutionException> Errors { get { return errors; } }

			public IntermediateData Intermediate { get { return data; } }
			public int Generation { get { return generation; } }


			public Computation(int generation, TimeSpan entityLogicTimeout)
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


				data.ic = untrimmed.Sub(new Int3(1), new Int3(InconsistencyCoverage.CommonResolution));
				errors = data.localChangeSet.Evolve(input.FinalEntities,input.ClientMessages,data.ic,generation, entityLogicTimeout);

				foreach (var n in Simulation.Neighbors)
				{
					IntBox remoteBox = n.ICExportRegion;
					var ic = untrimmed.Sub(remoteBox);
					RCS rcs = new RCS(new EntityChangeSet(data.localChangeSet, n.WorldSpace),ic);
					var oID = n.OutboundRCS;
					if (generation >= n.OldestGeneration)
						n.Set(oID.ToString(), rcs);
					if (rcs.IsFullyConsistent)
						n.OutStack.Put(generation,rcs);
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
					IntBox box = n.ICImportRegion;

					var rcs = old.InboundRCS[n.LinearIndex];
					if (rcs != null)
					{
						cs.Include(rcs.CS);
						ic.Include(rcs.IC, box.Min);
					}
					else
					{
						ic.SetOne(box);
					}
				}
				EntityPool p2 = data.entities.Clone();
				cs.Execute(p2);

				SDS rs = new SDS(generation, p2.ToArray(), ic, data, old.InboundRCS);

				if (!ic.AnySet)
				{
					DB.PutAsync(rs.Export(),false);
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
				SerialRCSStack rcsStack = DB.TryGet(other.InboundRCS);
				var rcs = rcsStack?.FindGeneration(Generation);
				if (rcs.HasValue)
				{
					InboundRCS[other.LinearIndex] = new RCS(rcs.Value);
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
			rs.SerialEntities = Entity.Export(FinalEntities);
			rs.Generation = Generation;
			rs.IC = IC.Export();
			rs._id = Simulation.ID.XYZ.Encoded;
			return rs;
		}


		public bool ICAndEntitiesAreEqual(SDS other)
		{
			return IC.Equals(other.IC) && Helper.AreEqual(FinalEntities, other.FinalEntities);
		}

	}
}