using System;
using System.Diagnostics;

namespace Base
{
	public struct PreciseTimeSpan : IEquatable<PreciseTimeSpan>, IComparable<PreciseTimeSpan>
	{
		public readonly long Ticks;

		public TimeSpan TimeSpan => TimeSpan.FromSeconds(Seconds);

		internal PreciseTimeSpan(long ticks) => Ticks = ticks;
		public double Seconds => (double)Ticks / Stopwatch.Frequency;


		public static bool operator >(PreciseTimeSpan a, PreciseTimeSpan b) => a.Ticks > b.Ticks;
		public static bool operator <(PreciseTimeSpan a, PreciseTimeSpan b) => a.Ticks < b.Ticks;
		public static bool operator >=(PreciseTimeSpan a, PreciseTimeSpan b) => a.Ticks >= b.Ticks;
		public static bool operator <=(PreciseTimeSpan a, PreciseTimeSpan b) => a.Ticks <= b.Ticks;
		public static bool operator ==(PreciseTimeSpan a, PreciseTimeSpan b) => a.Ticks == b.Ticks;
		public static bool operator !=(PreciseTimeSpan a, PreciseTimeSpan b) => a.Ticks != b.Ticks;



		public override string ToString()
		{
			return (double)Ticks / Stopwatch.Frequency + " sec";
		}




		public override int GetHashCode()
		{
			return 1099527005 + Ticks.GetHashCode();
		}

		public int CompareTo(PreciseTimeSpan other)
		{
			return Ticks.CompareTo(other.Ticks);
		}

		public static PreciseTimeSpan FromMilliseconds(double ms)
		{
			return FromSeconds(ms / 1000);
		}

		public static PreciseTimeSpan FromSeconds(double s)
		{
			return new PreciseTimeSpan((long)(s * Stopwatch.Frequency));
		}

		public override bool Equals(object obj)
		{
			if (!(obj is PreciseTimeSpan))
			{
				return false;
			}

			var span = (PreciseTimeSpan)obj;
			return Ticks == span.Ticks;
		}

		public bool Equals(PreciseTimeSpan other)
		{
			return this == other;
		}
	}
}