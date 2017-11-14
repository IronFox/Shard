using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShardTests1;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard.Tests
{
	[TestClass()]
	public class ComputationTests
	{
		static Random random = new Random();


		[TestMethod()]
		public void ComputationTest()
		{
			SDS.IntermediateData intermediate = new SDS.IntermediateData();
			intermediate.entities = EntityTest.RandomDefaultPool(100);
			intermediate.ic = InconsistencyCoverage.NewCommon();
			intermediate.inputConsistent = true;
			intermediate.localChangeSet = new EntityChangeSet();

			DB.ConfigContainer config = new DB.ConfigContainer() { extent = new ShardID(new Int3(3), 1), r = 1f / 8, m = 1f / 16 };
			Simulation.Configure(new ShardID(Int3.One, 0), config);


			SDS root = new SDS(null, 0, intermediate.entities.ToArray(), intermediate.ic, intermediate, null, null);
			Assert.IsTrue(root.IsFullyConsistent);

			SDSStack stack = Simulation.Stack;
			stack.Insert(root);
			SDS temp = stack.AllocateGeneration(1);
			Assert.AreEqual(temp.Generation, 1);
			Assert.IsNotNull(stack.FindGeneration(1));
			SDS.Computation comp = new SDS.Computation(1);


		}

		[TestMethod()]
		public void CompleteTest()
		{
			Assert.Fail();
		}
	}
}