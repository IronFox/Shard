using System;

namespace Consensus
{
	internal interface IDispatchable
	{
		void OnArrive(Connector receiver, Connection sender);
	}
}
