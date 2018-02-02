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


		public EntityRanges(float r, float m, float s)
		{
			M = m;
			R = r;
			S = s;
		}

	}

}
