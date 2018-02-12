using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	public class Neighborhood : IEnumerable<Link>
	{
		private Link[] links;

		public int Count
		{
			get
			{
				return links != null ? links.Length : 0;
			}
		}
		public IEnumerator<Link> GetEnumerator()
		{
			return ((IEnumerable<Link>)links).GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return links.GetEnumerator();
		}

		public void AdvertiseOldestGeneration(OldestGeneration gen)
		{
			foreach (var lnk in links)
				lnk.Set("OldestGeneration", gen);
		}

		public Link Find(ShardID id)
		{
			foreach (var n in links)
				if (n.ID == id)
					return n;
			return null;
		}
		public Link Find(PeerAddress addr)
		{
			foreach (var n in links)
				if (n.ShardPeerAddress.Address == addr)
					return n;
			return null;
		}

		public Link Find(Int3 coordinates)
		{
			foreach (var n in links)
				if (n.ID.XYZ == coordinates)
					return n;
			return null;
		}

		private Neighborhood(int numLinks)
		{
			links = new Link[numLinks];
		}
		private Neighborhood(IEnumerable<Link> links)
		{
			this.links = Helper.ToArray(links);
		}

		public static Neighborhood NewSiblingList(ShardID myAddr, int replicaLevel, bool forceAllLinksPassive)
		{
			int at = 0;
			Neighborhood n = new Neighborhood(replicaLevel - 1);
			for (int i = 0; i < replicaLevel; i++)
				if (i != myAddr.ReplicaLevel)
				{
					n.links[at] = new Link(new ShardID(myAddr.XYZ, i), i > myAddr.ReplicaLevel && !forceAllLinksPassive, at, true);
					at++;
				}
			return n;
		}

		public static Neighborhood NewNeighborList(ShardID myAddr, Int3 extent, bool forceAllLinksPassive)
		{
			List<Link> neighbors = new List<Link>();
			Int3 at = Int3.Zero;
			for (at.X = myAddr.X - 1; at.X <= myAddr.X + 1; at.X++)
				for (at.Y = myAddr.Y - 1; at.Y <= myAddr.Y + 1; at.Y++)
					for (at.Z = myAddr.Z - 1; at.Z <= myAddr.Z + 1; at.Z++)
					{
						if (at == myAddr.XYZ)
							continue;

						if ((at >= Int3.Zero).All && (at < extent).All)
						{
							int linear = neighbors.Count;
							neighbors.Add(new Link(new ShardID(at, myAddr.ReplicaLevel), at.OrthographicCompare(myAddr.XYZ) > 0 && !forceAllLinksPassive, linear, false));
						}
					}
			return new Neighborhood(neighbors);
		}
	}
}
