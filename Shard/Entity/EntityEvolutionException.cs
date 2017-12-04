using System;

namespace Shard
{
	public class EntityEvolutionException : Exception
	{
		public readonly Entity FaultedEntity;

		public EntityEvolutionException(Entity e, Exception innerException) : base("", innerException)
		{
			FaultedEntity = e;
		}

		public override string Message
		{
			get
			{
				var ex = InnerException;
				if (ex.InnerException != null)
					ex = ex.InnerException;
				return FaultedEntity +": " + ex.Message;
			}
		}
	}
}