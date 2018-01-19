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
		public readonly Entity[] FinalEntities;
		public readonly int Generation;
		public readonly InconsistencyCoverage IC;
		public readonly Dictionary<Guid, EntityMessage[]> ClientMessages;




		public SDS(int generation)
		{
			Generation = generation;
		}

		public SDS(int generation, Entity[] entities, InconsistencyCoverage ic, Dictionary<Guid, EntityMessage[]> clientMessages)
		{
			Generation = generation;
			FinalEntities = entities;
			IC = ic;
			ClientMessages = clientMessages;
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


		public bool HasEntity(Guid guid)
		{
			foreach (var e in FinalEntities)
				if (e.ID.Guid == guid)
					return true;
			return false;
		}


		public SDS MergeWith(SDS sds)
		{
			throw new NotImplementedException();
		}



		public bool ICMessagesAndEntitiesAreEqual(SDS other)
		{
			return IC.Equals(other.IC) 
				&& Helper.AreEqual(FinalEntities, other.FinalEntities) 
				&& Helper.AreEqual(ClientMessages, other.ClientMessages);
		}

	}
}