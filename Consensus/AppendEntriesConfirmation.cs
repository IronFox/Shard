using System;

namespace Consensus
{
	[Serializable]
	internal class AppendEntriesConfirmation : IndexedPackage
	{
		public readonly bool Succeeded, Yield;
		public readonly int LastCommit;

		public AppendEntriesConfirmation(Node source, bool success, bool yield) : base(source)
		{
			Yield = yield;
			Succeeded = success;
			LastCommit = source.CommitIndex;
		}

		public override string ToString()
		{
			string rs = "AppendConfirm{";
			rs += "Succeeded=" + Succeeded;
			rs += ",Yield=" + Yield;
			rs += ",LastCommit=" + LastCommit;
			rs += ",Term=" + Term;
			rs += ",Last=" + LastLogIndex;
			rs += "}";
			return rs;
		}

		public override void OnProcess(Node instance, Connection c)
		{
			if (instance.CurrentState == Node.State.Leader)
			{
				if (Yield)
				{
					instance.Yield();
					return;
				}


				var info = c.ConsensusState;
				if (Succeeded)
				{
					//iface.log("Append entries confirmed by remote "+msg.getSender()+". commit="+conf.myLastCommit);
					info.MatchIndex = LastLogIndex;
					info.NextIndex = LastLogIndex + 1;
					info.CommitIndex = LastCommit;
					if (info.MatchIndex == instance.LogSize)
						info.AppendTimeout = PreciseTime.None;
					instance.ReCheckCommitment();
				}
				else
				{
					instance.LogEvent("Append entries rejected by remote " + info);
					info.MatchIndex = LastCommit;
					info.NextIndex = LastCommit + 1;
					c.Dispatch(new AppendEntries(instance, info.NextIndex));
					info.AppendTimeout = instance.GetAppendMessageTimeout();
				}
			}
		}


		public override void OnBadTermIgnore(Node processor, Connection sender)
		{
			//sender.Dispatch(new AppendEntriesConfirmation(processor, false, true));
		}

	}
}