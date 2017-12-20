using System;
using System.Runtime.Serialization;

namespace Shard
{
	[Serializable]
	internal class TimeBudgetException : Exception
	{
		private TimeSpan budget;
		private Entity.TimeTrace t;

		public TimeBudgetException()
		{
		}

		public TimeBudgetException(string message) : base(message)
		{
		}

		public TimeBudgetException(string message, Exception innerException) : base(message, innerException)
		{
		}

		public TimeBudgetException(TimeSpan budget, Entity.TimeTrace trace)
		{
			this.budget = budget;
			this.t = trace;
		}

		protected TimeBudgetException(SerializationInfo info, StreamingContext context) : base(info, context)
		{
		}


		public override string Message
		{
			get
			{
				return "Failed to execute in " + budget.TotalMilliseconds + " ms (" + t + ")";
			}
		}
	}
}