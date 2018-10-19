using System;

namespace Consensus
{
	[Serializable]
	internal class CommitEntry : Package
	{
		private ICommitable e;
		private CommitID c;

		public CommitEntry(CommitID c, int term, ICommitable e) : base(term)
		{
			this.e = e;
			this.c = c;
		}

		public override void OnProcess(Node receiver, Connection sender)
		{
			receiver.Commit(c,e);
		}
	}
}