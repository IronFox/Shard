using Base;
using System;
using System.Collections.Generic;

namespace Consensus
{
	[Serializable]
	internal class AppendEntries : Package
	{

		public override void OnProcess(Node receiver, Connection sender)
		{
			receiver.SignalAppendEntries(this,sender);
		}

		public readonly LogEntry[] Entries;
		public readonly int PrevLogLength;
		public readonly int PrevLogTerm;
		public readonly int LeaderCommit;
		public readonly int SkipTo = -1;


		public AppendEntries(Node source, int firstLogIndex) : this(
			source, 
			source.LogSubSet(Math.Max(firstLogIndex, source.LogOffset+1)),
			firstLogIndex < source.LogOffset+1 ? source.LogOffset : -1)
		{ }

		public AppendEntries(Node source, LogEntry[] entries, int skipTo) : base(source.CurrentTerm)
		{
			SkipTo = skipTo;
			Entries = entries;
			LeaderCommit = source.CommitIndex;
			int cnt = entries != null ? entries.Length : 0;
			PrevLogLength = source.LogSize - cnt;
			PrevLogTerm = source.GetLogTerm(PrevLogLength);
		}

		public override string ToString()
		{
			int len = Helper.Length(Entries);
			string b = "AppendEntries{[";
			if (len > 0)
				b += (PrevLogLength+1)+"..."+ (PrevLogLength + len+1);
			b += "],commit=" + LeaderCommit + "}";
			return b;
		}

		public AppendEntries(Node source) : this(source, (LogEntry[])null,-1)
		{ }

		public AppendEntries(Node source, List<LogEntry> entries) : this(source, entries?.ToArray(), -1)
		{}

		public AppendEntries(Node source, LogEntry entry): this(source, entry != null ? new LogEntry[] { entry } : null, -1)
		{}

	}
}