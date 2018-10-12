using System;

namespace Consensus
{
	internal interface IDispatchable
	{
		void OnArrive(Member receiver, Connection sender);
	}
}
