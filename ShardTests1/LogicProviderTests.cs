using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard.Tests
{
	[TestClass()]
	public class LogicProviderTests
	{

		const string code =
			@"	using Shard;
				using System;
				[Serializable]
				public class TestLogic : Shard.EntityLogic {
				
				int counter = 0;
				public TestLogic()	{}
				private TestLogic(int counter)
				{
					this.counter = counter;
				}
				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource)
				{
					counter++;
				}
			};
		";

		const string disallowedStructNotSerializable =
		@"	using Shard;
			using System;
			[Serializable]
			public class TestLogic : Shard.EntityLogic {
				struct MyStruct
				{
					int i;
				}
				MyStruct m;
				
				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource)
				{}
			};
		";

		const string disallowedLogicNotSerializable =
		@"	using Shard;
			using System;
			public class TestLogic : Shard.EntityLogic {
				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource)
				{}
			};
		";

		const string disallowedClassNotSerializable =
		@"	using Shard;
			using System;
			[Serializable]
			public class TestLogic : Shard.EntityLogic {
				class Nested
				{
					public int a = 2;
				}

				readonly Nested n = new Nested();
				
				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource)
				{
					n.a = 4;
				}
			};
		";

		const string allowed =
		@"	using Shard;
			using System;
			[Serializable]
			public class TestLogic : Shard.EntityLogic {

				public int SomeProperty{get {return 13;}}
				public int SomeField = 12;

				[Serializable]
				struct NestedStruct
				{
					public int testInt;
				}
				NestedStruct test = new NestedStruct() {testInt = 42};
				
				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource)
				{}
			};
		";

		const string instantiationTest =
		@"	using Shard;
			using System;
			using VectorMath;

			[Serializable]
			public class InstantiatedLogic : Shard.EntityLogic {
				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource)
				{
					//self-destruct
					actions.Kill(currentState.ID);
				}
			};
			[Serializable]
			public class InstantiatorLogic : Shard.EntityLogic {

				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource)
				{
					//Vec3 targetLocation, EntityLogic logic, EntityAppearanceCollection appearances
					//if (generation == 1)
						actions.Instantiate(currentState.ID.Position + randomSource.NextVec3(-1,1),new InstantiatedLogic(),null);
				}
			};
		";

		[TestMethod()]
		public void AllowedLogicTest()
		{
			try
			{
				CSLogicProvider factory0 = new CSLogicProvider("DisallowedA", disallowedStructNotSerializable);
				Assert.Fail("The specified code has a non-serializable struct field. Should have triggered an exception");
			}
			catch (CSLogicProvider.SerializationException)
			{ }
			try
			{
				CSLogicProvider factory1 = new CSLogicProvider("DisallowedB", disallowedLogicNotSerializable);
				Assert.Fail("The specified code has a non-serializable logic. Should have triggered an exception");
			}
			catch (CSLogicProvider.SerializationException)
			{ }

			try
			{
				CSLogicProvider factory1 = new CSLogicProvider("DisallowedNested", disallowedClassNotSerializable);
				Assert.Fail("The specified code has a non-serializable class field. Should have triggered an exception");
			}
			catch (CSLogicProvider.SerializationException)
			{ }

			CSLogicProvider factory2 = new CSLogicProvider("Allowed", allowed);


		}

		[TestMethod()]
		public void LogicProviderTest()
		{
			DB.LogicLoader = scriptName => Task.Run( () => new CSLogicProvider(scriptName, code));
			CSLogicProvider provider = new CSLogicProvider("Test", code);
			var exported = provider.Export();
			var imported = new CSLogicProvider(exported);
			Assert.AreEqual(provider, imported);

			DynamicCSLogic logic = new DynamicCSLogic(provider,null,null);

			var serialLogic = Helper.SerializeToArray(logic);

			var logic2 = (DynamicCSLogic)  Helper.Deserialize(serialLogic);
			logic2.FinishLoading(new EntityID(), TimeSpan.FromSeconds(1));



			var serialProvider = Helper.SerializeToArray(provider);
			var provider2 = (CSLogicProvider)Helper.Deserialize(serialProvider);

			DB.LogicLoader = scriptName => Task.Run(() => provider2);
			var logic3 = (DynamicCSLogic)Helper.Deserialize(serialLogic);
			logic3.FinishLoading(new EntityID(),TimeSpan.FromSeconds(1));

		}

		[TestMethod()]
		public void ScriptedLogicInstantiationTest()
		{
			CSLogicProvider provider = new CSLogicProvider("Test", instantiationTest);
			DB.LogicLoader = scriptName => Task.Run(() => provider);


			DB.ConfigContainer config = new DB.ConfigContainer() { extent = new ShardID(new Int3(1), 1), r = 1f / 8, m = 1f / 16 };
			Simulation.Configure(new ShardID(Int3.Zero, 0), config, true);

			SDS.IntermediateData intermediate = new SDS.IntermediateData();
			intermediate.entities = new EntityPool(
				new Entity[]
				{
					new Entity(
						new EntityID(Guid.NewGuid(), Simulation.MySpace.Center),
						new DynamicCSLogic(provider,"InstantiatorLogic",null),
						null),
				}
			);
			//EntityTest.RandomDefaultPool(100);
			intermediate.ic = InconsistencyCoverage.NewCommon();
			intermediate.inputConsistent = true;
			intermediate.localChangeSet = new EntityChangeSet();
			Assert.AreEqual(intermediate.entities.Count, 1);

			SDS root = new SDS(0, intermediate.entities.ToArray(), intermediate.ic, intermediate, null, null);
			Assert.IsTrue(root.IsFullyConsistent);

			SDSStack stack = Simulation.Stack;
			stack.ResetToRoot(root);

			const int NumIterations = 3;

			for (int i = 0; i < NumIterations; i++)
			{
				SDS temp = stack.AllocateGeneration(i + 1);
				SDS.Computation comp = new SDS.Computation(i + 1, null,TimeSpan.FromMilliseconds(1));
				ComputationTests.AssertNoErrors(comp,i.ToString());
				int instantiations = comp.Intermediate.localChangeSet.NamedSets.Where(pair => pair.Key == "instantiations").First().Value.Size;
				Assert.AreEqual(instantiations, 1);
				Assert.AreEqual(comp.Intermediate.entities.Count, Math.Min(i+1,2));	//can never be more than 2
				Assert.AreEqual(comp.Intermediate.ic.OneCount, 0);
				Assert.IsTrue(comp.Intermediate.inputConsistent);
				SDS sds = comp.Complete();
				Assert.AreEqual(sds.FinalEntities.Length, 2);	//previous clone self-destructed, so we are back to exactly 2
				Assert.IsTrue(sds.IsFullyConsistent);
				stack.Insert(sds);
			}
			Assert.AreEqual(1, stack.Size);
			foreach (var e in stack.Last().FinalEntities)
			{
				var st = Helper.Deserialize(e.SerialLogicState);
				Assert.IsTrue(st is DynamicCSLogic, st.GetType().ToString());
			}
		}



		const string remoteTestA =
		@"	using Shard;
			using System;
			using VectorMath;

			public class CantSerialize
			{}

			[Serializable]
			public class InstantiatorLogic : Shard.EntityLogic {

				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource)
				{
					actions.Instantiate(currentState.ID.Position + randomSource.NextVec3(-1,1),""RemoteB"",""InstantiatedLogic"",new object[]{""My Little Secret""},null);
				}
			};
		";

		const string remoteTestB =
		@"	using Shard;
			using System;
			using VectorMath;

			[Serializable]
			public class InstantiatedLogic : Shard.EntityLogic {
				public InstantiatedLogic()
				{
					throw new Exception(""Secret not given"");
				}
				public InstantiatedLogic(string test)
				{
					if (test != ""My Little Secret"")
						throw new Exception(""Secret not given or wrong"");
				}
				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource)
				{
					//self-destruct
					actions.Kill(currentState.ID);
				}
			};
		";

		[TestMethod()]
		public void ScriptedRemoteLogicInstantiationTest()
		{
			CSLogicProvider providerA = new CSLogicProvider("RemoteA", remoteTestA);
			CSLogicProvider providerB = new CSLogicProvider("RemoteB", remoteTestB);
			DB.LogicLoader = scriptName => Task.Run(() => scriptName == providerA.AssemblyName ? providerA : providerB);


			DB.ConfigContainer config = new DB.ConfigContainer() { extent = new ShardID(new Int3(1), 1), r = 1f / 8, m = 1f / 16 };
			Simulation.Configure(new ShardID(Int3.Zero, 0), config, true);

			SDS.IntermediateData intermediate = new SDS.IntermediateData();
			intermediate.entities = new EntityPool(
				new Entity[]
				{
					new Entity(
						new EntityID(Guid.NewGuid(), Simulation.MySpace.Center),
						new DynamicCSLogic(providerA,"InstantiatorLogic",null),
						null),
				}
			);
			//EntityTest.RandomDefaultPool(100);
			intermediate.ic = InconsistencyCoverage.NewCommon();
			intermediate.inputConsistent = true;
			intermediate.localChangeSet = new EntityChangeSet();
			Assert.AreEqual(intermediate.entities.Count, 1);

			SDS root = new SDS(0, intermediate.entities.ToArray(), intermediate.ic, intermediate, null, null);
			Assert.IsTrue(root.IsFullyConsistent);

			SDSStack stack = Simulation.Stack;
			stack.ResetToRoot(root);

			const int NumIterations = 3;

			for (int i = 0; i < NumIterations; i++)
			{
				SDS temp = stack.AllocateGeneration(i + 1);
				SDS.Computation comp = new SDS.Computation(i + 1, null,TimeSpan.FromMilliseconds(100));
				ComputationTests.AssertNoErrors(comp, i.ToString());
				int instantiations = comp.Intermediate.localChangeSet.NamedSets.Where(pair => pair.Key == "instantiations").First().Value.Size;
				Assert.AreEqual(instantiations, 1);
				Assert.AreEqual(comp.Intermediate.entities.Count, Math.Min(i + 1, 2));  //can never be more than 2
				Assert.AreEqual(comp.Intermediate.ic.OneCount, 0);
				Assert.IsTrue(comp.Intermediate.inputConsistent);
				SDS sds = comp.Complete();
				Assert.AreEqual(sds.FinalEntities.Length, 2);   //previous clone self-destructed, so we are back to exactly 2
				Assert.IsTrue(sds.IsFullyConsistent);
				stack.Insert(sds);
			}
			Assert.AreEqual(1, stack.Size);
			foreach (var e in stack.Last().FinalEntities)
			{
				var st = Helper.Deserialize(e.SerialLogicState);
				Assert.IsTrue(st is DynamicCSLogic, st.GetType().ToString());
			}
		}




		[TestMethod()]
		public void ScriptedLogicPerformanceTest()
		{
			Stopwatch watch = new Stopwatch();
			watch.Start();
			CSLogicProvider factory = null;
			for (int i = 0; i < 10; i++)
			{
				factory = new CSLogicProvider("Test", code);
			}
			watch.Stop();
			Console.WriteLine("Compilation of 10 scripts took " + watch.Elapsed);

			var binary = Helper.SerializeToArray(factory);
			watch.Reset();
			watch.Start();
			for (int i = 0; i < 100; i++)
			{
				factory = (CSLogicProvider) Helper.Deserialize(binary);
			}
			watch.Stop();
			Console.WriteLine("Loading of 100 assemblies took " + watch.Elapsed);

		}


	}
}