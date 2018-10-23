using System;

namespace DBType
{
	public interface IPollable<T> where T:Entity
	{
		T	Latest { get; }
	}


	public class FunctionPollable<T> : IPollable<T> where T:Entity
	{
		public readonly Func<T> PollFunction;

		public FunctionPollable(Func<T> f)
		{
			PollFunction = f;
		}

		public T Latest => PollFunction();
	}
}