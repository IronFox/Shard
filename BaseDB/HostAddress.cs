using System;
using System.Collections.Generic;
using System.Diagnostics;
using Base;

namespace Shard
{

	[Serializable]
	public struct FullShardAddress
	{
		public readonly ShardID ShardID;
		public readonly string Host;
		public readonly int PeerPort, ConsensusPort, ObserverPort;

		public FullShardAddress(ShardID id, string host, int peerPort, int consensusPort, int observerPort)
		{
			ShardID = id;
			Host = host;
			PeerPort = peerPort;
			ConsensusPort = consensusPort;
			ObserverPort = observerPort;
		}

		public Address ObserverAddress => new Address(Host, ObserverPort);
		public Address PeerAddress => new Address(Host, PeerPort);
		public Address ConsensusAddress => new Address(Host, ConsensusPort);

		public bool IsEmpty => Host == null;

		public override string ToString()
		{
			return ShardID + "->" + Host + ":"+PeerPort+"|"+ConsensusPort+"|"+ObserverPort;
		}
	}
}