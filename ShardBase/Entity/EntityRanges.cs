using Base;
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
		public readonly float Motion;
		/// <summary>
		/// Maximum transmission range
		/// </summary>
		public readonly float Transmission;

		/// Maximum influence range
		/// </summary>
		public readonly float R;

		public readonly bool DisplacedTransmission;

		/// <summary>
		/// Entire space available to simulation.
		/// If an entity leaves this space, then it may be lost
		/// </summary>
		public readonly Box World;

		/// <summary>
		/// Constructs a new entity range configuration
		/// </summary>
		/// <param name="r">Maximum entity influence. Most be positive, non-zero</param>
		/// <param name="m">Maximum entity motion radius. 
		/// If found outside (0,r), displaced transmission is concluded, and Motion and Transmission are set to R.
		/// Otherwise max transmission range is set to r-m</param>
		/// <param name="world">World scope</param>
		public EntityRanges(float r, float m, Box world)
		{
			DisplacedTransmission = m > 0 && m < r;
			if (DisplacedTransmission)
			{
				Motion = m;
				Transmission = r - m;
			}
			else
				Motion = Transmission = r;
			R = r;
			World = world;
		}

	}

}
