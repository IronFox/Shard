using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	public struct EntityRanges
	{
#if STATE_ADV
		/// <summary>
		/// Maximum movement range
		/// </summary>
		public readonly float M;
		/// <summary>
		/// Maximum sensor range
		/// </summary>
		public readonly float S;
#else
		public float M => R;
#endif
		/// <summary>
		/// Maximum influence range
		/// </summary>
		public readonly float R;

		/// <summary>
		/// Entire space available to simulation.
		/// If an entity leaves this space, then it may be lost
		/// </summary>
		public readonly Box World;

		public EntityRanges(float r, float m, float s, Box world)
		{
#if STATE_ADV
			M = m;
			S = s;
#endif
			R = r;
			World = world;
		}

	}

}
