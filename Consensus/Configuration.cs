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

			public override bool Equals(object obj)
			{
				if (!(obj is Member))
				{
					return false;
				}

				var member = (Member)obj;
				return Identifier == member.Identifier &&
					   CanBeLeader == member.CanBeLeader;
			}

			public override int GetHashCode()
			{
				var hashCode = 1820678735;
				hashCode = hashCode * -1521134295 + Identifier.GetHashCode();
				hashCode = hashCode * -1521134295 + CanBeLeader.GetHashCode();
				return hashCode;
			}

			public override string ToString()
			{
				return "m"+(CanBeLeader ? "l":"") + Identifier;
			}

			public static bool operator ==(Member a, Member b)
			{
				return a.Identifier == b.Identifier && a.CanBeLeader == b.CanBeLeader;
			}
			public static bool operator !=(Member a, Member b)
			{
				return !(a == b);
			}


		}

		public readonly Member[] Members;
		public int Size => Helper.Length(Members);
		public int Majority => Size / 2 + 1;


		public Configuration(IEnumerable<Member> memberIdentifiers)
		{
			Members = memberIdentifiers.ToArray();
		}


		public bool ContainsIdentifier(int myIdentifier) => Members != null && Members.Any(m => m.Identifier == myIdentifier);
		public bool ContainsIdentifier(Member member) => Members != null && Members.Any(m => m.Identifier == member.Identifier);

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

		public override bool Equals(object obj)
		{
			if (!(obj is Configuration))
			{
				return false;
			}

			var configuration = (Configuration)obj;
			return this == configuration;
		}

		public override int GetHashCode()
		{
			return new Helper.HashCombiner(GetType()).Add(Members).GetHashCode();
		}

		public static bool operator ==(Configuration a, Configuration b)=>Helper.AreEqual(a.Members, b.Members);
		public static bool operator !=(Configuration a, Configuration b) => !(a == b);


		public override string ToString() => "{" + string.Join(",", Members.Select(m => m.ToString())) + "}";

	}


}