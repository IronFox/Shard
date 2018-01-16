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
			public InconsistencyCoverage.DBSerial IC { get; set; }
			public byte[] SerialMessages { get; set; }

			public override bool Equals(object obj)
			{
				var other = obj as Serial;
				if (other == null)
					return false;
				return Helper.AreEqual(SerialEntities,other.SerialEntities) 
						&& IC.Equals(other.IC)
						&& Helper.AreEqual(SerialMessages, other.SerialMessages)
						;
			}

			public override int GetHashCode()
			{
				return Helper.Hash(this).Add(SerialEntities).Add(IC).Add(SerialMessages).GetHashCode();
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
		public readonly Dictionary<Guid, EntityMessage[]> ClientMessages;

		public bool SignificantInboundChange { get; private set; }
		public RCS[] InboundRCS { get; private set; } = new RCS[Simulation.NeighborCount];


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
			ClientMessages = dbSDS.SerialMessages != null ? (Dictionary<Guid,EntityMessage[]>) Helper.Deserialize(dbSDS.SerialMessages) : null;
			Generation = dbSDS.Generation;
			IC = new InconsistencyCoverage(dbSDS.IC);
		}


		public SDS(int generation)
		{
			Generation = generation;
		}

		public SDS(int generation, Entity[] entities, InconsistencyCoverage ic, IntermediateData intermediate, RCS[] inbound, Dictionary<Guid, EntityMessage[]> clientMessages)
		{
			Intermediate = intermediate;
			Generation = generation;
			FinalEntities = entities;
			IC = ic;
			ClientMessages = clientMessages;
			if (inbound != null)
				InboundRCS = inbound;
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
					if (ClientMessages != null)
						f.Serialize(ms, ClientMessages);
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
			List<EntityError> errors;
			Dictionary<Guid, EntityMessage[]> clientMessages;

			public List<EntityError> Errors { get { return errors; } }

			public IntermediateData Intermediate { get { return data; } }
			public int Generation { get { return generation; } }

			/// <summary>
			/// Time at which this computation will be completed.
			/// Ignored during tests
			/// </summary>
			public readonly DateTime Deadline;

			public Computation(int generation, DateTime stepDeadline, ClientMessageQueue freshClientMessages, TimeSpan entityLogicTimeout)
			{
				Deadline = stepDeadline;
				SDSStack stack = Simulation.Stack;
				this.generation = generation;
				SDS input = stack.FindGeneration(generation - 1);
				//if (input.Generation != generation-1)
				//	throw new IntegrityViolation("Generation mismatch");
				old = stack.FindGeneration(generation);
				if (old == null)
					throw new IntegrityViolation("Unable to locate original SDS at generation "+generation);
				//if (old.Generation != generation)
				//	throw new IntegrityViolation("Generation mismatch");
				data.inputConsistent = input.IsFullyConsistent;
				//output = new SDS(generation);
				data.inputHash = input.HashDigest;

				clientMessages = freshClientMessages?.Collect(generation);
				if (clientMessages == null)	//nothing new, check locally archived...
					clientMessages = old.ClientMessages;


				if (old.Intermediate.inputHash == data.inputHash)
				{
					data = old.Intermediate;
					return;
				}



				InconsistencyCoverage untrimmed = input.IC.Grow(false);
				if (untrimmed.Size != InconsistencyCoverage.CommonResolution + 2)
					throw new IntegrityViolation("IC of unsupported size: "+untrimmed.Size);
				data.entities = new EntityPool(input.FinalEntities);
				data.localChangeSet = new EntityChangeSet();


				data.ic = untrimmed.Sub(new Int3(1), new Int3(InconsistencyCoverage.CommonResolution));
				bool doSendClientMessages = freshClientMessages != null && freshClientMessages.ArchivedGeneration == generation;
				errors = data.localChangeSet.Evolve(input.FinalEntities,clientMessages,data.ic,generation, doSendClientMessages, entityLogicTimeout);
				if (errors == null && input.IsFullyConsistent && data.ic.OneCount != 0)
					throw new IntegrityViolation("Input is fully consistent, and there are no errors. IC should have remaining empty");


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

				SDS rs = new SDS(generation, p2.ToArray(), ic, data, old.InboundRCS,clientMessages);

				if (!ic.AnySet)
				{
					DB.PutAsync(rs.Export(),false).Wait();
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
			rs.SerialMessages = ClientMessages != null ? Helper.SerializeToArray(ClientMessages) : null;
			rs._id = Simulation.ID.XYZ.Encoded;
			return rs;
		}


		public bool ICMessagesAndEntitiesAreEqual(SDS other)
		{
			return IC.Equals(other.IC) 
				&& Helper.AreEqual(FinalEntities, other.FinalEntities) 
				&& Helper.AreEqual(ClientMessages, other.ClientMessages);
		}

	}
}