using System;

namespace Consensus
{
	[Serializable]
	internal class CommitEntry : Package
	{
		private ICommitable e;

		public CommitEntry(int term, ICommitable e) : base(term)
		{
			this.e = e;
		}

		public override void OnProcess(Connector receiver, Connection sender)
		{
			receiver.Commit(e);
		}
	}
}