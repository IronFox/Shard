using Base;
using System;
using System.Collections.Generic;

namespace Consensus
{
	public class Identity : IEquatable<Identity>
	{
		public Func<Address> Address { get; set; }
		public readonly Identity Parent;


		public override string ToString()
		{
			string rs = EndPoint;
			if (rs != "")
				rs += " ";
			if (Parent != null)
				return rs + Parent + "<->" + Address();
			return rs + Address().ToString();
		}

		public virtual string EndPoint => "";

		public Identity(Identity parent, Func<Address> address)
		{
			Parent = parent;
			Address = address;
		}

		public override bool Equals(object obj)
		{
			return Equals(obj as Identity);
		}

		public bool Equals(Identity other)
		{
			return other != null &&
				   Address().Equals(other.Address());
		}

		public override int GetHashCode()
		{
			return -1984154133 + EqualityComparer<Address>.Default.GetHashCode(Address());
		}
	};


}