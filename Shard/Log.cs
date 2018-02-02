using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard
{
	public static class Log
	{
		public static void Message(string msg)
		{
			System.Diagnostics.Debug.WriteLine(msg);
			Console.WriteLine(msg);
		}

		public static void Debug(string msg)
		{
			System.Diagnostics.Debug.WriteLine(msg);
		}

		public static void Error(Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(ex.Message);
			Console.Error.WriteLine(ex.ToString());
		}
		public static void Error(string msg)
		{
			System.Diagnostics.Debug.WriteLine(msg);
			Console.Error.WriteLine(msg);
		}

		public static void Minor(string msg)
		{
			
		}
	}
}
