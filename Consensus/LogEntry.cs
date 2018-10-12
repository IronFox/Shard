using System;

namespace Consensus
{
	public class LogEntry
	{
		public readonly ICommitable Operation;
		public readonly int Term;

		public LogEntry(int term, ICommitable op)
		{
			Term = term;
			Operation = op;
		}

		internal void Execute()
		{
			Operation.Commit();
		}
	}
}