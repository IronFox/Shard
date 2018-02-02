using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
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

		protected override void Evolve(ref Actions actions, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges)
		{
			try
			{
				FinishLoading(currentState.ID,TimeSpan.FromMilliseconds(1));
				nestedLogic.Execute(ref actions, currentState, generation, randomSource,ranges);

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

		public static Func<string, Task<CSLogicProvider>> AsyncFactory { get; set; }


		public struct Dependency
		{
			public readonly string Name;
			public readonly Task<CSLogicProvider> Provider;

			public Dependency(string name, Task<CSLogicProvider> prov)
			{
				Name = name;
				Provider = prov;
			}

		}

		//CompilerResults result;
		public readonly Assembly Assembly;
		public readonly byte[] BinaryAssembly;
		public readonly Dependency[] Dependencies;
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
			if (types.Count == 0)
				throw new LogicCompositionException(this, "This assembly does not provide any logic");

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
			return (EntityLogic)Helper.Deserialize(serialData, EnumerateAssemblies(), FromScript);
		}

		private IEnumerable<Assembly> EnumerateAssemblies()
		{
			yield return Assembly;

			foreach (var d in Dependencies)
				yield return d.Provider.Get().Assembly;
		}




		private struct BinaryKey
		{
			public byte[] key;

			public BinaryKey(byte[] binaryAssembly) : this()
			{
				key = binaryAssembly;
			}

			public override bool Equals(object obj)
			{
				if (!(obj is BinaryKey))
					return false;
				BinaryKey other = (BinaryKey)obj;
				return Helper.AreEqual(key, other.key);
			}

			//http://www.java2s.com/Code/CSharp/Data-Types/Gethashcodeforabytearray.htm
			public override int GetHashCode()
			{
				if (key == null)
				{
					return 0;
				}

				int i = key.Length;
				int hc = i + 1;

				while (--i >= 0)
				{
					hc *= 257;
					hc ^= key[i];
				}

				return hc;
			}
		}

		private static Dictionary<BinaryKey, Assembly> loadedAssemblies = new Dictionary<BinaryKey, Assembly>();


		public static Assembly LoadAssembly(byte[] binaryAssembly)
		{
			var key = new BinaryKey(binaryAssembly);
			lock (loadedAssemblies)
			{
				Assembly a;
				if (loadedAssemblies.TryGetValue(key, out a))
					return a;
				a = AppDomain.CurrentDomain.Load(binaryAssembly);
				loadedAssemblies.Add(key, a);
				return a;
			}
		}

		public CSLogicProvider(string assemblyName, bool fromScript, Assembly assembly, byte[] binaryAssembly, Dependency[] dependencies)
		{
			FromScript = fromScript;
			AssemblyName = assemblyName;
			Assembly = assembly;
			BinaryAssembly = binaryAssembly;
			Dependencies = dependencies;
			CheckAssembly(types);
		}


		//public CSLogicProvider(string assemblyName, byte[] binaryAssembly, byte[][] dependencies)
		//{
		//	FromScript = false;
		//	AssemblyName = assemblyName;
		//	if (dependencies != null)
		//		foreach (var dep in dependencies)
		//			LoadAssembly(dep);
		//	Assembly = LoadAssembly(binaryAssembly);
		//	BinaryAssembly = binaryAssembly;
		//	CheckAssembly(types);
		//}


		public static async Task<CSLogicProvider> CompileAsync(string assemblyName, string assemblyCode)
		{
			var c = await CompileAsync(assemblyCode);
			return new CSLogicProvider(assemblyName, true, c.assembly, c.compiledAssembly, c.dependencies);
		}


		public static async Task<CSLogicProvider> LoadAsync(string assemblyName, string sourceCode, byte[] compiledAssembly, string[] dependencies)
		{
			bool fromScript = compiledAssembly == null || compiledAssembly.Length == 0;
			if (fromScript)
			{
				if (Helper.Length(dependencies) != 0)
					throw new IntegrityViolation("When compiling from code, dependencies must not be specified externally. Use '#reference [logic name]' instead");
				return await CompileAsync(assemblyName, sourceCode);
			}
			Dependency[] deps = dependencies?.Select(name => new Dependency(name, AsyncFactory(name))).ToArray();
			return new CSLogicProvider(assemblyName, false, LoadAssembly(compiledAssembly), compiledAssembly, deps);
		}


		public struct Compiled
		{
			public Assembly assembly;
			public byte[] compiledAssembly;
			public Dependency[] dependencies;
		}

		private static async Task<Compiled> CompileAsync(string code)
		{
			string[] lines = code.Split('\n');
			List<string> actualLines = new List<string>();
			List<string> dependenciesNames = new List<string>();
			List<CSLogicProvider> assemblies = new List<CSLogicProvider>();
			foreach (var l in lines)
			{
				string trimmed = l.Trim();
				if (trimmed.StartsWith("#reference"))
				{
					//# reference shared
					trimmed = trimmed.Remove(0, 10).Trim();
					dependenciesNames.Add(trimmed);
					var t = AsyncFactory(trimmed);
					assemblies.Add(await t);
				}
				else
					actualLines.Add(l);
			}
			code = string.Join("\n", actualLines);

			Assembly assembly = null;
			byte[] binary = null;
			await Task.Run(() =>
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
				foreach (var a in assemblies)
					options.ReferencedAssemblies.Add(a.Assembly.Location);

				var result = csProvider.CompileAssemblyFromSource(options, code);
				if (result.Errors.HasErrors)
					throw new CompilationException("Unable to compile logic: " + result.Errors[0]);
				assembly = result.CompiledAssembly;
				binary = File.ReadAllBytes(result.PathToAssembly);
				lock (loadedAssemblies)
					loadedAssemblies[new BinaryKey( binary )] = assembly;
				//foreach (var f in result.TempFiles)
				//{
				//	System.Console.WriteLine(f);
				//	File.Delete(f.ToString());
				//}
				//if (result.Errors.HasWarnings)
				//Log.Message("Warning while loading logic '" + ScriptName + "': " + FirstWarning);
			});
			Compiled rs = new Compiled();
			rs.assembly = assembly;
			rs.compiledAssembly = binary;
			rs.dependencies = new Dependency[assemblies.Count];
			for (int i = 0; i < assemblies.Count; i++)
			{
				var a = assemblies[i];
				rs.dependencies[i] = new Dependency(dependenciesNames[i], Task.Run(() => a));
			}
			return rs;
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
			//if (types.Count == 0)
				//throw new LogicCompositionException(this, "Failed to find any entity logic in assembly");
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
			info.AddValue("dependencies", Dependencies?.Select(d => d.Name).ToArray());
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
			Assembly = LoadAssembly(BinaryAssembly);
			var refs = (string[])info.GetValue("dependencies", typeof(string[]));
			if (refs != null)
			{
				Dependencies = new Dependency[refs.Length];
				for (int i = 0; i < refs.Length; i++)
					Dependencies[i] = new Dependency(refs[i], AsyncFactory(refs[i]));
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
