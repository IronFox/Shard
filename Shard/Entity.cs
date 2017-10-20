using System;

namespace Shard
{
	public struct Entity
	{
		public struct Serial
		{
			public EntityAppearance Appearance;
			public byte[] Guid;
			public string LogicID;
			public byte[] LogicState;
			

			public Serial(Entity entity) : this()
			{
				Appearance = entity.Appearance;
				Guid = entity.Guid.ToByteArray();
				LogicID = entity.LogicID;
				LogicState = entity.LogicState.BinaryState;
			}

			internal void BeginFetchLogic()
			{
				if (LogicID != null && LogicID.Length > 0)
					DB.BeginFetchLogic(LogicID);
			}
		}




		public readonly EntityAppearance Appearance;
		public readonly Guid Guid;
		public readonly EntityLogic.State LogicState;
		public readonly bool IsInconsistent;
		public readonly string LogicID;

		public Entity(Serial entity)
		{
			LogicID = entity.LogicID;
			IsInconsistent = false;
			Appearance = entity.Appearance;
			Guid = new Guid(entity.Guid);
			if (entity.LogicID == null || entity.LogicID.Length == 0)
				LogicState = null;
			else
			{
				EntityLogic logic = DB.TryGetLogic(entity.LogicID);
				if (logic != null)
					LogicState = logic.Instantiate(entity.LogicState);
				else
				{
					LogicState = null;
					IsInconsistent = true;
				}
			}
		}

		public static Entity[] Import(Serial[] entities)
		{
			Entity[] rs = new Entity[entities.Length];
			for (int i = 0; i < entities.Length; i++)
				rs[i] = new Entity(entities[i]);
			return rs;
		}
		public static Serial[] Export(Entity[] entities)
		{
			Serial[] rs = new Serial[entities.Length];
			for (int i = 0; i < entities.Length; i++)
				rs[i] = new Serial(entities[i]);
			return rs;
		}
	}

}
