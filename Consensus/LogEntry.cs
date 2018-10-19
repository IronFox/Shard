using System;

namespace Consensus
{

	[Serializable]
	public struct CommitID : IEquatable<CommitID>
	{
		public static readonly CommitID None = new CommitID(-1,-1);
		public readonly int OriginNode;
		public readonly int OriginSerialNumber;
		public CommitID(int origin, int serial)
		{
			OriginNode = origin;
			OriginSerialNumber = serial;
		}

		public override bool Equals(object obj)
		{
			return obj is CommitID && Equals((CommitID)obj);
		}

		public bool Equals(CommitID other)
		{
			return OriginNode == other.OriginNode &&
				   OriginSerialNumber == other.OriginSerialNumber;
		}

		public override int GetHashCode()
		{
			var hashCode = -1367048135;
			hashCode = hashCode * -1521134295 + OriginNode.GetHashCode();
			hashCode = hashCode * -1521134295 + OriginSerialNumber.GetHashCode();
			return hashCode;
		}

		public static bool operator ==(CommitID a, CommitID b)
		{
			return a.OriginNode == b.OriginNode && a.OriginSerialNumber == b.OriginSerialNumber;
		}

		public static bool operator !=(CommitID a, CommitID b)
		{
			return !(a == b);
		}


	}

	[Serializable]
	public class LogEntry
	{
		public readonly ICommitable Operation;
		public readonly CommitID CommitID;
		public readonly int Term;

		public LogEntry(CommitID id, int term, ICommitable op)
		{
			CommitID = id;
			Term = term;
			Operation = op;
		}

		internal void Execute(Node location)
		{
			Operation.Commit(location);
		}

		public override string ToString()
		{
			return Operation + "@t=" + Term;
		}
	}
}