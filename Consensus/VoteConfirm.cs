namespace Consensus
{
	internal class VoteConfirm : Package
	{
		public VoteConfirm(int term) : base(term)
		{
		}

		public override void OnProcess(Member receiver, Connection sender)
		{
			receiver.ProcessVoteConfirmation(sender);
		}
	}
}