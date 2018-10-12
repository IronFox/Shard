﻿using System;

namespace Consensus
{
	[Serializable]
	internal class VoteConfirm : Package
	{
		public VoteConfirm(int term) : base(term)
		{
		}

		public override void OnProcess(Member receiver, Connection sender)
		{
			receiver.ProcessVoteConfirmation(sender,Term);
		}

		public override string ToString()
		{
			return "VoteConfirm t" + Term;
		}
	}
}