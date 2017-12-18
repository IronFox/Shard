using System;

namespace Shard
{
	public class EntityEvolutionException : Exception
	{
		public readonly Entity FaultedEntity;
		public readonly Entity.TimeTrace TimeTable;

		public EntityEvolutionException(Entity e, Exception innerException, Entity.TimeTrace timeTable) : base("", innerException)
		{
			FaultedEntity = e;
			TimeTable = timeTable;
		}

		public override string ToString()
		{
			return Message;
		}

		public override string Message
		{
			get
			{
				var ex = InnerException;
				if (ex.InnerException != null)
					ex = ex.InnerException;
				return /*FaultedEntity +": " +*/ ex.Message+" ("+TimeTable+")";
			}
		}
	}
}