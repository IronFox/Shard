using Base;
using Consensus;
using System;

namespace Witness
{
	internal class Notify : INotifiable
	{
		private Action<string> onQuit;
		private Func<Interface> getNode;
		private Status lastStatus;
		private Identity lastLeader;

		private void UpdateTitle()
		{
			var n = getNode();
			string str = n != null ? n.MyID+" "+ n.BoundAddress+" " : "";
			switch (lastStatus)
			{
				case Status.Follower:
					str += "Following " + lastLeader;
					break;
				case Status.Leader:
					str += "Leader";
					break;
				case Status.NotEstablished:
					str += "No Consensus";
					break;
			}
			Console.Title = str;
		}

		public Notify(Action<string> onQuit, Func<Interface> getNode)
		{
			this.onQuit = onQuit;
			this.getNode = getNode;
		}

		public void OnAddressMismatchConsensusLoss(Address locallyBound, Address globallyRegistered)
		{
			onQuit("Address mismatch. Registered: " + globallyRegistered + ", local: " + locallyBound);
		}

		public void OnConsensusChange(Status newState, Identity newLeader)
		{
			lastStatus = newState;
			lastLeader = newLeader;
			UpdateTitle();
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