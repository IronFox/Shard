namespace Consensus
{
	internal abstract class IndexedPackage : Package
	{
		public readonly int LastLogIndex;

		public IndexedPackage(Member source) : base(source.CurrentTerm)
		{
			LastLogIndex = source.LogSize;
		}

		protected IndexedPackage(int term, int lastLogIndex) : base(term)
		{
			LastLogIndex = lastLogIndex;
		}
	}
}