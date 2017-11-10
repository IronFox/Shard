using System;
using VectorMath;

namespace Shard
{
	public class InconsistencyCoverage : BitCube
	{
		public static int CommonResolution { get; set; }


		private InconsistencyCoverage(BitCube raw) : base(raw)
		{ }

		public InconsistencyCoverage(Int3 size) : base(size)
		{}

		public InconsistencyCoverage(Serial serial) : base(serial.Data)
		{}


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
			return new Serial(this);
		}

		public InconsistencyCoverage Grow(bool trimToLocalSize)
		{
			BitCube cube = base.GrowOnes();
			if (trimToLocalSize)
			{
				cube = cube.SubCube(new Int3(1), base.Size);
				if (cube.Size != base.Size)
					throw new Exception("Unexpted grown size: "+cube.Size+". Expected "+base.Size);
			}
			return new InconsistencyCoverage(cube);
		}

		public InconsistencyCoverage TrimToCommonResolution()
		{
			if ((Size <= CommonResolution).All)
				return this;
			return new InconsistencyCoverage(this.SubCube(Int3.Zero, new Int3(CommonResolution)));
		}

		public InconsistencyCoverage Sub(Int3 offset, Int3 size)
		{
			return new InconsistencyCoverage(SubCube(offset,size));
		}



	}
}