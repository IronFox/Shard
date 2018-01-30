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
		private EntityLogic nestedLogic;
		private CSLogicProvider provider;
		private Constructor constructor;

		public CSLogicProvider Provider { get { return provider; } }

		class Constructor
		{
			Task task;
			CSLogicProvider provider;
			EntityLogic instance;
			public readonly string AssemblyName,LogicName;
			public readonly byte[] SerialData;
			public readonly object[] ConstructorParameters;

			public override string ToString()
			{
				if (SerialData != null)
					return provider != null ? provider.DeserializeLogic(SerialData).ToString() : AssemblyName + " [" + SerialData.Length + "]";
				return "new " + AssemblyName + "." + LogicName;
			}

			public Constructor(string assemblyName, byte[] data)
			{
				AssemblyName = assemblyName;
				SerialData = data;
				LogicName = null;

				task = Load();
			}
			public Constructor(string assemblyName, string logicName, object[] constructorParameters)
			{
				AssemblyName = assemblyName;
				SerialData = null;
				LogicName = logicName;
				ConstructorParameters = constructorParameters;
				if (constructorParameters != null)
				{
					int at = 0;
					foreach (var p in constructorParameters)
					{
						if (p != null)
						{
							if (!p.GetType().IsSerializable)
								throw new IntegrityViolation("Construction of "+assemblyName+"."+logicName+": Parameter #"+at+", type '"+p.GetType()+"' is not serializable");
						}
						at++;
					}
				}
				task = Load();
			}

			private async Task Load()
			{
				provider = await CSLogicProvider.AsyncFactory(AssemblyName);
				instance = SerialData != null ? provider.DeserializeLogic(SerialData) : provider.Instantiate(LogicName, ConstructorParameters);
			}

			public void Finish(DynamicCSLogic target, EntityID parent, TimeSpan timeout)
			{
				if (!task.Wait(timeout))
					throw new ExecutionException(parent, AssemblyName+": Failed to load/deserialize in "+timeout);
				target.provider = provider;
				target.nestedLogic = instance;
			}


		}

		public DynamicCSLogic(CSLogicProvider provider, string logicName, object[] constructorParameters)
		{
			nestedLogic = provider.Instantiate(logicName, constructorParameters);
			this.provider = provider;
		}

		private DynamicCSLogic(CSLogicProvider provider, EntityLogic newState)
		{
			if (newState.GetType().Assembly != provider.Assembly)
				throw new IntegrityViolation("Illegal state/provider combination given: "+newState.GetType()+"/"+provider);
			nestedLogic = newState;
			this.provider = provider;
		}

		public void FinishLoading(EntityID parent, TimeSpan timeout)
		{
			if (nestedLogic == null)
			{
				constructor.Finish(this, parent, timeout);
				constructor = null;
			}
		}

		protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource)
		{
			try
			{
				FinishLoading(currentState.ID,TimeSpan.FromMilliseconds(1));
				nestedLogic.Execute(ref actions, currentState, generation, randomSource);

				actions.ReplaceInstantiations(inst =>
				{
					if (inst.logic != null && !(inst.logic is DynamicCSLogic))
					{
						inst.logic = new DynamicCSLogic(provider, inst.logic);
					}
					return inst;
				});
			}
			catch (ExecutionException ex)
			{
				throw new ExecutionException(currentState.ID, provider.AssemblyName + "." + nestedLogic.GetType() + ": " + ex.Message, ex);
			}
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			if (constructor != null)
			{
				info.AddValue("assemblyName", constructor.AssemblyName);
				info.AddValue("state", constructor.SerialData);
				if (constructor.SerialData == null)
				{
					info.AddValue("logicName", constructor.LogicName);
					info.AddValue("constructorParameters", constructor.ConstructorParameters);
				}
				return;
			}
			info.AddValue("assemblyName", provider.AssemblyName);
			info.AddValue("state", Helper.SerializeToArray( nestedLogic ));
		}


		public DynamicCSLogic(SerializationInfo info, StreamingContext context)
		{
			string assemblyName = info.GetString("assemblyName");
			byte[] serialData = (byte[])info.GetValue("state", typeof(byte[]));
			if (serialData == null)
			{
				string logicName = info.GetString("logicName");
				object[] parameters = (object[])info.GetValue("constructorParameters", typeof(object[]));
				constructor = new Constructor(assemblyName, logicName, parameters);
			}
			else
				constructor = new Constructor(assemblyName,serialData);
		}

		public DynamicCSLogic(string assemblyName, string logicName, object[] constructorParameters)
		{
			constructor = new Constructor(assemblyName, logicName, constructorParameters);
		}

		public override string ToString()
		{
			return "Dynamic CS: " + (constructor != null ? constructor.ToString() : nestedLogic.ToString());
		}

	}

	[Serializable]
	public class CSLogicProvider : ISerializable
	{

		public static Func<string,Task<CSLogicProvider>> AsyncFactory { get; set; }


		//CompilerResults result;
		public readonly Assembly Assembly;
		public readonly byte[] BinaryAssembly;
		private readonly Dictionary<string,Type> types = new Dictionary<string, Type>();

		public readonly string AssemblyName,SourceCode;
		public readonly bool FromScript;

		public override bool Equals(object obj)
		{
			CSLogicProvider other = obj as CSLogicProvider;
			return other != null
					//&& other.assembly.Equals(assembly)
					&& Helper.AreEqual(other.BinaryAssembly,BinaryAssembly)
					&& AssemblyName == other.AssemblyName
					&& SourceCode == other.SourceCode;
		}



		public EntityLogic Instantiate(string logicName, object[] constructorParameters)
		{
			Type t = string.IsNullOrEmpty(logicName) && types.Count == 1 ? types.First().Value : types[logicName];

			var rs = Activator.CreateInstance(t, constructorParameters) as EntityLogic;

			if (rs == null)
			{
				List<string> types = new List<string>();
				for (int i = 0; i < Helper.Length(constructorParameters); i++)
					if (constructorParameters[i] != null)
						types.Add(constructorParameters[i].GetType().Name);
					else
						types.Add("null");
				string parameters = string.Join(",", types);

				throw new IntegrityViolation("Unable to find appropritate constructor for logic " + AssemblyName + "." + logicName+"("+parameters+")");
			}
			return rs;
		}

		public EntityLogic DeserializeLogic(byte[] serialData)
		{
			return (EntityLogic)Helper.Deserialize(serialData, Assembly, FromScript);
		}


		public CSLogicProvider(string assemblyName, string sourceCode, byte[] compiledAssembly)
		{
			FromScript = compiledAssembly == null || compiledAssembly.Length == 0;
			AssemblyName = assemblyName;
			SourceCode = sourceCode;
			BinaryAssembly = compiledAssembly;

			if (FromScript)
				Assembly = Compile(SourceCode, out BinaryAssembly);
			else
				Assembly = Assembly.Load(BinaryAssembly);

			CheckAssembly(types);
		}

		public CSLogicProvider(string assemblyName, byte[] binaryAssembly)
		{
			FromScript = false;
			AssemblyName = assemblyName;
			Assembly = Assembly.Load(binaryAssembly);
			this.BinaryAssembly = binaryAssembly;
			CheckAssembly(types);
		}


		public CSLogicProvider(string assemblyName, string code)
		{
			SourceCode = code;
			FromScript = true;
			AssemblyName = assemblyName;
			Assembly = Compile(code, out BinaryAssembly);
			CheckAssembly(types);
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
//			options.OutputAssembly = Path.Combine("logicAssemblies",assemblyName+".dll");
			
			//options.TreatWarningsAsErrors = true;
			// Add any references you want the users to be able to access, be warned that giving them access to some classes can allow
			// harmful code to be written and executed. I recommend that you write your own Class library that is the only reference it allows
			// thus they can only do the things you want them to.
			// (though things like "System.Xml.dll" can be useful, just need to provide a way users can read a file to pass in to it)
			// Just to avoid bloatin this example to much, we will just add THIS program to its references, that way we don't need another
			// project to store the interfaces that both this class and the other uses. Just remember, this will expose ALL public classes to
			// the "script"
			options.ReferencedAssemblies.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
			options.ReferencedAssemblies.Add(typeof(VectorMath.Vec3).Assembly.Location);
			options.ReferencedAssemblies.Add(typeof(EntityAppearance).Assembly.Location);
			// Compile our code
			var result = csProvider.CompileAssemblyFromSource(options, code);
			if (result.Errors.HasErrors)
				throw new CompilationException("Unable to compile logic: " + result.Errors[0]);
			var assembly = result.CompiledAssembly;
			binaryAssembly = File.ReadAllBytes(result.PathToAssembly);

			//foreach (var f in result.TempFiles)
			//{
			//	System.Console.WriteLine(f);
			//	File.Delete(f.ToString());
			//}
			//if (result.Errors.HasWarnings)
			//Log.Message("Warning while loading logic '" + ScriptName + "': " + FirstWarning);

			return assembly;
		}

		void CheckAssembly(Dictionary<string, Type> types)
		{
			HashSet<Type> checkedTypes = new HashSet<Type>() ;
			var a = Assembly;
			types.Clear();
			// Now that we have a compiled script, lets run them
			foreach (Type type in a.GetExportedTypes())
			{
				if (type.BaseType == typeof(EntityLogic))
				{

					var c = type.GetConstructor(Type.EmptyTypes);
					if (c == null || !c.IsPublic)
					{
						throw new LogicCompositionException(this, "Type '" + type + "' is entity logic, but has no public constructor with no arguments");
						//continue;
					}
					CheckType(checkedTypes, type, type.Name,true);

					types[type.Name] = type;
				}
			}
			if (types.Count == 0)
				throw new LogicCompositionException(this, "Failed to find any entity logic in assembly");
		}

		private void CheckType(HashSet<Type> checkedTypes, Type type, string path, bool requireSerializable)
		{
			if (checkedTypes.Contains(type))
				return;
			checkedTypes.Add(type);
			if (requireSerializable && !type.IsSerializable)
				throw new SerializationException(this,"Type '" + path + "' is entity logic or member, but not serializable");
			if (requireSerializable && type.GetInterfaces().Contains(typeof(ISerializable)))
				requireSerializable = false;

			var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			foreach (var f in fields)
			{
				string subPath = path + "." + f.Name;// + "(" + f.FieldType + ")";
				//if (!f.IsInitOnly)
					//throw new InvarianceViolation(AssemblyName + ": Field '" + subPath + "' must be declared readonly");
				Type sub = f.FieldType;
				//if (sub.IsClass || sub.IsStr)
					CheckType(checkedTypes, sub, subPath, requireSerializable);
			}
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("assemblyName", AssemblyName);
			info.AddValue("assembly", BinaryAssembly);
		}

		public override int GetHashCode()
		{
			var hashCode = -342463410;
			hashCode = hashCode * -1521134295 + EqualityComparer<byte[]>.Default.GetHashCode(BinaryAssembly);
			hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(AssemblyName);
			hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(SourceCode);
			return hashCode;
		}

		public CSLogicProvider(SerializationInfo info, StreamingContext context)
		{
			AssemblyName = info.GetString("assemblyName");
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
			private readonly CSLogicProvider provider;
			public LogicCompositionException(CSLogicProvider provider, string message) : base(message){ this.provider = provider; }

			public override string Message => provider.AssemblyName + ": "+ base.Message;
		}

		public class SerializationException : LogicCompositionException
		{

			public SerializationException(CSLogicProvider provider, string message) : base(provider,message)
			{
			}
		}
	}
}
