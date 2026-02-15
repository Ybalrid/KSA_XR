using StarMap.API;
using HarmonyLib;
using System.Reflection;

namespace KSA
{
	namespace XR
	{
		/// <summary>
		/// The mod's main entry point. This class sereves as the holder of state for the rest of the XR mod
		/// </summary>
		[StarMapMod]
		public class ModLoader
		{
			public static OpenXR? openxr;
			public static DebugUI? ui;
			public static Harmony harmony = new Harmony("KSA_XR.patches");


			[StarMapBeforeMain]
			public void preMain()
			{
				Logger.message("Loading KSA_XR");

				//It is very important to install the patches to KSA before we initialize anything else.
				harmony.PatchAll(Assembly.GetExecutingAssembly());
				Logger.message("Harmony patches have been installed.");
				openxr = new OpenXR();
				Logger.message("OpenXR 1st stage initialization done!");

				ui = new DebugUI();
			}

			[StarMapAfterGui]
			public void UIPulse(double dt)
			{
				ui?.StatusWindow();
			}

			[StarMapAfterOnFrame]
			public void OnFrame(double dta, double dtb) //Note: I found the function signature in StarMap's source code. I have not checked what these are, but they look close enough to frame timings lol
			{
				openxr?.OnFrame(dta);
			}
		}
	}
}
