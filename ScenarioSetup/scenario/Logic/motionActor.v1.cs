using VectorMath;
using Shard;
using System;

[Serializable]
public class ActorLogic : EntityLogic
{
	protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom random, EntityRanges ranges, bool isInconsistent)
	{
		newState.Broadcast(0,null);
	}
}
