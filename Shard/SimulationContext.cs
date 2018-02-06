using System;
using Shard.EntityChange;

namespace Shard
{
	public class SimulationContext : ExecutionContext
	{
		public SimulationContext():base(new EntityRanges(Simulation.R,Simulation.M,Simulation.SensorRange,Simulation.FullSimulationSpace),Simulation.MySpace)
		{}
		public SimulationContext(DB.ConfigContainer cfg) : base(new EntityRanges(cfg.r,cfg.m,cfg.r-cfg.m, Simulation.ExtToWorld(cfg.extent.XYZ)), Simulation.MySpace)
		{ }

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

	}
}