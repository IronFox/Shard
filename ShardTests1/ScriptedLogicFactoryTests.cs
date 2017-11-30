using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard.Tests
{
	[TestClass()]
	public class ScriptedLogicFactoryTests
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
		public void AllowedScriptedLogicFactoryTest()
		{
			try
			{
				ScriptedLogicFactory factory0 = new ScriptedLogicFactory("DisallowedA", disallowedField);
				Assert.Fail("The specified code has a non-readonly field. Should have triggered an exception");
			}
			catch (ScriptedLogicFactory.InvarianceViolation)
			{ }
			try
			{
				ScriptedLogicFactory factory1 = new ScriptedLogicFactory("DisallowedB", disallowedProperty);
				Assert.Fail("The specified code has properties with set method. Should have triggered an exception");
			}
			catch (ScriptedLogicFactory.InvarianceViolation)
			{ }

			try
			{
				ScriptedLogicFactory factory1 = new ScriptedLogicFactory("DisallowedNested", disallowedNested);
				Assert.Fail("The specified code has nested types with modifyable fields. Should have triggered an exception");
			}
			catch (ScriptedLogicFactory.InvarianceViolation)
			{ }

			ScriptedLogicFactory factory2 = new ScriptedLogicFactory("Allowed", allowed);


		}

		[TestMethod()]
		public void ScriptedLogicFactoryTest()
		{
			DB.LogicLoader = scriptName => Task.Run( () => new ScriptedLogicFactory(scriptName, code));
			ScriptedLogicFactory factory = new ScriptedLogicFactory("Test", code);
			var warning = factory.FirstWarning;
			Assert.IsNull(warning);

			ScriptedLogic logic = new ScriptedLogic(factory);

			var serialLogic = Helper.SerializeToArray(logic);

			var logic2 = (ScriptedLogic)  Helper.Deserialize(serialLogic);
			logic2.FinishLoading(1000);



			var s2 = logic2.EvolveAsync(new Entity(), 0, new Random()).Result;
			Assert.AreEqual(s2.newLogic.GetType(), typeof(ScriptedLogic));
			Assert.IsFalse(s2.newLogic == logic2);
		}
	}
}