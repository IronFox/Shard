using System;
using System.Runtime.Serialization;

namespace Base
{
	[Serializable]
	internal class DeadlockException : Exception
	{
		public DeadlockException()
		{
		}

		public DeadlockException(string message) : base(message)
		{
		}

		public DeadlockException(string message, Exception innerException) : base(message, innerException)
		{
		}

		protected DeadlockException(SerializationInfo info, StreamingContext context) : base(info, context)
		{
		}
	}
}