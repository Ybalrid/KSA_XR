using StarMap.API;
using HarmonyLib;
using System.Reflection;

namespace KSA_XR
{
	[StarMapMod]
	public class ModLoader
	{
		public static OpenXR? openxr;
		public static DebugUI? ui;

		public static Harmony harmony = new Harmony("KSA_XR.patches");
		[StarMapBeforeMain]
		public void preMain()
		{
			harmony.PatchAll(Assembly.GetExecutingAssembly());
			Logger.message("preMain() reached");
			openxr = new OpenXR();
			ui = new DebugUI();

			Logger.message("Initialized OpenXR. Querrying Vulkan Extensions required to communicate with runtime");
			Logger.message("Instance:");
			var vkInstanceExts = openxr.GetRequiredVulkanInstanceExtensions();
			if (vkInstanceExts != null)
			{
				foreach (var vkext in vkInstanceExts)
				{
					Logger.message($"\t- {vkext}");
				}
			}

			var vkDeviceExts = openxr.GetRequiredVulkanDeviceExtensions();
			Logger.message("Device:");
			if (vkDeviceExts != null)
			{
				foreach (var vkext in vkDeviceExts)
				{
					Logger.message($"\t- {vkext}");
				}
			}

		}


		[StarMapAfterGui]
		public void UIPulse(double dt)
		{
			ui.StatusWindow();
		}

		[StarMapAfterOnFrame]
		public void OnFrame(double dta, double dtb)
		{
			openxr.OnFrame(dta);
		}
	}
}
