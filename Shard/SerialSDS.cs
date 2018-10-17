using Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	public class SerialSDS : SerialGenerationObject
	{
		public SerialSDS()
		{ }

		public SerialSDS(SDS sds, Int3 sectorID)
		{
			SerialEntities = Entity.Export(sds.FinalEntities);
			Generation = sds.Generation;
			IC = sds.IC.Export();
			_id = sectorID.Encoded;
		}

		public byte[] SerialEntities { get; set; }
		public InconsistencyCoverage.DBSerial IC { get; set; }

		public override bool Equals(object obj)
		{
			var other = obj as SerialSDS;
			if (other == null)
				return false;
			return Helper.AreEqual(SerialEntities, other.SerialEntities)
					&& IC.Equals(other.IC)
					;
		}

		public override int GetHashCode()
		{
			return Helper.Hash(this)
				.Add(SerialEntities)
				.Add(IC)
				.GetHashCode();
		}

		public override string ToString()
		{
			return "Serial SDS [" + Helper.Length(SerialEntities) + " byte(s)] IC=" + IC;
		}

		public SDS Deserialize()
		{
			return new SDS(Generation, Entity.Import(SerialEntities), new InconsistencyCoverage(IC));
		}
	}

}
