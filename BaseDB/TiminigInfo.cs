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
		/// <summary>
		/// Total steps (main+N*recovery) per generation
		/// The first step is the main progression which evaluates before all recovery steps but applies after
		/// </summary>
		public readonly int RecoveryStepsPerGeneration;
		/// <summary>
		/// First simulation generation. Typically 0
		/// </summary>
		public readonly int StartGeneration;
		/// <summary>
		/// Last generation allowing the simulation to halt. Due to latency not guaranteed to be applied immediately. Usually -1 (no limit)
		/// </summary>
		public readonly int MaxGeneration;

		/// <summary>
		/// Time budge per generation, including recovery
		/// </summary>
		public readonly TimeSpan GenerationTimeWindow;
		/// <summary>
		/// Time budget per recovery step
		/// </summary>
		public readonly TimeSpan RecoveryStepTimeWindow;
		/// <summary>
		/// Time budget for change set application
		/// </summary>
		public readonly TimeSpan CSApplicationTimeWindow;
		/// <summary>
		/// Time budget for entity evaluation
		/// </summary>
		public readonly TimeSpan EntityEvaluationTimeWindow;
		/// <summary>
		/// Simulation start time. When starting the simulation, this value should be set sufficiently into the future such that its value can propagate before it is reached
		/// </summary>
		public readonly DateTime Start;


		public TimingInfo(BaseDB.TimingContainer t)
		{
			RecoveryStepsPerGeneration = t.recoverySteps;
			GenerationTimeWindow = TimeSpan.FromMilliseconds(t.msGenerationBudget);
			RecoveryStepTimeWindow = TimeSpan.FromMilliseconds(t.msGenerationBudget / (1 + t.recoverySteps));
			EntityEvaluationTimeWindow = TimeSpan.FromMilliseconds(t.msComputation);
			CSApplicationTimeWindow = TimeSpan.FromMilliseconds(t.msApplication);

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

		public DateTime GetGenerationStart(int gen)
		{
			gen -= StartGeneration;
			return Start + TimeSpan.FromTicks(GenerationTimeWindow.Ticks * gen);
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
				return Start + GenerationTimeWindow.Times(TopLevelGeneration);
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
				return NextGenerationDeadline - LatestGenerationStart;
			}
		}

		public DateTime NextGenerationDeadline
		{
			get
			{
				return Start + GenerationTimeWindow.Times(TopLevelGeneration + 1);
			}
		}
		public DateTime NextRecoveryStepDeadline
		{
			get
			{
				return LatestGenerationStart + RecoveryStepTimeWindow.Times(LatestRecoveryStepIndex + 1);
			}
		}
		public DateTime NextRecoveryApplicationDeadline
		{
			get
			{
				return LatestGenerationStart + RecoveryStepTimeWindow.Times(LatestRecoveryStepIndex + 1) - CSApplicationTimeWindow;
			}
		}
		/// <summary>
		/// Determines the generation step.
		/// 0 indicates the main processing step, RecoverySteps> x >0 a recovery step.
		/// Note that this index can exceed the configured number of recovery steps when entering the final application phase
		/// or if the simulation has reached the maximum generation
		/// </summary>
		public int LatestRecoveryStepIndex
		{
			get
			{
				var recoveryElapsed = LatestGenerationElapsed - EntityEvaluationTimeWindow;
				return (int)Math.Floor(recoveryElapsed.TotalSeconds / RecoveryStepTimeWindow.TotalSeconds) + 1;
			}
		}
		/// <summary>
		/// Detects whether or not a recovery operation should be initiated right now if not found active
		/// </summary>
		public bool ShouldStartRecovery
		{
			get
			{
				var s = LatestRecoveryStepIndex;
				return s > 0 && s <= RecoveryStepsPerGeneration;
			}
		}
	}

}
