namespace KSA
{
	namespace XR
	{
		internal class Logger
		{
			private static Mutex logMut = new Mutex();
			private static void writeTag(string? extra = null)
			{
				var bgRestore = Console.BackgroundColor;
				var fgRestore = Console.ForegroundColor;

				Console.BackgroundColor = ConsoleColor.Black;
				Console.ForegroundColor = ConsoleColor.Magenta;
				Console.Write("[KSA_XR");
				if (extra != null)
					Console.Write($" {extra}");
				Console.Write("] ");
				Console.BackgroundColor = bgRestore;
				Console.ForegroundColor = fgRestore;
			}

			public static void warning(string warning, string? tag = null)
			{
				logMut.WaitOne();
				writeTag(tag);
				var bgRestore = Console.BackgroundColor;
				var fgRestore = Console.ForegroundColor;

				Console.BackgroundColor = ConsoleColor.Black;
				Console.ForegroundColor = ConsoleColor.Yellow;

				Console.WriteLine(warning);

				Console.BackgroundColor = bgRestore;
				Console.ForegroundColor = fgRestore;
				logMut.ReleaseMutex();
			}

			public static void error(string error, string? tag = null)
			{
				logMut.WaitOne();
				writeTag(tag);
				var bgRestore = Console.BackgroundColor;
				var fgRestore = Console.ForegroundColor;

				Console.BackgroundColor = ConsoleColor.Black;
				Console.ForegroundColor = ConsoleColor.Red;

				Console.WriteLine(error);

				Console.BackgroundColor = bgRestore;
				Console.ForegroundColor = fgRestore;
				logMut.ReleaseMutex();
			}


			public static void message(string message, string? tag = null)
			{
				logMut.WaitOne();
				writeTag(tag);
				Console.WriteLine(message);
				logMut.ReleaseMutex();
			}
		}
	}
}
