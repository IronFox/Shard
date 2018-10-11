using System.Collections.Generic;

namespace Consensus
{
	internal class AppendEntries : Package
	{

		public override void OnProcess(Member receiver, Connection sender)
		{
			receiver.SignalAppendEntries(this,sender);
		}

		public readonly LogEntry[] Entries;
		public readonly int PrevLogIndex;
		public readonly int PrevLogTerm;
		public readonly int LeaderCommit;


		public AppendEntries(Member source, int firstLogIndex) : this(source, source.LogSubSet(firstLogIndex))
		{ }

		public AppendEntries(Member source, LogEntry[] entries) : base(source.CurrentTerm)
		{
			Entries = entries;
			LeaderCommit = source.CommitIndex;
			int cnt = entries != null ? entries.Length : 0;
			PrevLogIndex = source.LogSize - cnt;
			PrevLogTerm = source.GetLogTerm(PrevLogIndex);
		}

		public AppendEntries(Member source) : this(source, (LogEntry[])null)
		{ }

		public AppendEntries(Member source, List<LogEntry> entries) : this(source, entries?.ToArray())
		{}

		public AppendEntries(Member source, LogEntry entry): this(source, entry != null ? new LogEntry[] { entry } : null)
		{}

		public void onReceive(ActorLink sender, ActorLogicInterface iface, ConsensusLogic receiver)
		{
		}

	}
}