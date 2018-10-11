using System;

namespace Consensus
{
	public interface IDispatchable
	{
		void OnArrive(Member receiver, Connection sender);
	}
}
