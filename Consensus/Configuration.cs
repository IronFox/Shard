using System;
using System.Collections.Generic;
using System.Linq;

namespace Consensus
{
	[Serializable]
	public class Configuration
	{
		public Address[] Addresses { get; set; }

		public Configuration()
		{ }
		public Configuration(IEnumerable<Address> addresses)
		{
			Addresses = addresses.ToArray();
		}
	}

	[Serializable]
	internal class ConfigurationUpdate : Configuration, IDispatchable
	{

		public void OnArrive(Member receiver, Connection sender)
		{
			receiver.Join(this);
		}
	}
}