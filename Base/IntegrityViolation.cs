using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	public class IntegrityViolation : Exception
	{
		public IntegrityViolation(string message) : base(message)
		{ }

	}

}
