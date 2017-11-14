using System;
using VectorMath;

namespace Shard
{
	public class InconsistencyCoverage : BitCube
	{
		public static int CommonResolution { get; set; } = 8;


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

		public void FlagInconsistentR(Vec3 relative)
		{
			this[ToPixelR(relative)] = true;
		}
		public void FlagInconsistentR(Vec3 relative, Int3 offset)
		{
			this[ToPixelR(relative) + offset] = true;
		}

		public static InconsistencyCoverage NewCommon()
		{
			return new InconsistencyCoverage(new Int3(CommonResolution));
		}
		public InconsistencyCoverage Clone()
		{
			var rs = new InconsistencyCoverage(Int3.Zero);
			rs.LoadCopy(this);
			return rs;
		}


		public static Int3 ToPixelR(Vec3 relative)
		{
			if ((relative < Vec3.Zero).Any)
				throw new ArgumentOutOfRangeException(relative + " partially less than zero");
			if ((relative > Vec3.One).Any)
				throw new ArgumentOutOfRangeException(relative + " partially greater or equal to one");
			return Int3.Min((relative * CommonResolution).FloorInt3, CommonResolution - 1);
		}

		/// <summary>
		/// Checks whether or not a location in [0,1] is consistent according to the local IC
		/// </summary>
		/// <param name="relative"></param>
		/// <returns></returns>
		public bool IsInconsistentR(Vec3 relative)
		{
			return this[ToPixelR(relative)];
		}
	}
}