using System;
using VectorMath;

namespace Shard
{
	public class InconsistencyCoverage
	{
		private BitCube data;


		public InconsistencyCoverage(Int3 size)
		{
			data = new BitCube(size);
		}


		public InconsistencyCoverage(Serial serial)
		{
			
		}

		public bool IsFullyConsistent { get { return Inconsistency == 0; } }
		public int Inconsistency { get; private set; }

		public class Serial
		{
		}

		internal Serial Export()
		{
			throw new NotImplementedException();
		}
	}
}