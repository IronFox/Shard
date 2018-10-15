using Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	public struct TimingInfo
	{
		public readonly int StepsPerGeneration, StartGeneration, MaxGeneration;
		public readonly TimeSpan
							GenerationTimeWindow,
							StepTimeWindow,
							MessageProcessingTimeWindow,
							StepComputationTimeWindow;
		public readonly DateTime
							Start;

		public TimingInfo(BaseDB.TimingContainer t)
		{
			StepsPerGeneration = 1 + t.recoverySteps;
			GenerationTimeWindow = TimeSpan.FromMilliseconds(t.msStep * StepsPerGeneration);
			StepTimeWindow = TimeSpan.FromMilliseconds(t.msStep);
			StepComputationTimeWindow = TimeSpan.FromMilliseconds(t.msComputation);
			MessageProcessingTimeWindow = TimeSpan.FromMilliseconds(t.msMessageProcessing);

			Start = Convert.ToDateTime(t.startTime);
			StartGeneration = t.startGeneration;
			MaxGeneration = t.maxGeneration;
		}

		public static TimingInfo Current
		{
			get
			{
				return new TimingInfo(BaseDB.Timing);
			}
		}

		public int TopLevelGeneration
		{
			get
			{
				var rs = StartGeneration + Math.Max(0, (int)(SimulationTime.TotalSeconds / GenerationTimeWindow.TotalSeconds));
				if (MaxGeneration >= 0)
					rs = Math.Min(rs, MaxGeneration);
				return rs;
			}
		}

		public TimeSpan SimulationTime
		{
			get
			{
				return Clock.Now - Start;
			}
		}

		public DateTime LatestGenerationStart
		{
			get
			{
				return Start + TimeSpan.FromTicks(GenerationTimeWindow.Ticks * TopLevelGeneration);
			}
		}

		public TimeSpan LatestGenerationElapsed
		{
			get
			{
				return Clock.Now - LatestGenerationStart;
			}
		}

		public TimeSpan TimeToGenerationDeadline
		{
			get
			{
				var remainingRelative = 1.0 - Helper.Frac(SimulationTime.TotalSeconds / GenerationTimeWindow.TotalSeconds);
				return TimeSpan.FromTicks((long)(remainingRelative * GenerationTimeWindow.Ticks));
			}
		}

		public DateTime NextGenerationDeadline
		{
			get
			{
				
				return Clock.Now + TimeToGenerationDeadline;
			}
		}
		public DateTime NextStepDeadline
		{
			get
			{
				var remainingRelative = 1.0 - Helper.Frac(SimulationTime.TotalSeconds / StepTimeWindow.TotalSeconds);
				return Clock.Now + TimeSpan.FromTicks((long)(remainingRelative * StepTimeWindow.Ticks));
			}
		}

		/// <summary>
		/// Determines the generation step.
		/// 0 indicates the main processing step, >0 a recovery step.
		/// Note that this index can exceed the configured number of recovery steps
		/// if the simulation has reached the maximum generation
		/// </summary>
		public int LatestStepIndex
		{
			get
			{
				return LatestGenerationElapsed.FloorDiv(StepTimeWindow);
			}
		}

	}

}
