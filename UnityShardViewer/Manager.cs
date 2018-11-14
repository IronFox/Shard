using Base;
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
		//public Address rootHost;


		Dictionary<Address, Sector> sectors = new Dictionary<Address, Sector>();
		Dictionary<ShardID, Sector> shardMap = new Dictionary<ShardID, Sector>();




		public void Start()
		{
			//if (rootHost.IsSet)
				//AddSector(rootHost,ShardID. null);
			
		}

		public void ConnectTo(Address dbHost, ShardID id)
		{
			BaseDB.Connect(dbHost);
			BaseDB.GetAddress(id,addr => AddSector(addr.ObserverAddress,addr.ShardID));
		}

		public void AddSector(Address host, ShardID id)
		{
			Sector existing;
			if (shardMap.TryGetValue(id,out existing))
			{
				existing.Host = host;
				return;
			}
			AddSector(host, id, sec=> shardMap.Add(id, sec));
		}

		public Action<string, Action<GameObject>> OnCreateObject { get; set; }

		public void AddSector(Address host, ShardID idk, Action<Sector> onCreate)
		{
			Sector rs;
			if (sectors.TryGetValue(host, out rs))
			{
				onCreate?.Invoke(rs);
				return;
			}


			OnCreateObject(idk.ToString(),sec =>
			{
				sec.transform.parent = transform;
				Sector s = sec.AddComponent<Sector>();
				s.CubePrototype = cubePrototype;
				s.EntityPrototype = entityPrototype;
				s.Host = host;
				s.ExpectedID = idk;

				sectors.Add(host, s);
				s.onNewID = id =>
				{

				};
				s.OnNewNeighbor = (id, addr) =>
				{
					AddSector(addr, id);
				};
				onCreate?.Invoke(s);
			});
		}


		public void Update()
		{


		}





	}
}
