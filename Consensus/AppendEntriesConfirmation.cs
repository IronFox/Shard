using System;

namespace Consensus
{
	[Serializable]
	internal class AppendEntriesConfirmation : IndexedPackage
	{
		public readonly bool Succeeded;
		public readonly int LastCommit;

		public AppendEntriesConfirmation(Hub source, bool success) : base(source)
		{
			Succeeded = success;
			LastCommit = source.CommitIndex;
		}

		public override void OnProcess(Hub instance, Connection c)
		{
			if (instance.CurrentState == Hub.State.Leader)
			{
				var info = c.ConsensusState;
				if (Succeeded)
				{
					//iface.log("Append entries confirmed by remote "+msg.getSender()+". commit="+conf.myLastCommit);
					info.MatchIndex = LastLogIndex;
					info.NextIndex = LastLogIndex + 1;
					info.CommitIndex = LastCommit;
					if (info.MatchIndex == instance.LogSize)
						info.AppendTimeout = -1;
					instance.ReCheckCommitment();
				}
				else
				{
					instance.LogEvent("Append entries rejected by remote " + info);
					info.MatchIndex = LastLogIndex;
					info.NextIndex = LastLogIndex + 1;
					c.Dispatch(new AppendEntries(instance, info.NextIndex));
					info.AppendTimeout = instance.GetAppendMessageTimeout();
				}
			}
		}


		public override void OnBadTermIgnore(Hub processor, Connection sender)
		{
			sender.Dispatch(new AppendEntriesConfirmation(processor, false));
		}

	}
}