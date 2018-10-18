using System;

namespace Consensus
{
	public interface ICommitable
	{
		void Commit(Node node);
	}
}