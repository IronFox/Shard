using VectorMath;
using System;
using Newtonsoft.Json;

namespace Shard
{
	[Serializable]
	public struct ShardID
	{
		[JsonIgnore]
		public Int3 XYZ;
		public int ReplicaLevel;

		public int X { get { return XYZ.X; } set { XYZ.X = value; } }
		public int Y { get { return XYZ.Y; } set { XYZ.Y = value; } }
		public int Z { get { return XYZ.Z; } set { XYZ.Z = value; } }

		public static readonly ShardID Zero = new ShardID(0, 0, 0, 0);
		public static readonly ShardID One = new ShardID(1, 1, 1, 1);

		public ShardID(int x, int y, int z, int r)
		{
			XYZ = new Int3(x, y, z);
			ReplicaLevel = r;
		}
		public ShardID(Int3 xyz, int r)
		{
			XYZ = xyz;
			ReplicaLevel = r;
		}

		private bool Eq(ShardID other)
		{
			return XYZ == other.XYZ && ReplicaLevel == other.ReplicaLevel;
		}

		public static bool operator ==(ShardID u, ShardID v)
		{
			return u.Eq(v);
		}
		public static bool operator !=(ShardID u, ShardID v)
		{
			return !u.Eq(v);
		}

		public override bool Equals(object obj)
		{
			return obj is ShardID && Eq((ShardID)obj);
		}

		public static Bool4 operator >(ShardID a, ShardID b)
		{
			return new Bool4(a.XYZ > b.XYZ, a.ReplicaLevel > b.ReplicaLevel);
		}
		public static Bool4 operator <(ShardID a, ShardID b)
		{
			return new Bool4(a.XYZ < b.XYZ, a.ReplicaLevel < b.ReplicaLevel);
		}
		public static Bool4 operator >=(ShardID a, ShardID b)
		{
			return new Bool4(a.XYZ >= b.XYZ, a.ReplicaLevel >= b.ReplicaLevel);
		}
		public static Bool4 operator <=(ShardID a, ShardID b)
		{
			return new Bool4(a.XYZ <= b.XYZ, a.ReplicaLevel <= b.ReplicaLevel);
		}


		public override int GetHashCode()
		{
			int hash = 17;
			hash = hash * 31 + XYZ.GetHashCode();
			hash = hash * 31 + ReplicaLevel.GetHashCode();
			return hash;
		}

		public static ShardID Decode(string str)
		{
			string[] parts = str.Split('r');
			if (parts.Length != 2)
				throw new FormatException("Expected two parts in vector expression '" + str + '\'');

			return new ShardID(
					Int3.Decode(parts[0]),
					int.Parse(parts[1])
				);
		}

		public string Encode()
		{
			return Convert.ToString(X) + '-' + Convert.ToString(Y) + '-' + Convert.ToString(Z)+'r'+Convert.ToString(ReplicaLevel);
		}


		public override string ToString()
		{
			return XYZ+"_R" + ReplicaLevel;
		}
	}
}