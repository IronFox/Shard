using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Base
{
	public static class Log
	{
		public static string Time
		{
			get
			{
				var t = Clock.Now;
				return t.ToLongTimeString() + "." + t.Millisecond;
			}
		}

		public static void Message(string msg)
		{
			msg = Time + ": " + msg;
			System.Diagnostics.Debug.WriteLine(msg);
			Console.WriteLine(msg);
		}

		public static void Debug(string msg)
		{
			System.Diagnostics.Debug.WriteLine(msg);
		}

		public static void Error(Exception ex)
		{
			var t = Time;
			System.Diagnostics.Debug.WriteLine(t + ": " + ex.Message);
			Console.Error.WriteLine(t + ": " + ex.ToString());
		}
		public static void Error(string msg)
		{
			msg = Time + ": " + msg;
			System.Diagnostics.Debug.WriteLine(msg);
			Console.Error.WriteLine(msg);
		}

		public static void Minor(string msg)
		{

		}
	}
}
