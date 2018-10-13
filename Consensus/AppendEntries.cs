using System;
using System.Collections.Generic;

namespace Consensus
{
	[Serializable]
	internal class AppendEntries : Package
	{

		public override void OnProcess(Hub receiver, Connection sender)
		{
			receiver.SignalAppendEntries(this,sender);
		}

		public readonly LogEntry[] Entries;
		public readonly int PrevLogIndex;
		public readonly int PrevLogTerm;
		public readonly int LeaderCommit;


		public AppendEntries(Hub source, int firstLogIndex) : this(source, source.LogSubSet(firstLogIndex))
		{ }

		public AppendEntries(Hub source, LogEntry[] entries) : base(source.CurrentTerm)
		{
			Entries = entries;
			LeaderCommit = source.CommitIndex;
			int cnt = entries != null ? entries.Length : 0;
			PrevLogIndex = source.LogSize - cnt;
			PrevLogTerm = source.GetLogTerm(PrevLogIndex);
		}

		public AppendEntries(Hub source) : this(source, (LogEntry[])null)
		{ }

		public AppendEntries(Hub source, List<LogEntry> entries) : this(source, entries?.ToArray())
		{}

		public AppendEntries(Hub source, LogEntry entry): this(source, entry != null ? new LogEntry[] { entry } : null)
		{}

	}
}