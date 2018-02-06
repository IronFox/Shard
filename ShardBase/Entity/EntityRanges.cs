using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	public struct EntityRanges
	{
		/// <summary>
		/// Maximum movement range
		/// </summary>
		public readonly float M;
		/// <summary>
		/// Maximum influence range
		/// </summary>
		public readonly float R;
		/// <summary>
		/// Maximum sensor range
		/// </summary>
		public readonly float S;

		/// <summary>
		/// Entire space available to simulation.
		/// If an entity leaves this space, then it may be lost
		/// </summary>
		public readonly Box World;

		public EntityRanges(float r, float m, float s, Box world)
		{
			M = m;
			R = r;
			S = s;
			World = world;
		}

	}

}
