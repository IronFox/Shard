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

		private InconsistencyCoverage(BitCube cube)
		{
			data = cube;
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

		internal void AddTo(Hasher inputHash)
		{
			data.AddTo(inputHash);
		}

		public InconsistencyCoverage Grow(bool trimToLocalSize)
		{
			BitCube cube = data.GrowOnes();
			if (trimToLocalSize)
			{
				cube = cube.SubCube(new Int3(1), data.Size);
				if (cube.Size != data.Size)
					throw new Exception("Unexpted grown size: "+cube.Size+". Expected "+data.Size);
			}
			return new InconsistencyCoverage(cube);
		}
	}
}