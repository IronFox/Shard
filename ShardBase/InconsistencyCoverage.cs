using Base;
using System;
using VectorMath;

namespace Shard
{
	[Serializable]
	public class InconsistencyCoverage : BitCube
	{
		public static int CommonResolution { get; set; } = 8;
		public bool IsFullyConsistent
		{
			get
			{
				return base.OneCount == 0;
			}
		}

		public bool IsCompletelyInconsistent => base.OneCount == base.Size.Product;

		private InconsistencyCoverage(BitCube raw) : base(raw)
		{ }

		public InconsistencyCoverage(Int3 size) : base(size)
		{}

		public InconsistencyCoverage(DBSerial serial) : base(serial)
		{}

		

		public InconsistencyCoverage Grow(bool trimToLocalSize)
		{
			BitCube cube = base.GrowOnes();
			if (trimToLocalSize)
			{
				cube = cube.SubCube(new Int3(1), base.Size);
				if (cube.Size != base.Size)
					throw new IntegrityViolation("Unexpted grown size: "+cube.Size+". Expected "+base.Size);
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
		public static InconsistencyCoverage NewAllOne()
		{
			var rs = NewCommon();
			rs.SetAllOne();
			return rs;
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

		public static Vec3 ToRelative(Int3 pixelCoords)
		{
			if ((pixelCoords < Int3.Zero).Any)
				throw new ArgumentOutOfRangeException(pixelCoords + " partially less than zero");
			if ((pixelCoords >= CommonResolution).Any)
				throw new ArgumentOutOfRangeException(pixelCoords + " partially greater or equal to max resolution");
			return new Vec3(pixelCoords) / (CommonResolution - 1);
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

		public InconsistencyCoverage Sub(IntBox box)
		{
			return Sub(box.Min, box.Size);
		}

		public void SetOne(IntBox box)
		{
			SetOne(box.Min, box.Size);
		}

		public int GetInconsistencyAtR(Vec3 pos)
		{
			return IsInconsistentR(pos) ? 1 : 0;
		}

		public static InconsistencyCoverage GetMinimum(InconsistencyCoverage ic0, InconsistencyCoverage ic1)
		{
			InconsistencyCoverage rs = new InconsistencyCoverage(Int3.Zero);
			rs.LoadMinimum(ic0, ic1);
			return rs;
		}

		public bool FindInconsistentPlacementCandidateR(ref Vec3 relativeCoords, float maxDist)
		{
			var c = ToPixelR(relativeCoords);

			int maxDist2 = (int)(maxDist * CommonResolution);
			int d2 = maxDist2;
			Int3 closest = Int3.Zero;
			bool any = false;
			
			for (int z = 0; z < Size.Z; z++)
				for (int y = 0; y < Size.Y; y++)
					for (int x = 0; x < Size.X; x++)
					{
						if (!this[x, y, z])
							continue;
						var candidate = new Int3(x, y, z);
						int dist = Int3.GetChebyshevDistance(c, candidate);
						if (dist >= d2)
							continue;
						d2 = dist;
						any = true;
						closest = candidate;
					}

			if (any)
				relativeCoords = ToRelative(closest);
			return any;
		}
	}
}