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

		public ScriptedLogic(ScriptedLogicFactory factory)
		{
			scriptLogic = factory.Instantiate();
			this.factory = factory;
		}

		private ScriptedLogic(ScriptedLogicFactory factory, EntityLogic newState)
		{
			scriptLogic = newState;
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
			newState.newLogic = scriptLogic;
			scriptLogic.Evolve(ref newState, currentState, generation, randomSource);
			if (newState.newLogic == scriptLogic)
				newState.newLogic = this;
			else
				newState.newLogic = new ScriptedLogic(factory,newState.newLogic);
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

			//options.TreatWarningsAsErrors = true;
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
				throw new CompilationException("Unable to compile logic '" + scriptName + "': " + FirstError);

			//if (result.Errors.HasWarnings)
			//Log.Message("Warning while loading logic '" + ScriptName + "': " + FirstWarning);

			HashSet<Type> checkedTypes = new HashSet<Type>() ;
			var a = Assembly;
			// Now that we have a compiled script, lets run them
			foreach (Type type in a.GetExportedTypes())
			{
				if (type.BaseType == typeof(EntityLogic))
				{
					if (!type.IsSerializable)
						throw new LogicCompositionException(ScriptName + ": Type '" + type + "' is entity logic, but not serializable");

					constructor = type.GetConstructor(Type.EmptyTypes);
					if (constructor == null || !constructor.IsPublic)
					{
						throw new LogicCompositionException(ScriptName + ": Type '" + type + "' is entity logic, but has no public constructor with no arguments");
						//continue;
					}
					CheckType(checkedTypes, type);
					//no need to check properties: either they represent/can change some local field, or they are ineffective
					//var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
					//foreach (var p in properties)
					//{
					//	if (p.CanWrite)
					//		throw new LogicCompositionException(ScriptName + ": Property '" + type.Name + "." + p.Name + "' must not have a set method");
					//}

					if (constructor != null)
						break;
				}
			}
			if (constructor == null)
				throw new LogicCompositionException(ScriptName + ": Failed to find entity logic in assembly");
		}

		private void CheckType(HashSet<Type> checkedTypes, Type type, string path = null)
		{
			if (checkedTypes.Contains(type))
				return;
			checkedTypes.Add(type);
			if (path == null)
				path = type.Name;
			var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			foreach (var f in fields)
			{
				string subPath = path + "." + f.Name;// + "(" + f.FieldType + ")";
				if (!f.IsInitOnly)
					throw new InvarianceViolation(ScriptName + ": Field '" + subPath + "' must be declared readonly");
				Type sub = f.FieldType;
				if (sub.IsClass)
					CheckType(checkedTypes, sub, subPath);
			}
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

		[Serializable]
		public class CompilationException : Exception
		{
			public CompilationException()
			{
			}

			public CompilationException(string message) : base(message)
			{
			}

			public CompilationException(string message, Exception innerException) : base(message, innerException)
			{
			}

			protected CompilationException(SerializationInfo info, StreamingContext context) : base(info, context)
			{
			}
		}

		public class LogicCompositionException : Exception
		{
			public LogicCompositionException(string message) : base(message){}
		}
		public class InvarianceViolation : Exception
		{
			public InvarianceViolation(string message) : base(message) { }
		}
	}
}
