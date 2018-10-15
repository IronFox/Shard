using Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	public class IntermediateSDS
	{

		public EntityPool entities;
		public Digest inputHash;
		public EntityChangeSet localChangeSet;
		public InconsistencyCoverage ic;
		public bool inputConsistent;

		public static bool Eq(IntermediateSDS a, IntermediateSDS b)
		{
			return a.entities == b.entities
				&& a.inputHash == b.inputHash
				&& a.localChangeSet == b.localChangeSet
				&& a.ic == b.ic
				&& a.inputConsistent == b.inputConsistent;
		}

		//public static bool operator ==(IntermediateSDS a, IntermediateSDS b)
		//{
		//	return a.entities == b.entities
		//		&& a.inputHash == b.inputHash
		//		&& a.localChangeSet == b.localChangeSet
		//		&& a.ic == b.ic
		//		&& a.inputConsistent == b.inputConsistent;
		//}

		//public static bool operator !=(IntermediateSDS a, IntermediateSDS b)
		//{
		//	return !(a == b);
		//}

		public override bool Equals(object obj)
		{
			return obj is IntermediateSDS && Eq((IntermediateSDS)obj,this);
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

	}
}
