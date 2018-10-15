namespace Consensus
{
	public interface ICommitable
	{
		void Commit(Member hub);
	}
}