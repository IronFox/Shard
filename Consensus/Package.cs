using System;

namespace Consensus
{
	[Serializable]
	internal abstract class Package : IDispatchable
	{
		public readonly int Term;

		public Package(int term) => Term = term;

		public abstract void OnProcess(Node receiver, Connection sender);

		public virtual void OnBadTermIgnore(Node processor, Connection sender) { }

		public void OnArrive(Node receiver, Connection sender)
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