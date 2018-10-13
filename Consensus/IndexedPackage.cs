using System;

namespace Consensus
{
	[Serializable]
	internal abstract class IndexedPackage : Package
	{
		public readonly int LastLogIndex;

		public IndexedPackage(Hub source) : base(source.CurrentTerm)
		{
			LastLogIndex = source.LogSize;
		}

		protected IndexedPackage(int term, int lastLogIndex) : base(term)
		{
			LastLogIndex = lastLogIndex;
		}
	}
}