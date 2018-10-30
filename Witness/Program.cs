using Base;
using Shard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Witness
{
	class Program
	{
		static Consensus.Interface iface;

		static void Main(string[] args)
		{
			if (args.Length < 2)
				throw new ArgumentException("Missing arguments. Usage: Executable <DBHost> <ShardID>");
			int at = 0;
			var dbHost = new Address(args[at++]);
			BaseDB.Connect(dbHost);//,"admin","1234");
			ShardID addr = ShardID.Decode(args[at++]);
			BaseDB.BeginPullConfig(addr.XYZ);

			iface = new Consensus.Interface(addr, -1, 0, true,Consensus.Interface.ThreadOperations.CheckConfiguration, new Notify(error =>
			{
				Log.Error(error);
				iface.Dispose();
				Log.Message("Shutting down");
			},() => iface));
			iface.Notify.OnConsensusChange(Consensus.Status.NotEstablished, null);

			iface.AwaitClosure();

		}
	}
}
