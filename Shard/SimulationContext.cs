using System;
using System.Diagnostics;
using Base;
using Shard.EntityChange;
using VectorMath;

namespace Shard
{
	public class SimulationContext : ExecutionContext
	{
		public readonly bool AllowMotionToUnresponsiveNeighbor;

		public SimulationContext(bool allowMotionToUnresponsiveNeighbor) :base(Simulation.Ranges,Simulation.MySpace)
		{
			AllowMotionToUnresponsiveNeighbor = allowMotionToUnresponsiveNeighbor;
		}
		public SimulationContext(BaseDB.ConfigContainer cfg, Box mySpace, bool allowMotionToUnresponsiveNeighbor) : base(Simulation.ToRanges(cfg), mySpace)
		{
			AllowMotionToUnresponsiveNeighbor = allowMotionToUnresponsiveNeighbor;
		}

		public override void LogError(string message)
		{
			Log.Error(message);
		}

		public override void LogMessage(string message)
		{
			Log.Message(message);
		}

		public override void RelayClientMessage(Guid entityID, Guid clientID, int channel, byte[] data)
		{
			if (GenerationNumber == Simulation.Stack.NewestRegisteredSDSGeneration)
				InteractionLink.Relay(entityID, clientID, channel, data, GenerationNumber);
		}

		public void SetGeneration(int gen)
		{
			GenerationNumber = gen;
		}

		public override Vec3 ClampDestination(string task, Vec3 newPosition, EntityID currentEntityPosition, float maxDistance)
		{
			newPosition = Ranges.World.Clamp(newPosition);  //make sure we do not leave the simulation space

			
			if (!Simulation.Owns(newPosition))
			{
				Int3 targetShard = Int3.Min(newPosition.FloorInt3,Simulation.Extent.XYZ-1);
				bool any = false,anyResponive=false;
				foreach (var n in Simulation.Neighbors)
					if (n.ID.XYZ == targetShard)
					{
						any = true;
						if (n.IsResponsive)
							anyResponive = true;
						break;
					}
				if (!any)
				{
					Debug.Fail("This should not happen");
					throw new ExecutionException(currentEntityPosition, task + " targets space beyond known neighbors. Rejecting motion");
				}
				if (!anyResponive && !AllowMotionToUnresponsiveNeighbor)
					throw new ExecutionException(currentEntityPosition, task + " targets space of inactive neighbor shard. Rejecting");
			}


			return base.ClampDestination(task, newPosition, currentEntityPosition, maxDistance);
		}
	}
}