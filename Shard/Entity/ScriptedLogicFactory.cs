using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{

	public class ScriptedLogicFactory
	{
		CompilerResults result;

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
			var a = Assembly;
			// Now that we have a compiled script, lets run them
			foreach (Type type in a.GetExportedTypes())
			{
				if (type.BaseType == typeof(EntityLogic))
				{
					if (!type.IsSerializable)
						throw new ExecutionException(ScriptName + ": Type '"+type+"' is entity logic, but not serializable");

					ConstructorInfo constructor = type.GetConstructor(Type.EmptyTypes);
					if (constructor != null && constructor.IsPublic)
					{
						EntityLogic logic = constructor.Invoke(null) as EntityLogic;
						if (logic != null)
							return logic;
					}
				}
			}
			throw new ExecutionException(ScriptName+": Failed to find entity logic in assembly");
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
				throw new ExecutionException("Unable to load logic '"+scriptName+"': "+FirstError);

			if (result.Errors.HasWarnings)
			{
				Log.Message("Warning while loading logic '" + ScriptName + "': " + FirstWarning);
				// TODO: tell the user about the warnings, might want to prompt them if they want to continue
				// runnning the "script"
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
	}
}
