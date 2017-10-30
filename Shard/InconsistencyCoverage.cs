using System;
using VectorMath;

namespace Shard
{
	public class InconsistencyCoverage
	{
		private BitCube data;


		public InconsistencyCoverage(Int3 size)
		{
			data = new BitCube(size);
		}


		public InconsistencyCoverage(Serial serial)
		{
			
		}

		public bool IsFullyConsistent { get { return Inconsistency == 0; } }
		public int Inconsistency { get; private set; }

		public class Serial
		{
			public byte[] Data { get; set; }

			public Serial(BitCube cube)
			{
				Data = cube.ToByteArray();
			}

			public BitCube Export()
			{
				return new BitCube(Data);
			}

		}

		public Serial Export()
		{
			throw new NotImplementedException();
		}

		public void VerifyIntegrity()
		{
			//nothing for now
		}
	}
}