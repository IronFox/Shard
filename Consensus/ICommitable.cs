namespace Consensus
{
	public interface ICommitable
	{
		void Commit(Hub hub);
	}
}