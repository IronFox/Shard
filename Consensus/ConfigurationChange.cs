using System;

namespace Consensus
{
	[Serializable]
	internal class ConfigurationChange : ICommitable
	{
		public readonly Configuration NewCFG;

		public ConfigurationChange(Configuration cfg)
		{
			this.NewCFG = cfg;
		}


		public void Commit(Node node, CommitID myID)
		{
			node.Join(NewCFG);
		}
	}
}