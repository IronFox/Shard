using Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Consensus
{

	[Serializable]
	public struct Configuration
	{
		public struct Member
		{
			public readonly int Identifier;
			public readonly bool CanBeLeader;

			public Member(int id, bool leaderCandidate)
			{
				Identifier = id;
				CanBeLeader = leaderCandidate;
			}

			public override string ToString()
			{
				return "m"+(CanBeLeader ? "l":"") + Identifier;
			}
		}

		public readonly Member[] Members;
		public int Size => Helper.Length(Members);
		public readonly string[] Revisions; //revision history from newest to oldest
		public string Revision => Revisions[0];
		public int Majority => Size / 2 + 1;

		public Configuration(string revision, IEnumerable<Member> memberIdentifiers) : this(new string[] { revision }, memberIdentifiers)
		{ }
		public Configuration(string[] revisions, IEnumerable<Member> memberIdentifiers)
		{
			Members = memberIdentifiers.ToArray();
			Revisions = revisions;
		}

		public bool IsDescendantOf(string rev) => Revisions.Contains(rev);
		public bool IsDescendantOf(Configuration config) => Revisions.Contains(config.Revision);

		internal bool ContainsIdentifier(int myIdentifier) => Members != null && Members.Any(m => m.Identifier == myIdentifier);
		internal bool ContainsIdentifier(Member member) => Members != null && Members.Any(m => m.Identifier == member.Identifier);

		internal bool ToIndex(Member m, out int linear)
		{
			return ToIndex(m.Identifier, out linear);
		}
		internal bool ToIndex(int identifier, out int linear)
		{
			for (int i = 0; i < Members.Length; i++)
				if (Members[i].Identifier == identifier)
				{
					linear = i;
					return true;
				}
			linear = -1;
			return false;
		}
	}


}