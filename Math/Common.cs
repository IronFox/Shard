using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VectorMath
{
	internal static class Common
	{
		public static string ToString(float f)
		{
			return f.ToString("0.00", CultureInfo.InvariantCulture);
		}
		public static string ToString(int i)
		{
			return i.ToString();
		}


	}
}
