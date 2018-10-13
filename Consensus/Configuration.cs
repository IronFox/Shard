using System;
using System.Collections.Generic;
using System.Linq;

namespace Consensus
{

	public class Configuration
	{
		public Func<Address>[] Addresses { get; set; }
		public int Size => Addresses.Length;
		public int Majority => Size / 2 + 1;

		public Configuration()
		{ }
		public Configuration(IEnumerable<Address> addresses)
		{
			Addresses = addresses.Select<Address,Func<Address>>(addr => () => addr).ToArray();
		}
		public Configuration(IEnumerable<Func<Address>> addresses)
		{
			Addresses = addresses.ToArray();
		}
	}


}