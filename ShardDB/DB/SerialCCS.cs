using Base;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	public class SerialCCS : DBType.Entity
	{
		public int[] NumericID;
		public byte[] Data;

		[JsonIgnore]
		public int Generation => NumericID[3];

		public SerialCCS()
		{ }
		public SerialCCS(SDS.ID id, MessagePack data)
		{
			Data = Helper.SerializeToArray(data);
			NumericID = id.IntArray;
			_id = id.ShardID.Encoded;
		}

		[JsonIgnore]
		public SDS.ID ID => new SDS.ID(NumericID, 0);

		public MessagePack Deserialize()
		{
			return (MessagePack)Helper.Deserialize(Data);
		}

		public override bool Equals(object obj)
		{
			var cCS = obj as SerialCCS;
			return Equals(cCS);

		}

		public bool Equals(SerialCCS cCS)
		{
			return cCS != null &&
				   EqualityComparer<int[]>.Default.Equals(NumericID, cCS.NumericID) &&
				   EqualityComparer<byte[]>.Default.Equals(Data, cCS.Data);
		}

		public override int GetHashCode()
		{
			var hashCode = -1262551998;
			hashCode = hashCode * -1521134295 + EqualityComparer<int[]>.Default.GetHashCode(NumericID);
			hashCode = hashCode * -1521134295 + EqualityComparer<byte[]>.Default.GetHashCode(Data);
			return hashCode;
		}
	}
}
