using Base;
using Shard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AddressLookup
{
	class Program
	{
		static void Main(string[] args)
		{
			if (args.Length < 2)
			{
				Console.Error.WriteLine("Usage: <program> <db address:port> <shard ID.");
				Environment.Exit(-1);
			}
			try
			{
				BaseDB.Connect(new Address(args[0]));
				ShardID id = ShardID.Decode(args[1]);

				var rs = BaseDB.TryGetAddress(id);
				while (rs.IsEmpty)
				{
					Thread.Sleep(1000);
					rs = BaseDB.TryGetAddress(id);
				}
				Console.WriteLine(rs.PeerAddress);
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine(ex);
				Environment.Exit(-1);
			}
		}
	}
}
