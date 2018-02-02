using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorMath;
using static Shard.Tests.ComputationTests;

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
				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges)
				{
					counter++;
				}
			};
		";


		const string sharedCode =
			@"	using Shard;
				using System;
				[Serializable]
				public class SomeClass
				{
					public int SomeInt;
				}
		";

		const string usingSharedCode =
			@"	#reference shared
				using Shard;
				using System;
				[Serializable]
				public class SharingTestLogic : Shard.EntityLogic {
					public SomeClass cls = new SomeClass();
					int counter = 0;
					public SharingTestLogic()	{}
					private SharingTestLogic(int counter)
					{
						this.counter = counter;
					}
					protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges)
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
				
				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges)
				{}
			};
		";

		const string disallowedLogicNotSerializable =
		@"	using Shard;
			using System;
			public class TestLogic : Shard.EntityLogic {
				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges)
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
				
				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges)
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
				
				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges)
				{}
			};
		";

		const string instantiationTest =
		@"	using Shard;
			using System;
			using VectorMath;

			[Serializable]
			public class InstantiatedLogic : Shard.EntityLogic {
				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges)
				{
					//self-destruct
					actions.Kill(currentState.ID);
				}
			};
			[Serializable]
			public class InstantiatorLogic : Shard.EntityLogic {

				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges)
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
				CSLogicProvider factory0 = CSLogicProvider.CompileAsync("DisallowedA", disallowedStructNotSerializable).Get();
				Assert.Fail("The specified code has a non-serializable struct field. Should have triggered an exception");
			}
			catch (CSLogicProvider.SerializationException)
			{ }
			try
			{
				CSLogicProvider factory1 = CSLogicProvider.CompileAsync("DisallowedB", disallowedLogicNotSerializable).Get();
				Assert.Fail("The specified code has a non-serializable logic. Should have triggered an exception");
			}
			catch (CSLogicProvider.SerializationException)
			{ }

			try
			{
				CSLogicProvider factory1 = CSLogicProvider.CompileAsync("DisallowedNested", disallowedClassNotSerializable).Get();
				Assert.Fail("The specified code has a non-serializable class field. Should have triggered an exception");
			}
			catch (CSLogicProvider.SerializationException)
			{ }

			CSLogicProvider factory2 = CSLogicProvider.CompileAsync("Allowed", allowed).Get();


		}

		[TestMethod()]
		public void LogicProviderTest()
		{
			CSLogicProvider.AsyncFactory = scriptName => CSLogicProvider.CompileAsync(scriptName, code);
			CSLogicProvider provider = CSLogicProvider.CompileAsync("Test", code).Get();
			var exported = new SerialCSLogicProvider( provider );
			var imported = exported.Deserialize();
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
		public void ReferencedProviderTest()
		{
			CSLogicProvider sharedP = CSLogicProvider.CompileAsync("shared", sharedCode).Get();
			CSLogicProvider.AsyncFactory = scriptName => Task.Run(()=>sharedP);
			CSLogicProvider usingSharedP = CSLogicProvider.CompileAsync("using", usingSharedCode).Get();
			DynamicCSLogic logic = new DynamicCSLogic(usingSharedP, null, null);
			logic.FinishLoading(new EntityID(),TimeSpan.Zero);

			var serialProvider = Helper.SerializeToArray(usingSharedP);
			var provider2 = (CSLogicProvider)Helper.Deserialize(serialProvider);

			var serialLogic = Helper.SerializeToArray(logic);
			CSLogicProvider.AsyncFactory = scriptName => Task.Run(() => scriptName == "shared" ? sharedP : provider2);
			var logic3 = (DynamicCSLogic)Helper.Deserialize(serialLogic);
			logic3.FinishLoading(new EntityID(), TimeSpan.FromHours(1));
		}

		[TestMethod()]
		public void ScriptedLogicInstantiationTest()
		{
			CSLogicProvider provider = CSLogicProvider.CompileAsync("Test", instantiationTest).Result;
			DB.LogicLoader =
				CSLogicProvider.AsyncFactory = scriptName => Task.Run(() => provider);

			SimulationRun run = new SimulationRun(
				new DB.ConfigContainer() { extent = new ShardID(new Int3(1), 1), r = 1f / 8, m = 1f / 16 },
				new ShardID(Int3.Zero, 0),
				new Entity[]
				{
					new Entity(
						new EntityID(Guid.NewGuid(), Simulation.MySpace.Center),
						Vec3.Zero, 
						new DynamicCSLogic(provider,"InstantiatorLogic",null),
						null),
				}
			);
			
			const int NumIterations = 3;

			for (int i = 0; i < NumIterations; i++)
			{
				var rs = run.AdvanceTLG(true, true);
				int instantiations = rs.IntermediateSDS.localChangeSet.NamedSets.Where(pair => pair.Key == "instantiations").First().Value.Size;
				Assert.AreEqual(instantiations, 1);
				Assert.AreEqual(rs.IntermediateSDS.entities.Count, Math.Min(i+1,2));	//can never be more than 2
				Assert.AreEqual(rs.SDS.FinalEntities.Length, 2);	//previous clone self-destructed, so we are back to exactly 2
			}
			Assert.AreEqual(1, run.stack.Size);
			foreach (var e in run.stack.Last().SDS.FinalEntities)
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

				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges)
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
				protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges)
				{
					//self-destruct
					actions.Kill(currentState.ID);
				}
			};
		";

		[TestMethod()]
		public void ScriptedRemoteLogicInstantiationTest()
		{
			CSLogicProvider providerA = CSLogicProvider.CompileAsync("RemoteA", remoteTestA).Result;
			CSLogicProvider providerB = CSLogicProvider.CompileAsync("RemoteB", remoteTestB).Result;
			DB.LogicLoader = CSLogicProvider.AsyncFactory =  scriptName => Task.Run(() => scriptName == providerA.AssemblyName ? providerA : providerB);


			SimulationRun run = new SimulationRun(
				new DB.ConfigContainer() { extent = new ShardID(new Int3(1), 1), r = 1f / 8, m = 1f / 16 },
				new ShardID(Int3.Zero, 0),
				new Entity[]
				{
					new Entity(
						new EntityID(Guid.NewGuid(), Simulation.MySpace.Center),
						Vec3.Zero, 
						new DynamicCSLogic(providerA,"InstantiatorLogic",null),
						null),
				}
			);


			const int NumIterations = 3;

			for (int i = 0; i < NumIterations; i++)
			{
				var rs = run.AdvanceTLG(true, true);
				int instantiations = rs.IntermediateSDS.localChangeSet.NamedSets.Where(pair => pair.Key == "instantiations").First().Value.Size;
				Assert.AreEqual(instantiations, 1);
				Assert.AreEqual(rs.IntermediateSDS.entities.Count, Math.Min(i + 1, 2));  //can never be more than 2
				Assert.AreEqual(rs.SDS.FinalEntities.Length, 2);   //previous clone self-destructed, so we are back to exactly 2
			}
			Assert.AreEqual(1, run.stack.Size);
			foreach (var e in run.stack.Last().SDS.FinalEntities)
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
				factory = CSLogicProvider.CompileAsync("Test", code).Result;
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