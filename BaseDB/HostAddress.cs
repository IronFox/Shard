using System;
using System.Collections.Generic;
using System.Diagnostics;
using Base;

namespace Shard
{

	[Serializable]
	public struct ShardPeerAddress
	{
		public readonly ShardID ShardID;
		public readonly Address Address;

	}

	[Serializable]
	public struct FullShardAddress
	{
		public readonly ShardID ShardID;
		public readonly string Host;
		public readonly int PeerPort, ConsensusPort;

		public FullShardAddress(ShardID id, string host, int peerPort, int consensusPort)
		{
			ShardID = id;
			Host = host;
			PeerPort = peerPort;
			ConsensusPort = consensusPort;
		}

		public Address PeerAddress => new Address(Host, PeerPort);

		public Address ConsensusAddress => new Address(Host, ConsensusPort);

		public override string ToString()
		{
			return ShardID + "->" + Host + ":"+PeerPort+"|"+ConsensusPort;
		}
	}
}