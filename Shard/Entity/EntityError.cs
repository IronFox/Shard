using System;

namespace Shard
{
	public class EntityError
	{
		private Entity e;
		private Exception ex;
		private EntityLogic logic;

		public EntityError(Entity e, EntityLogic logic, Exception ex)
		{
			this.e = e;
			this.ex = ex;
			this.logic = logic;
		}

		public override string ToString()
		{
			return e.ID + "<"+logic+">: " + ex;
		}
	}
}