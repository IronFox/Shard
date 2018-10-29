using System;

namespace DBType
{
	public interface IPollable<T> where T:Entity
	{
		T	Latest { get; }

		bool Suspend();
		void Resume();
	}


	public class FunctionPollable<T> : IPollable<T> where T:Entity
	{
		public readonly Func<T> PollFunction;

		public FunctionPollable(Func<T> f)
		{
			PollFunction = f;
		}

		public T Latest => PollFunction();

		public void Resume()
		{}

		public bool Suspend() => false;
	}
}