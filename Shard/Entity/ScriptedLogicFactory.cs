using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	[Serializable]
	public class ScriptedLogic : EntityLogic, ISerializable
	{
		private EntityLogic scriptLogic;
		private ScriptedLogicFactory factory;
		private Constructor constructor;

		class Constructor
		{
			Task task;
			ScriptedLogicFactory factory;
			EntityLogic instance;
			public readonly string ScriptName;
			public readonly byte[] SerialData;

			public Constructor(string scriptName, byte[] data)
			{
				ScriptName = scriptName;
				SerialData = data;
				task = Load();
			}

			private async Task Load()
			{
				factory = await DB.GetLogicAsync(ScriptName);
				instance = factory.Deserialize(SerialData);
			}

			public void Finish(ScriptedLogic target, int timeoutMS)
			{
				if (!task.Wait(timeoutMS))
					throw new ExecutionException(ScriptName+": Failed to load/deserialize in time");
				target.factory = factory;
				target.scriptLogic = instance;
			}


		}


		public override int CompareTo(EntityLogic log)
		{
			return 0;
		}

		public ScriptedLogic(ScriptedLogicFactory factory)
		{
			scriptLogic = factory.Instantiate();
			this.factory = factory;
		}

		public void FinishLoading(int timeoutMS)
		{
			if (scriptLogic == null)
			{
				constructor.Finish(this, timeoutMS);
				constructor = null;
			}
		}

		public override void Evolve(ref NewState newState, Entity currentState, int generation, Random randomSource)
		{
			FinishLoading(1);
			scriptLogic.Evolve(ref newState, currentState, generation, randomSource);
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			if (constructor != null)
			{
				info.AddValue("scriptName", constructor.ScriptName);
				info.AddValue("state", constructor.SerialData);
				return;
			}
			info.AddValue("scriptName", factory.ScriptName);
			info.AddValue("state", Helper.SerializeToArray( scriptLogic ));
		}


		public ScriptedLogic(SerializationInfo info, StreamingContext context)
		{
			constructor = new Constructor(info.GetString("scriptName"),(byte[])info.GetValue("state", typeof(byte[])));
		}

	}

	public class ScriptedLogicFactory
	{
		CompilerResults result;
		ConstructorInfo constructor;

		public readonly string ScriptName;

		public Assembly Assembly
		{
			get
			{
				return result.CompiledAssembly;
			}
		}

		public EntityLogic Instantiate()
		{
			return constructor.Invoke(null) as EntityLogic;
		}

		public EntityLogic Deserialize(byte[] serialData)
		{
			return (EntityLogic)Helper.Deserialize(serialData, Assembly, true);
		}

		public ScriptedLogicFactory(string scriptName, string code)
		{
			ScriptName = scriptName;
			//https://stackoverflow.com/questions/137933/what-is-the-best-scripting-language-to-embed-in-a-c-sharp-desktop-application
			// Create a code provider
			// This class implements the 'CodeDomProvider' class as its base. All of the current .Net languages (at least Microsoft ones)
			// come with thier own implemtation, thus you can allow the user to use the language of thier choice (though i recommend that
			// you don't allow the use of c++, which is too volatile for scripting use - memory leaks anyone?)
			Microsoft.CSharp.CSharpCodeProvider csProvider = new Microsoft.CSharp.CSharpCodeProvider();

			// Setup our options
			CompilerParameters options = new CompilerParameters();
			options.GenerateExecutable = false; // we want a Dll (or "Class Library" as its called in .Net)
			options.GenerateInMemory = true; // Saves us from deleting the Dll when we are done with it, though you could set this to false and save start-up time by next time by not having to re-compile
											 // And set any others you want, there a quite a few, take some time to look through them all and decide which fit your application best!

			// Add any references you want the users to be able to access, be warned that giving them access to some classes can allow
			// harmful code to be written and executed. I recommend that you write your own Class library that is the only reference it allows
			// thus they can only do the things you want them to.
			// (though things like "System.Xml.dll" can be useful, just need to provide a way users can read a file to pass in to it)
			// Just to avoid bloatin this example to much, we will just add THIS program to its references, that way we don't need another
			// project to store the interfaces that both this class and the other uses. Just remember, this will expose ALL public classes to
			// the "script"
			options.ReferencedAssemblies.Add(Assembly.GetExecutingAssembly().Location);
			// Compile our code
			result = csProvider.CompileAssemblyFromSource(options, code);
			if (result.Errors.HasErrors)
				throw new ExecutionException("Unable to load logic '" + scriptName + "': " + FirstError);

			if (result.Errors.HasWarnings)
				Log.Message("Warning while loading logic '" + ScriptName + "': " + FirstWarning);

			var a = Assembly;
			// Now that we have a compiled script, lets run them
			foreach (Type type in a.GetExportedTypes())
			{
				if (type.BaseType == typeof(EntityLogic))
				{
					if (!type.IsSerializable)
						throw new ExecutionException(ScriptName + ": Type '" + type + "' is entity logic, but not serializable");

					constructor = type.GetConstructor(Type.EmptyTypes);
					if (!constructor.IsPublic)
					{
						constructor = null;
						throw new ExecutionException(ScriptName + ": Type '" + type + "' is entity logic, but has no public constructor with no arguments");
					}

					if (constructor != null)
						break;
				}
			}
			if (constructor == null)
				throw new ExecutionException(ScriptName + ": Failed to find entity logic in assembly");
		}

		public CompilerError FirstError
		{
			get
			{
				for (int i = 0; i < result.Errors.Count; i++)
					if (!result.Errors[i].IsWarning)
						return result.Errors[i];
				return null;
			}
		}
		public CompilerError FirstWarning
		{
			get
			{
				for (int i = 0; i < result.Errors.Count; i++)
					if (result.Errors[i].IsWarning)
						return result.Errors[i];
				return null;
			}
		}
	}
}
