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
		/* layout:
		 * 
		 * main:		|es         a   |
		 * recovery:	|  esaesaesa esa|
		 */




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

		public TimeSpan SynchronizationTimeWindow => RecoveryStepTimeWindow - CSApplicationTimeWindow - EntityEvaluationTimeWindow;

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
				var rs = StartGeneration + Math.Max(0, SimulationTime.FloorDiv(GenerationTimeWindow));
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
				return NextGenerationDeadline - Clock.Now;
			}
		}

		public DateTime NextGenerationDeadline
		{
			get
			{
				return LatestGenerationStart + GenerationTimeWindow;
					//Start + GenerationTimeWindow.Times(TopLevelGeneration + 1);
			}
		}
		/// <summary>
		/// Determines the generation step.
		/// 0 indicates the main processing step, RecoverySteps> x >0 a recovery step.
		/// </summary>
		public int LatestRecoveryStepIndex
		{
			get
			{
				return GetRecoveryStepIndex(LatestGenerationElapsed);
			}
		}

		public int LatestAbsoluteRecoveryStepIndex
		{
			get
			{
				int local;
				return GetAbsoluteRecoveryStepIndex(SimulationTime, out local);
			}
		}

		public int GetAbsoluteRecoveryStepIndex(TimeSpan elapsedSinceSimulationStart, out int local)
		{
			int gen = elapsedSinceSimulationStart.FloorDiv(GenerationTimeWindow);
			local = GetRecoveryStepIndex(elapsedSinceSimulationStart - GenerationTimeWindow.Times(gen));
			return local + gen * (RecoveryStepsPerGeneration + 1);
		}

		public TimeSpan GetRecoveryStepStart(int absoluteRecoveryStepIndex)
		{
			int local = absoluteRecoveryStepIndex % (RecoveryStepsPerGeneration + 1);
			if (local == 0)
				throw new ArgumentOutOfRangeException("Trying to query start of non-recovery step #" + absoluteRecoveryStepIndex);
			var gen = absoluteRecoveryStepIndex / (RecoveryStepsPerGeneration + 1);
			var offset = GenerationTimeWindow.Times(gen) + EntityEvaluationTimeWindow + SynchronizationTimeWindow;
			if (local < RecoveryStepsPerGeneration)
				return offset + RecoveryStepTimeWindow.Times(local - 1);
			return offset + RecoveryStepTimeWindow.Times(local - 1) + CSApplicationTimeWindow;
		}

		public int GetRecoveryStepIndex(TimeSpan elapsedSinceGenerationStart)
		{
			var recoveryElapsed = elapsedSinceGenerationStart - EntityEvaluationTimeWindow - SynchronizationTimeWindow;
			if (recoveryElapsed.Ticks < 0)
				return 0;
			int numFrames = (int)Math.Floor(recoveryElapsed.TotalSeconds / RecoveryStepTimeWindow.TotalSeconds);
			if (numFrames + 1 < RecoveryStepsPerGeneration)
				return numFrames + 1;
			recoveryElapsed -= RecoveryStepTimeWindow.Times(RecoveryStepsPerGeneration - 1);
			recoveryElapsed -= CSApplicationTimeWindow;
			if (recoveryElapsed.Ticks < 0)
				return 0;// RecoveryStepsPerGeneration-1;
			return RecoveryStepsPerGeneration;
		}

		public TimeSpan ElapsedMainComputationTimeWindow => GenerationTimeWindow - CSApplicationTimeWindow - RecoveryStepTimeWindow;

		public DateTime NextMainComputationDeadline => LatestGenerationStart + EntityEvaluationTimeWindow;
		public DateTime NextMainApplicationDeadline => NextGenerationDeadline - RecoveryStepTimeWindow - CSApplicationTimeWindow;

		public bool ShouldStartRecoveryAt(TimeSpan timeSinceSimulationStart, ref int lastRecoveryIndex)
		{
			int local;
			int ridx = GetAbsoluteRecoveryStepIndex(timeSinceSimulationStart, out local);
			if (ridx == lastRecoveryIndex)
				return false;
			lastRecoveryIndex = ridx;
			return local > 0;
		}
		public bool ShouldStartRecovery(ref int lastRecoveryIndex)
		{
			return ShouldStartRecoveryAt(SimulationTime, ref lastRecoveryIndex);
		}

		public bool IsMainStep(int absoluteRecoveryStepIndex)
		{
			return (absoluteRecoveryStepIndex % (RecoveryStepsPerGeneration + 1)) == 0;
		}

		public DateTime GetRecoveryStepComputationDeadline(int absoluteRecoveryStepIndex)
		{
			if (IsMainStep(absoluteRecoveryStepIndex))
				throw new ArgumentOutOfRangeException("Trying to query computation deadline of non-recovery step #"+ absoluteRecoveryStepIndex);
			return Start + GetRecoveryStepStart(absoluteRecoveryStepIndex) + EntityEvaluationTimeWindow;
		}

		public DateTime GetRecoveryStepApplicationDeadline(int absoluteRecoveryStepIndex)
		{
			if (IsMainStep(absoluteRecoveryStepIndex))
				throw new ArgumentOutOfRangeException("Trying to query application deadline of non-recovery step #" + absoluteRecoveryStepIndex);
			return Start + GetRecoveryStepStart(absoluteRecoveryStepIndex) + RecoveryStepTimeWindow - CSApplicationTimeWindow;
		}

	}

}
