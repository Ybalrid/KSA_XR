
using StarMap.API;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using static KSA_XR.OpenXR.Loader;

namespace KSA_XR
{
	[StarMapMod]
	public class ModLoader
	{

		static List<string> openxrExtensions = null;

		[StarMapBeforeMain]
		public void preMain()
		{
			Console.WriteLine("[KSA_XR] preMain() code reached.");

			try
			{
				GetAvailableOpenXRExtensions();
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}

			if (openxrExtensions != null)
			{
				const string desiredExtension = "XR_KHR_vulkan_enable2";
				if (openxrExtensions.Contains(desiredExtension))
				{
					Console.WriteLine($"[KSA_XR] Desired OpenXR extension '{desiredExtension}' is available.");
				}
				else
				{
					Console.WriteLine($"[KSA_XR] Desired OpenXR extension '{desiredExtension}' is NOT available.");
				}
			}

		}

		private static void GetAvailableOpenXRExtensions()
		{
			//Attempt to init OpenXR loader with a test call
			OpenXR.Loader.xrGetInstanceProcAddr(IntPtr.Zero, "xrEnumerateInstanceExtensionProperties", out IntPtr pfn_xrEnumerateInstanceVersion);

			//TODO move this sort of code for low level wrangling outside of this class
			if (pfn_xrEnumerateInstanceVersion != IntPtr.Zero)
			{
				Console.WriteLine("[KSA_XR] Found pointer to xrEnumerateInstanceExtensionProperties. OpenXR Loader native library was loaded successfully.");
				var xrEnumerateInstanceExtensionProperties = Marshal.GetDelegateForFunctionPointer
					<OpenXR.Loader.xrEnumerateInstanceExtensionPropertiesDelegate>(pfn_xrEnumerateInstanceVersion);

				unsafe
				{
					uint count = 0;
					var res = xrEnumerateInstanceExtensionProperties(null, 0, &count, null);

					var props = stackalloc XrExtensionProperties[(int)count];
					for (int i = 0; i < count; i++)
					{
						props[i].Init();
					}

					res = xrEnumerateInstanceExtensionProperties(null, count, &count, props);

					if (res == XrResult.XR_SUCCESS)
					{
						openxrExtensions = new List<string>();
						Console.WriteLine($"[KSA_XR] xrEnumerateInstanceExtensionProperties succeeded, found {count} extensions:");
						for (int i = 0; i < count; i++)
						{
							var extName = Marshal.PtrToStringAnsi((IntPtr)props[i].extensionName);
							Console.WriteLine($"[KSA_XR] Extension {i}: {extName}, version {props[i].extensionVersion}");
							openxrExtensions.Add(extName);
						}
					}
					else
					{
						Console.WriteLine($"[KSA_XR] xrEnumerateInstanceExtensionProperties failed with result: {res}");

					}

				}
			}
		}
	}
}
