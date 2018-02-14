using Shard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using VectorMath;

namespace UnityShardViewer
{
	public class Manager : MonoBehaviour
	{
		/// <summary>
		/// Sets the frame object prototype to use for each sector
		/// </summary>
		public GameObject cubePrototype;
		/// <summary>
		/// Sets the game object prototype to use for each entity
		/// </summary>
		public GameObject entityPrototype;

		/// <summary>
		/// 
		/// </summary>
		public PeerAddress rootHost;


		Dictionary<PeerAddress, Sector> sectors = new Dictionary<PeerAddress, Sector>();



		public void Start()
		{
			AddSector(rootHost);
			
		}

		public void AddSector(PeerAddress host, ShardID id)
		{
			AddSector(host);	//ignore id for now
		}

		public void AddSector(PeerAddress host)
		{
			GameObject sec = new GameObject(host.ToString());
			sec.transform.parent = transform;
			Sector s = sec.AddComponent<Sector>();
			s.CubePrototype = cubePrototype;
			s.EntityPrototype = entityPrototype;
			s.Host = host;
			
			sectors.Add(host, s);
			s.onNewID = id =>
			{

			};
			s.OnNewNeighbor = h =>
			{
				if (!sectors.ContainsKey(h.Address))
					AddSector(h.Address,h.ShardID);
			};

		}


		public void Update()
		{


		}





	}
}
