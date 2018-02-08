#if STATE_ADV

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	[Serializable]
	public class GeometricAppearance : EntityAppearance
	{
		public string geometryName;
		public VectorMath.Matrix3 orientation = VectorMath.Matrix3.Identity;


		public override int CompareTo(EntityAppearance other)
		{
			GeometricAppearance o = other as GeometricAppearance;
			if (o == null)
				return 1;
			return new Helper.Comparator()
				.Append(string.Compare(geometryName, o.geometryName))
				.Append(orientation,o.orientation)
				.Finish()
				;
		}

		public override int GetHashCode()
		{
			return new Helper.HashCombiner(GetType())
				.Add(orientation)
				.GetHashCode()
				;
		}
		//public string 

	}
}

#endif
