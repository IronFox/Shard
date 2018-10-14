using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	public class SerialRCS : BaseDB.Entity, IEquatable<SerialRCS>
	{
		public int[] NumericID;
		public RCS.SerialData Data;

		public SerialRCS()
		{ }
		public SerialRCS(RCS.GenID id, RCS data)
		{
			Data = data.Export();
			NumericID = id.IntArray;
			_id = id.ToString();
		}

		[JsonIgnore]
		public RCS.GenID ID => new RCS.GenID(NumericID, 0);
		



		public RCS Deserialize()
		{
			return new RCS(Data);
		}

		public override bool Equals(object obj)
		{
			return Equals(obj as SerialRCS);
		}

		public bool Equals(SerialRCS other)
		{
			return other != null &&
				   EqualityComparer<RCS.SerialData>.Default.Equals(Data, other.Data) &&
				   EqualityComparer<RCS.GenID>.Default.Equals(ID, other.ID);
		}

		public override int GetHashCode()
		{
			var hashCode = 1156961517;
			hashCode = hashCode * -1521134295 + EqualityComparer<RCS.SerialData>.Default.GetHashCode(Data);
			hashCode = hashCode * -1521134295 + EqualityComparer<RCS.GenID>.Default.GetHashCode(ID);
			return hashCode;
		}
	}


}
