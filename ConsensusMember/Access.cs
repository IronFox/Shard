using Base;
using Shard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Consensus
{
	public static class Access
	{
		private static Member connector;
		public static ShardID MyID { get; private set; }

		public static void Begin(ShardID myID, int peerPort)
		{
			Func<Address>[] addresses = new Func<Address>[3]; //0, -1, -2
			int at = -myID.ReplicaLevel;
			for (int i = 0; i < 3; i++)
				if (i != at)
					addresses[at] = () => BaseDB.TryGetConsensusAddress(new ShardID(myID.XYZ, i));
				//else
				//	addresses[at] = null;	//default
			

			Consensus.Configuration cfg = new Configuration(addresses);
			MyID = myID;
			connector = new Member(cfg, at);

			int consensusPort = connector.Address().Port;


			{
				Log.Message("Detecting address...");
				//https://stackoverflow.com/questions/6803073/get-local-ip-address - Mr.Wang from Next Door
				string localIP;
				using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
				{
					socket.Connect("8.8.8.8", 65530);
					IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
					localIP = endPoint.Address.ToString();
				}
				var address = new FullShardAddress(myID, localIP,peerPort, consensusPort);
				Log.Message("Publishing address: " + address);
				BaseDB.PutNow(address);
			}


		}
	}
}
