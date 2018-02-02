using VectorMath;
using Shard;
using System;

[Serializable]
public class MovingLogic : EntityLogic
{
	protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom random, EntityRanges ranges)
	{
		newState.NewPosition = currentState.ID.Position + random.NextVec3(-ranges.M,ranges.M);
	}
}
