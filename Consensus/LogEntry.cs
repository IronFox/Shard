using System;

namespace Consensus
{
	public class LogEntry
	{
		public int Term { get; internal set; }

		internal void Execute(Member parent)
		{
			throw new NotImplementedException();
		}
	}
}