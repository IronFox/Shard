using System;

namespace Consensus
{
	internal interface IDispatchable
	{
		void OnArrive(Hub receiver, Connection sender);
	}
}
