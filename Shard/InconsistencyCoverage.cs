namespace Shard
{
	public class InconsistencyCoverage
	{
		
		public InconsistencyCoverage(DBEntry iC)
		{
			
		}

		public bool IsFullyConsistent { get { return Inconsistency == 0; } }
		public int Inconsistency { get; private set; }

		public class DBEntry
		{
		}
	}
}