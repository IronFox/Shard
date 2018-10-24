using System;
using System.Diagnostics;

namespace Base
{
	public struct PreciseTime : IEquatable<PreciseTime>, IComparable<PreciseTime>
	{
		public readonly long Ticks;
		internal PreciseTime(long ticks) => Ticks = ticks;


		public static PreciseTime Now => new PreciseTime(Stopwatch.GetTimestamp());

		public double Seconds => (double)Ticks / Stopwatch.Frequency;

		public static readonly PreciseTime None = new PreciseTime(0);

		public static PreciseTimeSpan operator-(PreciseTime a, PreciseTime b) =>new PreciseTimeSpan(a.Ticks - b.Ticks);
		public static PreciseTime operator +(PreciseTime a, PreciseTimeSpan b) => new PreciseTime(a.Ticks + b.Ticks);

		public static bool operator >(PreciseTime a, PreciseTime b) => a.Ticks > b.Ticks;
		public static bool operator <(PreciseTime a, PreciseTime b) => a.Ticks < b.Ticks;
		public static bool operator >=(PreciseTime a, PreciseTime b) => a.Ticks >= b.Ticks;
		public static bool operator <=(PreciseTime a, PreciseTime b) => a.Ticks <= b.Ticks;
		public static bool operator ==(PreciseTime a, PreciseTime b) => a.Ticks == b.Ticks;
		public static bool operator !=(PreciseTime a, PreciseTime b) => a.Ticks != b.Ticks;

		public override bool Equals(object obj)
		{
			return obj is PreciseTime && Equals((PreciseTime)obj);
		}

		public bool Equals(PreciseTime other)
		{
			return Ticks == other.Ticks;
		}

		public override int GetHashCode()
		{
			return 1099527005 + Ticks.GetHashCode();
		}

		public override string ToString()
		{
			return "t=" + (double)Ticks / Stopwatch.Frequency + " sec";
		}

		public int CompareTo(PreciseTime other)
		{
			return Ticks.CompareTo(other.Ticks);
		}
	}
}