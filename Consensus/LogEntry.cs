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

		internal void Execute(Node location)
		{
			Operation.Commit(location);
		}

		public override string ToString()
		{
			return Operation + "@t=" + Term;
		}
	}
}