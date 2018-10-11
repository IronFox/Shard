using System;

namespace Consensus
{
	[Serializable]
	public class Configuration
	{
		public Address[] Addresses { get; set; }
	}

	[Serializable]
	public class ConfigurationUpdate : Configuration, IDispatchable
	{
		public void Implement(Member owner)
		{
			owner.Join(this);
		}
	}
}