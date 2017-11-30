using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
				
				readonly int counter = 0;
				public TestLogic()	{}
				private TestLogic(int counter)
				{
					this.counter = counter;
				}
				public override void Evolve(ref NewState newState, Entity currentState, int generation, Random randomSource)
				{
					newState.newLogic = new TestLogic(counter+1);
				}
			};
		";

		const string disallowedField =
		@"	using Shard;
			using System;
			[Serializable]
			public class TestLogic : Shard.EntityLogic {
				int someField = 3;
				
				public override void Evolve(ref NewState newState, Entity currentState, int generation, Random randomSource)
				{}
			};
		";

		const string disallowedProperty =
		@"	using Shard;
			using System;
			[Serializable]
			public class TestLogic : Shard.EntityLogic {
				public int SomeProperty{get;set;}	//generates a hidden field due to set
				
				public override void Evolve(ref NewState newState, Entity currentState, int generation, Random randomSource)
				{}
			};
		";

		const string disallowedNested =
		@"	using Shard;
			using System;
			[Serializable]
			public class TestLogic : Shard.EntityLogic {
				class Nested
				{
					public int a = 2;
				}

				readonly Nested n = new Nested();
				
				public override void Evolve(ref NewState newState, Entity currentState, int generation, Random randomSource)
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
				public readonly int SomeField = 12;

				struct NestedStruct
				{
					public int testInt;
				}
				readonly NestedStruct test = new NestedStruct() {testInt = 42};
				
				public override void Evolve(ref NewState newState, Entity currentState, int generation, Random randomSource)
				{}
			};
		";

		[TestMethod()]
		public void AllowedLogicTest()
		{
			try
			{
				CSLogicProvider factory0 = new CSLogicProvider("DisallowedA", disallowedField);
				Assert.Fail("The specified code has a non-readonly field. Should have triggered an exception");
			}
			catch (CSLogicProvider.InvarianceViolation)
			{ }
			try
			{
				CSLogicProvider factory1 = new CSLogicProvider("DisallowedB", disallowedProperty);
				Assert.Fail("The specified code has properties with set method. Should have triggered an exception");
			}
			catch (CSLogicProvider.InvarianceViolation)
			{ }

			try
			{
				CSLogicProvider factory1 = new CSLogicProvider("DisallowedNested", disallowedNested);
				Assert.Fail("The specified code has nested types with modifiable fields. Should have triggered an exception");
			}
			catch (CSLogicProvider.InvarianceViolation)
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

			DynamicCSLogic logic = new DynamicCSLogic(provider);

			var serialLogic = Helper.SerializeToArray(logic);

			var logic2 = (DynamicCSLogic)  Helper.Deserialize(serialLogic);
			logic2.FinishLoading(1000);



			var s2 = logic2.EvolveAsync(new Entity(), 0, new Random()).Result;
			Assert.AreEqual(s2.newLogic.GetType(), typeof(DynamicCSLogic));
			Assert.IsFalse(s2.newLogic == logic2);

			var serialProvider = Helper.SerializeToArray(provider);
			var provider2 = (CSLogicProvider)Helper.Deserialize(serialProvider);

			DB.LogicLoader = scriptName => Task.Run(() => provider2);
			var logic3 = (DynamicCSLogic)Helper.Deserialize(serialLogic);
			logic3.FinishLoading(1000);

		}


		[TestMethod()]
		public void ScriptedLogicPerformanceTest()
		{
			Stopwatch watch = new Stopwatch();
			watch.Start();
			CSLogicProvider factory = null;
			for (int i = 0; i < 100; i++)
			{
				factory = new CSLogicProvider("Test", code);
			}
			watch.Stop();
			Console.WriteLine("Compilation took " + watch.Elapsed);

			var binary = Helper.SerializeToArray(factory);
			watch.Reset();
			watch.Start();
			for (int i = 0; i < 100; i++)
			{
				factory = (CSLogicProvider) Helper.Deserialize(binary);
			}
			watch.Stop();
			Console.WriteLine("Loading took " + watch.Elapsed);

		}
	}
}