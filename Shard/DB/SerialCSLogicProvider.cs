using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	public class SerialCSLogicProvider : DBType.Entity
	{
		public byte[] compiledAssembly;
		public string[] dependencies;
		public string sourceCode;

		public SerialCSLogicProvider()
		{ }

		public SerialCSLogicProvider(string assemblyName, string sourceCode)
		{
			_id = assemblyName;
			this.sourceCode = sourceCode;
		}
		public SerialCSLogicProvider(CSLogicProvider provider)
		{
			compiledAssembly = provider.BinaryAssembly;
			_id = provider.AssemblyName;
			sourceCode = provider.SourceCode;
			dependencies = provider.Dependencies.Select(dep => dep.Name).ToArray();
		}

		public Task<CSLogicProvider> DeserializeAsync()
		{
			return CSLogicProvider.LoadAsync(_id, sourceCode, compiledAssembly, dependencies);
		}
	}
}
