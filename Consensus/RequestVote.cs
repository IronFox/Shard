using System;

namespace Consensus
{
	[Serializable]
	internal class RequestVote : IndexedPackage
	{
		public readonly int LastLogTerm;

		public RequestVote(Node source) : base(source)
		{
			LastLogTerm = source.GetLogTerm(LastLogIndex);
		}

		public RequestVote(int term, int lastLogIndex, int lastLogTerm) : base(term, lastLogIndex)
		{
			LastLogTerm = lastLogTerm;
		}

		public bool IsUpToDate(Node target)
		{
			int logSize = target.LogSize;
			int myTerm = target.GetLogTerm(logSize);
			if (myTerm < LastLogTerm)
				return true;
			if (myTerm > LastLogTerm)
				return false;
			return LastLogIndex >= logSize;
		}

		public override void OnProcess(Node receiver, Connection sender)
		{
			receiver.ProcessVoteRequest(sender, Term, IsUpToDate(receiver));
		}
	}
}