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
				
				public override void Evolve(ref NewState newState, Entity currentState, int generation, Random randomSource)
				{

				}

				public override int CompareTo(EntityLogic other)
				{
					return 0;
				}
			};
		";


		[TestMethod()]
		public void ScriptedLogicFactoryTest()
		{
			DB.LogicLoader = scriptName => Task.Run( () => new ScriptedLogicFactory(scriptName, code));
			ScriptedLogicFactory factory = new ScriptedLogicFactory("Test", code);
			var warning = factory.FirstWarning;
			Assert.IsNull(warning);

			ScriptedLogic logic = new ScriptedLogic(factory);

			var serial = Helper.SerializeToArray(logic);

			var deserial = (ScriptedLogic)  Helper.Deserialize(serial);
			deserial.FinishLoading(1000);
			
		}
	}
}