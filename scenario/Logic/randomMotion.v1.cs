using VectorMath;
using Shard;
using System;

[Serializable]
public class MovingLogic : EntityLogic
{
	public override void Evolve(ref NewState newState, Entity currentState, int generation, EntityRandom random)
	{
		newState.newPosition = currentState.ID.Position + random.NextVec3(-Simulation.M,Simulation.M);
	}
}
