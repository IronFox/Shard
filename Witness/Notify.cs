using Base;
using Consensus;
using System;

namespace Witness
{
	internal class Notify : INotifiable
	{
		private Action<string> onQuit;
		public Notify(Action<string> onQuit)
		{
			this.onQuit = onQuit;
		}

		public void OnAddressMismatchConsensusLoss(Address locallyBound, Address globallyRegistered)
		{
			onQuit("Address mismatch. Registered: " + globallyRegistered + ", local: " + locallyBound);
		}

		public void OnGenerationEnd(int generation)
		{
			Log.Message("Generation ended: "+generation);
		}

		public void OnMessageCommit(Address clientAddress, Shard.ClientMessage message)
		{
			Log.Message("Client message "+message.ID+" from "+clientAddress+" logged");
		}

		public void OnOutOfConfig(Consensus.Configuration newConfig, Consensus.Configuration.Member memberID)
		{
			onQuit("Dropped out of config. Consensus config is now "+newConfig+", member ID is "+memberID);
		}
	}
}