using VectorMath;
using Shard;
using System;



[Serializable]
public class Bug
{
	public readonly float Size;
}




[Serializable]
public class CellLogic : EntityLogic
{
	readonly Bug	bug;
	readonly Predator predator;
	
	enum Message
	{
		BugMoveRequest,
		BugMoveResponse,
		BugMoveAccept,
		
		PreMoveRequest,
		PreMoveResponse,
		PreMoveAccept,
	}
	
	
	public override void Evolve(ref NewState newState, Entity currentState, int generation, EntityRandom random)
	{
		if (generation % 3 == 0)	//move bugs
		{
			newState.Broadcast(
			
		}
		
		
		
		
		
	}
}
