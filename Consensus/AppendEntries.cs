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


		public AppendEntries(Node source, int firstLogIndex) : this(source, source.LogSubSet(firstLogIndex))
		{ }

		public AppendEntries(Node source, LogEntry[] entries) : base(source.CurrentTerm)
		{
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

		public AppendEntries(Node source) : this(source, (LogEntry[])null)
		{ }

		public AppendEntries(Node source, List<LogEntry> entries) : this(source, entries?.ToArray())
		{}

		public AppendEntries(Node source, LogEntry entry): this(source, entry != null ? new LogEntry[] { entry } : null)
		{}

	}
}