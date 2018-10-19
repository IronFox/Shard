using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Base
{
	public class DebugMutex
	{
		private System.Threading.Mutex mtx = new System.Threading.Mutex();
		private System.Threading.Thread lockingThread = null;
		private int recursion = 0;

		public readonly string Name;

		public bool IsLockedByMe => lockingThread == System.Threading.Thread.CurrentThread;

		public DebugMutex(string name)
		{
			Name = name;
		}
		public override string ToString()
		{
			return Name + "(mutex)";
		}
		public static string NameOf(System.Threading.Thread thread)
		{
			if (thread == null)
				return "<null>";
			return "Thread #"+thread.ManagedThreadId.ToString();
		}
		public void Lock()
		{
			int msTimeout = 5000;
			if (!mtx.WaitOne(msTimeout))
				throw new DeadlockException("Thread "+NameOf(System.Threading.Thread.CurrentThread)+" failed to acquire "+this+" after "+ msTimeout+"ms. Currently helt by "+NameOf(lockingThread));
			lockingThread = System.Threading.Thread.CurrentThread;
			recursion++;
		}

		public void Release()
		{
			recursion--;
			if (lockingThread != System.Threading.Thread.CurrentThread)
				throw new IntegrityViolation("Wrong thread unlocking "+this+". Expected "+NameOf(lockingThread)+" but got "+NameOf(System.Threading.Thread.CurrentThread));
			if (recursion == 0)
				lockingThread = null;
			mtx.ReleaseMutex();
		}


		public void DoLocked(Action action)
		{
			Lock();
			try
			{
				action();
			}
			finally
			{
				Release();
			}
		}

		public void AssertIsLockedByMe()
		{
			if (!IsLockedByMe)
				throw new IntegrityViolation("Local thread "+this+" is not locked by calling thread "+NameOf(System.Threading.Thread.CurrentThread));
		}
	}
}
