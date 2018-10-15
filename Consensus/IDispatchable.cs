using System;

namespace Consensus
{
	internal interface IDispatchable
	{
		void OnArrive(Node receiver, Connection sender);
	}
}
