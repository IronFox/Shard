using System;
using System.Runtime.Serialization;

namespace VectorMath
{
	[Serializable]
	internal class InversionFailedException : Exception
	{
		private Matrix4 matrix4;

		public InversionFailedException()
		{
		}

		public InversionFailedException(Matrix4 matrix4)
		{
			this.matrix4 = matrix4;
		}

		public InversionFailedException(string message) : base(message)
		{
		}

		public InversionFailedException(string message, Exception innerException) : base(message, innerException)
		{
		}

		protected InversionFailedException(SerializationInfo info, StreamingContext context) : base(info, context)
		{
		}
	}
}