using System;

namespace Consensus
{
	[Serializable]
	internal abstract class Package : IDispatchable
	{
		public readonly int Term;

		public Package(int term) => Term = term;

		public abstract void OnProcess(Hub receiver, Connection sender);

		public virtual void OnBadTermIgnore(Hub processor, Connection sender) { }

		public void OnArrive(Hub receiver, Connection sender)
		{
			receiver.DoSerialized(() =>
			{
				if (Term < receiver.CurrentTerm)
					OnBadTermIgnore(receiver, sender);
				else
					OnProcess(receiver, sender);
			});
		}
	}




}