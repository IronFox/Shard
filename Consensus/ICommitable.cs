namespace Consensus
{
	public interface ICommitable
	{
		void Commit(Connector hub);
	}
}