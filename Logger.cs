using System;
using System.Collections.Generic;
using System.Text;

namespace KSA_XR
{
	internal class Logger
	{
		private static void writeTag()
		{
			var bgRestore = Console.BackgroundColor;
			var fgRestore = Console.ForegroundColor;

			Console.BackgroundColor = ConsoleColor.Black;
			Console.ForegroundColor = ConsoleColor.Magenta;
			Console.Write("[KSA_XR] ");
			Console.BackgroundColor = bgRestore;
			Console.ForegroundColor = fgRestore;
		}

		public static void error(string error)
		{
			writeTag();
			var bgRestore = Console.BackgroundColor;
			var fgRestore = Console.ForegroundColor;

			Console.BackgroundColor = ConsoleColor.Black;
			Console.ForegroundColor = ConsoleColor.Red;

			Console.WriteLine(error);

			Console.BackgroundColor = bgRestore;
			Console.ForegroundColor = fgRestore;
		}


		public static void message(string message)
		{
			writeTag();
			Console.WriteLine(message);
		}
	}
}
