using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	public class SerialSDS : SerialGenerationObject
	{
		public SerialSDS()
		{ }

		public SerialSDS(SDS sds)
		{
			SerialEntities = Entity.Export(sds.FinalEntities);
			Generation = sds.Generation;
			IC = sds.IC.Export();
			SerialMessages = sds.ClientMessages != null ? Helper.SerializeToArray(sds.ClientMessages) : null;
			_id = Simulation.ID.XYZ.Encoded;
		}

		public byte[] SerialEntities { get; set; }
		public InconsistencyCoverage.DBSerial IC { get; set; }
		public byte[] SerialMessages { get; set; }

		public override bool Equals(object obj)
		{
			var other = obj as SerialSDS;
			if (other == null)
				return false;
			return Helper.AreEqual(SerialEntities, other.SerialEntities)
					&& IC.Equals(other.IC)
					&& Helper.AreEqual(SerialMessages, other.SerialMessages)
					;
		}

		public override int GetHashCode()
		{
			return Helper.Hash(this).Add(SerialEntities).Add(IC).Add(SerialMessages).GetHashCode();
		}

		public override string ToString()
		{
			return "Serial SDS [" + Helper.Length(SerialEntities) + " byte(s)] IC=" + IC;
		}

		public SDS Deserialize()
		{
			return new SDS(Generation, Entity.Import(SerialEntities), new InconsistencyCoverage(IC),SerialMessages != null ? (Dictionary<Guid, EntityMessage[]>)Helper.Deserialize(SerialMessages) : null);
		}
	}

}
