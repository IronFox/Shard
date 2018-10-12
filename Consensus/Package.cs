using System;

namespace Consensus
{
	[Serializable]
	internal abstract class Package
	{
		public readonly int Term;

		public Package(int term) => Term = term;

		public abstract void OnProcess(Member receiver, Connection sender);

		public virtual void OnBadTermIgnore(Member processor, Connection sender) { }
	}

	[Serializable]
	internal class Wrapped : IDispatchable
	{
		public readonly Package Package;

		public Wrapped(Package content) => Package = content;
		public void OnArrive(Member receiver, Connection sender)
		{
			receiver.Receive(new Tuple<Package, Connection>(Package, sender));
		}


		public override string ToString()
		{
			return "Wrapped: " + Package;
		}
	}


}