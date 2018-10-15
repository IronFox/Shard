using System;

namespace Consensus
{
	[Serializable]
	public class LogEntry
	{
		public readonly ICommitable Operation;
		public readonly int Term;

		public LogEntry(int term, ICommitable op)
		{
			Term = term;
			Operation = op;
		}

		internal void Execute(Member location)
		{
			Operation.Commit(location);
		}
	}
}