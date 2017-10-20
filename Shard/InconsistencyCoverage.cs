using System;

namespace Shard
{
	public class InconsistencyCoverage
	{
		private BitCube data;





		public InconsistencyCoverage(Serial iC)
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