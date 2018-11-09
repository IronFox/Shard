using System;
using Base;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;

namespace ShardTests1
{
	[TestClass]
	public class TimingTests
	{
		[TestMethod]
		public void TimingInfo()
		{
			int msBudgetPerStep = 300;
			for (int steps = 2; steps < 5; steps++)
			{
				Console.WriteLine("Begin with steps=" + steps);
				Clock.TimeOverrideFunction = () => new DateTime();
				int msBudget = msBudgetPerStep * (steps + 1);
				TimingInfo ifo = new TimingInfo(new BaseDB.TimingContainer() { startTime = Clock.Now.ToString(), msGenerationBudget = msBudget, msApplication = 100, msComputation = 100, recoverySteps = steps });

				var computeEnd = ifo.NextMainComputationDeadline.ToTotalMilliseconds();
				var applicationBegin = ifo.NextMainApplicationDeadline.ToTotalMilliseconds();

				Assert.AreEqual(computeEnd, 100);
				Assert.AreEqual(applicationBegin, msBudget - msBudgetPerStep - 100);

				int totalSteps = (steps + 1);
				for (int i = 0; i < totalSteps * 3; i++)
				{
					Assert.AreEqual(ifo.IsMainStep(i), (i % totalSteps) == 0);
					if (!ifo.IsMainStep(i))
					{
						var begin = ifo.GetRecoveryStepStart(i);
						var apply = ifo.GetRecoveryStepApplicationDeadline(i) - ifo.Start;
						Assert.AreEqual((apply - begin).TotalMilliseconds, 200);
						int local;
						var s0 = ifo.GetAbsoluteRecoveryStepIndex(begin, out local);
						Assert.AreEqual(s0, i);
						var s1 = ifo.GetAbsoluteRecoveryStepIndex(begin + TimeSpan.FromMilliseconds(1), out local);
						Assert.AreEqual(s1, i);
					}
				}


				for (int m = 0; m < msBudget * 3; m++)
				{
					int local;
					var s = ifo.GetAbsoluteRecoveryStepIndex(TimeSpan.FromMilliseconds(m), out local);
					if (ifo.IsMainStep(s))
						continue;
					var begin = ifo.GetRecoveryStepStart(s);
					Assert.IsTrue(m >= begin.TotalMilliseconds);
					Assert.IsTrue(m <= (begin + ifo.RecoveryStepTimeWindow).TotalMilliseconds);
				}
			}
		}
	}
}
