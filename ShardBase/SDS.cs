using Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{

	[Serializable]
	public class SDS
	{


		[Serializable]
		public struct ID
		{
			public readonly Int3 ShardID;
			public readonly int Generation;


			public int[] IntArray=>new int[]{ShardID.X,ShardID.Y,ShardID.Z,Generation };
			
			public ID(int[] array, int offset)
			{
				ShardID = new Int3(array, offset);
				Generation = array[offset + 3];
			}
			public ID(Int3 shardID, int generation)
			{
				this.ShardID = shardID;
				this.Generation = generation;
			}

			public override string ToString()
			{
				return ShardID + "g" + Generation;
			}
			public string P2PKey
			{
				get
				{
					return ShardID.Encoded + "g" + Generation;
				}
			}

			public override int GetHashCode()
			{
				return ShardID.GetHashCode() * 31 + Generation.GetHashCode();
			}

			public static bool operator ==(ID a, ID b)
			{
				return a.ShardID == b.ShardID && a.Generation == b.Generation;
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

		public readonly Entity[] FinalEntities;
		public readonly int Generation;
		public readonly InconsistencyCoverage IC;



		public SDS(int generation)
		{
			Generation = generation;
		}

		public SDS(int generation, Entity[] entities, InconsistencyCoverage ic)
		{
			Generation = generation;
			FinalEntities = entities;
			IC = ic;
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
					return new Digest(SHA256.Create().ComputeHash(ms),true);
				}
			}
		}


		public bool HasEntity(Guid guid)
		{
			foreach (var e in FinalEntities)
				if (e.ID.Guid == guid)
					return true;
			return false;
		}


		public enum MergeStrategy
		{
			Exclusive,
			ExclusiveWithPositionCorrection,
			EntitySelective
		}

		private static int SelectExclusiveSource(SDS a, SDS b)
		{
			int balance = 0;
			var e0 = a.IC.Bits.GetEnumerator();
			var e1 = b.IC.Bits.GetEnumerator();
			while (e0.MoveNext() && e1.MoveNext())
			{
				balance += e0.Current.CompareTo(e1.Current);
			}
			return balance < 0 ? -1 : 1;

		}


		private static void MergeInconsistentEntitiesComp(EntityPool pool, SDS s0, SDS s1, InconsistencyCoverage ic, EntityChange.ExecutionContext ctx)
		{
			var a = new EntityPool(s0.FinalEntities, ctx);
			var b = new EntityPool(s1.FinalEntities, ctx);

			const float searchScope = 0.5f;

			foreach (var e0 in a)
			{
				if (pool.Contains(e0.ID.Guid))
					continue;   //already good
								//entity is inconsistent and not in merged state yet
				var c0 = ctx.LocalSpace.Relativate(e0.ID.Position);
				var e1 = b.Find(e0.ID.Guid);
				var c1 = e1 != null ? ctx.LocalSpace.Relativate(e1.ID.Position) : Vec3.Zero;
				if (!ic.IsInconsistentR(c0))
				{
					//this is tricky. entity not merged, but would reside in consistent space (bad).
					if (e1 != null)
					{
				//		ASSERT__(b.ic.IsInconsistent(c1));	//otherwise it would have been added prior, and we would not be here
						//so this entity exists in both SDS'
						{
							//we now have the same entity twice, both inconsistent, residing each in the consistent space of the other SDS'
							//let's assume the entity isn't supposed to exist here anyways

							Entity candidate = null;
							int sc = s0.IC.GetInconsistencyAtR(c0).CompareTo(s1.IC.GetInconsistencyAtR(c1));
							if (sc< 0)
								candidate = e0;
							else if(sc > 0)
								candidate = e1;
							else if(e0.CompareTo(e1) < 0)
								candidate = e0;
							else
								candidate = e1;


							var c = ctx.LocalSpace.Relativate(candidate.ID.Position);
							if (ic.FindInconsistentPlacementCandidateR(ref c, searchScope))
							{
								Entity me = candidate.Relocate(ctx.LocalSpace.DeRelativate(c));
								pool.Insert(me);
							}
						}
					}
					else
					{
						//entity exists only in local SDS.
						//let's assume the entity isn't supposed to exist here anyways

						//TEntityCoords c = Frac(e0->coordinates);
						//if (merged.ic.FindInconsistentPlacementCandidate(c,searchScope))
						//{
						//	Entity copy = *e0;
						//	copy.coordinates = c + shardOffset;
						//	//ASSERT__(merged.ic.IsInconsistent(Frac(copy.coordinates)));
						//	merged.entities.InsertEntity(copy);
						//}
						//else
						//	FATAL__("bad");
					}
				}
				else
				{
					//entity location is inconsistent in both SDS'. This is expected to be the most common case
					if (e1 != null)
					{
						if ( e1.Equals(e0) )
						{
							//probably actually consistent
		//						ASSERT__(merged.ic.IsInconsistent(Frac(e0->coordinates)));
							pool.Insert(e0);
						}
						else
						{

							if (!ic.IsInconsistentR(c1))
							{
								Debug.Assert(ic.IsInconsistentR(c0));
								pool.Insert(e0);
							}
							else
							{
								Entity candidate = null;
								int sc = s0.IC.GetInconsistencyAtR(c0).CompareTo(s1.IC.GetInconsistencyAtR(c1));
								if (sc < 0)
									candidate = e0;
								else if (sc > 0)
									candidate = e1;
								else if (e0.CompareTo(e1) < 0)
									candidate = e0;
								else
									candidate = e1;
					
								//common case. Choose one
								//ASSERT__(ic.IsInconsistentR(candidate->coordinates-shardOffset));
								pool.Insert(candidate);
							}
						}
					}
					else
					{
						//only e0 exists
						int sc = s0.IC.GetInconsistencyAtR(c0).CompareTo(s1.IC.GetInconsistencyAtR(c0));
						//ASSERT__(merged.ic.IsInconsistent(Frac(e0->coordinates)));
						if (sc <= 0)
							pool.Insert(e0);
					}
				}
			}
			foreach (var e0 in b)
			{
				if (pool.Contains(e0.ID.Guid))
					continue;	//already good
				//entity is inconsistent and not in merged state yet
				var c0 = ctx.LocalSpace.Relativate(e0.ID.Position);
				//const auto c1 = e1 ? (e1->coordinates - shardOffset) : TEntityCoords();
				if (!ic.IsInconsistentR(c0))
				{
					#if false
					//this is tricky. entity not merged, but would reside in consistent space (bad).
					if (e1)
					{
						//case handled
						//FATAL__("bad");
					}
					else
					{
						//entity exists only in local SDS.
						//let's assume the entity isn't supposed to exist here anyways

						//TEntityCoords c = Frac(e0->coordinates);
						//if (merged.ic.FindInconsistentPlacementCandidate(c,searchScope))
						//{
						//	Entity copy = *e0;
						//	copy.coordinates = c + shardOffset;
						//	//ASSERT__(merged.ic.IsInconsistent(Frac(copy.coordinates)));
						//	merged.entities.InsertEntity(copy);
						//}
					/*	else
							FATAL__("bad");*/
					}
					#endif 
				}
				else
				{
					var e1 = a.Find(e0.ID.Guid);
					//entity location is inconsistent in both SDS'. This is expected to be the most common case
					if (e1 != null)
					{
						//case handled
						//FATAL__("bad");
					}
					else
					{
						//only e0 exists
						int sc = s0.IC.GetInconsistencyAtR(c0).CompareTo(s1.IC.GetInconsistencyAtR(c0));
						//ASSERT__(merged.ic.IsInconsistent(Frac(e0->coordinates)));
						if (sc >= 0)
							pool.Insert(e0);
					}
				}
			}

		}

		private static void MergeInconsistentEntitiesEx(EntityPool pool, SDS source, bool correctLocations, InconsistencyCoverage ic, EntityChange.ExecutionContext ctx)
		{
			const float searchScope = 0.5f;

			foreach (var e0 in source.FinalEntities)
			{
				if (pool.Contains(e0.ID.Guid))
					continue;	//already good

				var c0 = ctx.LocalSpace.Relativate(e0.ID.Position);
				if (ic.IsInconsistentR(c0))
				{
					pool.Insert(e0);
				}
				else
				{
					if (correctLocations)
					{
						var c = c0;
						if (ic.FindInconsistentPlacementCandidateR(ref c, searchScope))
						{
							Entity me = e0.Relocate(c);
							pool.Insert(me);
						}
					}
					//only increases overaccounted entities:
					//if (merged.ic.FindInconsistentPlacementCandidate(c0,searchScope))
					//{
					//	Entity me = *e0;
					//	me.coordinates = c0 + shardOffset;
					//	merged.entities.InsertEntity(me);
					//}
				}
			}
		}


		private class DualEntityMessages
		{
			//sorted by message ID:
			Dictionary<Guid, ClientMessage>
				a = new Dictionary<Guid, ClientMessage>(),
				b = new Dictionary<Guid, ClientMessage>();


			public void AddA(ClientMessage m)
			{
				a.Add(m.ID.MessageID, m);
			}

			public void AddB(ClientMessage m)
			{
				b.Add(m.ID.MessageID, m);
			}

			internal void MergeTo(List<ClientMessage> list)
			{

				foreach (var t in a)
				{
					ClientMessage msg;
					if (b.TryGetValue(t.Key, out msg))
					{
						int cmp = t.Value.CompareTo(msg);
						if (cmp <= 0)
							list.Add(t.Value);
						else
							list.Add(msg);
					}
					else
						list.Add(t.Value);
				}

				foreach (var t in b)
					if (!a.ContainsKey(t.Key))
						list.Add(t.Value);
			}
		}

		private class ActorMessages
		{
			public Dictionary<Guid, DualEntityMessages> messages = new Dictionary<Guid, DualEntityMessages>();

			public void AddA(Guid receivingEntity, ClientMessage message)
			{
				messages.GetOrCreate(receivingEntity).AddA(message);
			}
			public void AddB(Guid receivingEntity, ClientMessage message)
			{
				messages.GetOrCreate(receivingEntity).AddB(message);
			}

			public void MergeTo(Dictionary<Guid, List<ClientMessage>> outMessages)
			{
				foreach (var t in messages)
				{
					t.Value.MergeTo(outMessages.GetOrCreate(t.Key));
				}
			}
		}

		public SDS MergeWith(SDS other, MergeStrategy strategy, EntityChange.ExecutionContext ctx)
		{
			if (Generation != other.Generation)
				throw new IntegrityViolation("Generation mismatch: "+Generation+" != "+other.Generation);

			SDS exclusiveSource = null;
			int exclusiveChoice = 0;

			if (strategy == MergeStrategy.Exclusive || strategy == MergeStrategy.ExclusiveWithPositionCorrection)
			{
				exclusiveChoice = SelectExclusiveSource(this, other);
				exclusiveSource = (exclusiveChoice == -1 ? this : other);
			}

			InconsistencyCoverage merged = InconsistencyCoverage.GetMinimum(IC, other.IC);
			EntityPool pool = new EntityPool(ctx);

			foreach (var e in this.FinalEntities)
			{
				if (IC.IsInconsistentR(ctx.LocalSpace.Relativate(e.ID.Position)))
					continue;   //for now
				pool.Insert(e);
			}
			foreach (var e in other.FinalEntities)
			{
				if (other.IC.IsInconsistentR(ctx.LocalSpace.Relativate(e.ID.Position)))
					continue;   //for now
				if (pool.Contains(e.ID.Guid))
					continue;
				pool.Insert(e);
			}
			//at this point we merged all fully consistent entities from either. If the inconsistent areas did not overlap then the result should contain all entities in their consistent state


			if (!merged.IsFullyConsistent)
			{
				if (strategy == MergeStrategy.EntitySelective)
					MergeInconsistentEntitiesComp(pool, this, other, merged, ctx);
				else
					MergeInconsistentEntitiesEx(pool, exclusiveSource, strategy == MergeStrategy.ExclusiveWithPositionCorrection,merged, ctx);
			}
			return new SDS(Generation,pool.ToArray(),merged);
		}



		public bool ICMessagesAndEntitiesAreEqual(SDS other)
		{
			return IC.Equals(other.IC) 
				&& Helper.AreEqual(FinalEntities, other.FinalEntities) 
				;
		}

	}
}