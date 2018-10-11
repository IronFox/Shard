namespace Consensus
{
	internal class RequestVote : IndexedPackage
	{
		public readonly int LastLogTerm;

		public RequestVote(Member source) : base(source)
		{
			LastLogTerm = source.GetLogTerm(LastLogIndex);
		}

		public RequestVote(int term, int lastLogIndex, int lastLogTerm) : base(term, lastLogIndex)
		{
			LastLogTerm = lastLogTerm;
		}

		public bool IsUpToDate(Member target)
		{
			int logSize = target.LogSize;
			int myTerm = target.GetLogTerm(logSize);
			if (myTerm < LastLogTerm)
				return true;
			if (myTerm > LastLogTerm)
				return false;
			return LastLogIndex >= logSize;
		}

		public override void OnProcess(Member receiver, Connection sender)
		{
			receiver. 
			bool upToDate = IsUpToDate(instance);
			if ((instance.votedFor == null || instance.votedFor == sender.getDestinationActor() || term > instance.currentTerm) && upToDate)
			{
				instance.state = State.Follower;
				instance.log(iface, "Recognized vote request for term " + term + " from " + sender.getDestinationActor());
				instance.nextActionAt = instance.getElectionTimeout();
				instance.votedFor = sender.getDestinationActor();
				instance.currentTerm = term;
				sender.sendMessage(new VoteConfirm(instance.currentTerm));
			}
			else
				instance.log(iface, "Rejected vote request for term " + term + " (at term " + instance.currentTerm + ", upToDate=" + upToDate + ") from " + sender.getDestinationActor());
		}
	}
}