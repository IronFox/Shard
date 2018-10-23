using Base;
using System;
using System.Collections.Generic;

namespace Consensus
{
	public abstract class Identity : IEquatable<Identity>
	{
		public abstract Address PublicAddress { get; }
		public readonly Identity Parent;


		public override string ToString()
		{
			string rs = EndPoint;
			if (rs != "")
				rs += " ";
			if (Parent != null)
				return rs + Parent + "<->" + PublicAddress;
			return rs + PublicAddress.ToString();
		}

		public virtual string EndPoint => "";

		public Identity(Identity parent)
		{
			Parent = parent;
		}

		public override bool Equals(object obj)
		{
			return Equals(obj as Identity);
		}

		public bool Equals(Identity other)
		{
			return other != null &&
				   PublicAddress.Equals(other.PublicAddress);
		}

		public override int GetHashCode()
		{
			return -1984154133 + EqualityComparer<Address>.Default.GetHashCode(PublicAddress);
		}
	};


}