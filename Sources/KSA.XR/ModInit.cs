using HarmonyLib;
using StarMap.API;
using System.Reflection;

namespace KSA.XR
{
	/// <summary>
	/// The mod's main entry point. This class sereves as the holder of state for the rest of the XR mod
	/// </summary>
	[StarMapMod]
	public class ModInit
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

			ui = new DebugUI();
		}

		[StarMapAfterGui]
		public void UIPulse(double dt)
		{
			ui?.StatusWindow();
		}

		[StarMapAfterOnFrame]
		public void OnFrame(double time, double dt)
		{
			ModInit.openxr?.OnFrame(0);
		}

		[StarMapUnload]
		public void Unload()
		{
			openxr?.Quit();
		}
	}
}
