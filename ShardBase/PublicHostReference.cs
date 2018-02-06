using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	public struct PublicHostReference
	{
		public readonly ShardID ShardID;
		public readonly string Host;

		public PublicHostReference(ShardID id, string host)
		{
			ShardID = id;
			Host = host;
		}
	}
}
