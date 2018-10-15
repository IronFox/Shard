using System;

namespace Consensus
{
	[Serializable]
	internal abstract class IndexedPackage : Package
	{
		public readonly int LastLogIndex;

		public IndexedPackage(Node source) : base(source.CurrentTerm)
		{
			LastLogIndex = source.LogSize;
		}

		protected IndexedPackage(int term, int lastLogIndex) : base(term)
		{
			LastLogIndex = lastLogIndex;
		}
	}
}