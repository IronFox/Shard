using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	public class SerialCSLogicProvider : DB.Entity
	{
		public byte[] compiledAssembly;
		public string sourceCode;

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
		}

		public CSLogicProvider Deserialize()
		{
			return new CSLogicProvider(_id, sourceCode, compiledAssembly);
		}
	}
}
