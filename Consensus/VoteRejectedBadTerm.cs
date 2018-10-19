using System;

namespace Consensus
{
	[Serializable]
	internal class VoteRejectedBadTerm : Package
	{

		public VoteRejectedBadTerm(int currentTerm):base(currentTerm)
		{}

		public override void OnProcess(Node receiver, Connection sender)
		{
			receiver.SignalVoteRejectedBadTerm(this.Term, sender);
		}
	}
}