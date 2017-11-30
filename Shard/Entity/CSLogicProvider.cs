using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	[Serializable]
	public class DynamicCSLogic : EntityLogic, ISerializable
	{
		private EntityLogic scriptLogic;
		private CSLogicProvider provider;
		private Constructor constructor;

		class Constructor
		{
			Task task;
			CSLogicProvider provider;
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
				provider = await DB.GetLogicProviderAsync(ScriptName);
				instance = provider.DeserializeLogic(SerialData);
			}

			public void Finish(DynamicCSLogic target, int timeoutMS)
			{
				if (!task.Wait(timeoutMS))
					throw new ExecutionException(ScriptName+": Failed to load/deserialize in time");
				target.provider = provider;
				target.scriptLogic = instance;
			}


		}

		public DynamicCSLogic(CSLogicProvider provider)
		{
			scriptLogic = provider.Instantiate();
			this.provider = provider;
		}

		private DynamicCSLogic(CSLogicProvider provider, EntityLogic newState)
		{
			scriptLogic = newState;
			this.provider = provider;
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
				newState.newLogic = new DynamicCSLogic(provider,newState.newLogic);
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			if (constructor != null)
			{
				info.AddValue("scriptName", constructor.ScriptName);
				info.AddValue("state", constructor.SerialData);
				return;
			}
			info.AddValue("scriptName", provider.ScriptName);
			info.AddValue("state", Helper.SerializeToArray( scriptLogic ));
		}


		public DynamicCSLogic(SerializationInfo info, StreamingContext context)
		{
			constructor = new Constructor(info.GetString("scriptName"),(byte[])info.GetValue("state", typeof(byte[])));
		}

	}

	[Serializable]
	public class CSLogicProvider : ISerializable
	{

		public class DBSerial : DB.Entity
		{
			public byte[] compiledAssembly;
			public string sourceCode;
		}


		//CompilerResults result;
		public readonly Assembly Assembly;
		public readonly byte[] BinaryAssembly;
		public readonly ConstructorInfo Constructor;

		public readonly string ScriptName,SourceCode;
		public readonly bool FromScript;

		public override bool Equals(object obj)
		{
			CSLogicProvider other = obj as CSLogicProvider;
			return other != null
					//&& other.assembly.Equals(assembly)
					&& Helper.AreEqual(other.BinaryAssembly,BinaryAssembly)
					&& ScriptName == other.ScriptName
					&& SourceCode == other.SourceCode;
		}


		public EntityLogic Instantiate()
		{
			return Constructor.Invoke(null) as EntityLogic;
		}

		public EntityLogic DeserializeLogic(byte[] serialData)
		{
			return (EntityLogic)Helper.Deserialize(serialData, Assembly, FromScript);
		}

		public CSLogicProvider(DBSerial serial)
		{
			FromScript = serial.compiledAssembly == null || serial.compiledAssembly.Length == 0;
			ScriptName = serial._id;
			SourceCode = serial.sourceCode;
			BinaryAssembly = serial.compiledAssembly;

			if (FromScript)
				Assembly = Compile(SourceCode, out BinaryAssembly);
			else
				Assembly = Assembly.Load(BinaryAssembly);

			CheckAssembly(out Constructor);
		}

		public DBSerial Export()
		{
			return new DBSerial()
			{
				compiledAssembly = BinaryAssembly,
				_id = ScriptName,
				sourceCode = SourceCode
			};
		}


		public CSLogicProvider(string scriptName, byte[] binaryAssembly)
		{
			FromScript = false;
			ScriptName = scriptName;
			Assembly = Assembly.Load(binaryAssembly);
			this.BinaryAssembly = binaryAssembly;
			CheckAssembly(out Constructor);
		}


		public CSLogicProvider(string scriptName, string code)
		{
			SourceCode = code;
			FromScript = true;
			ScriptName = scriptName;
			Assembly = Compile(code, out BinaryAssembly);
			CheckAssembly(out Constructor);
		}

		private static Assembly Compile(string code, out byte[] binaryAssembly)
		{
			//https://stackoverflow.com/questions/137933/what-is-the-best-scripting-language-to-embed-in-a-c-sharp-desktop-application
			// Create a code provider
			// This class implements the 'CodeDomProvider' class as its base. All of the current .Net languages (at least Microsoft ones)
			// come with thier own implemtation, thus you can allow the user to use the language of thier choice (though i recommend that
			// you don't allow the use of c++, which is too volatile for scripting use - memory leaks anyone?)
			Microsoft.CSharp.CSharpCodeProvider csProvider = new Microsoft.CSharp.CSharpCodeProvider();
			// Setup our options
			CompilerParameters options = new CompilerParameters();
			options.GenerateExecutable = false; // we want a Dll (or "Class Library" as its called in .Net)
			options.GenerateInMemory = false;
											 

//			Directory.CreateDirectory("logicAssemblies");
//			options.OutputAssembly = Path.Combine("logicAssemblies",scriptName+".dll");
			
			//options.TreatWarningsAsErrors = true;
			// Add any references you want the users to be able to access, be warned that giving them access to some classes can allow
			// harmful code to be written and executed. I recommend that you write your own Class library that is the only reference it allows
			// thus they can only do the things you want them to.
			// (though things like "System.Xml.dll" can be useful, just need to provide a way users can read a file to pass in to it)
			// Just to avoid bloatin this example to much, we will just add THIS program to its references, that way we don't need another
			// project to store the interfaces that both this class and the other uses. Just remember, this will expose ALL public classes to
			// the "script"
			options.ReferencedAssemblies.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
			// Compile our code
			var result = csProvider.CompileAssemblyFromSource(options, code);
			var assembly = result.CompiledAssembly;
			binaryAssembly = File.ReadAllBytes(result.PathToAssembly);
			if (result.Errors.HasErrors)
				throw new CompilationException("Unable to compile logic: " + result.Errors[0]);

			//foreach (var f in result.TempFiles)
			//{
			//	System.Console.WriteLine(f);
			//	File.Delete(f.ToString());
			//}
			//if (result.Errors.HasWarnings)
			//Log.Message("Warning while loading logic '" + ScriptName + "': " + FirstWarning);

			return assembly;
		}

		void CheckAssembly(out ConstructorInfo outConstructor)
		{
			HashSet<Type> checkedTypes = new HashSet<Type>() ;
			var a = Assembly;
			// Now that we have a compiled script, lets run them
			foreach (Type type in a.GetExportedTypes())
			{
				if (type.BaseType == typeof(EntityLogic))
				{
					if (!type.IsSerializable)
						throw new LogicCompositionException(ScriptName + ": Type '" + type + "' is entity logic, but not serializable");

					outConstructor = type.GetConstructor(Type.EmptyTypes);
					if (Constructor == null || !Constructor.IsPublic)
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

					if (outConstructor != null)
						return;
				}
			}
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

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("scriptName", ScriptName);
			info.AddValue("assembly", BinaryAssembly);
		}

		public CSLogicProvider(SerializationInfo info, StreamingContext context)
		{
			ScriptName = info.GetString("scriptName");
			BinaryAssembly = (byte[])info.GetValue("assembly", typeof(byte[]));
			Assembly = Assembly.Load(BinaryAssembly);
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
