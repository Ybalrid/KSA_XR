using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Brutal.VulkanApi.Abstractions;
using Evergine.Bindings.OpenXR;

namespace KSA_XR
{
	[HarmonyPatch(typeof(VulkanHelpers))]
	[HarmonyPatch(nameof(VulkanHelpers.AddSurfaceExtensions))]
	public static class VulkanSurfaceExtensionPatch
	{
		static void Postfix(HashSet<string> __0)
		{
			Logger.message("Adding other Vulkan Instance Extensions after VulkanHelpers.AddSurfaceExtensions");
			VulkanExtensionPatchHelpers.InjectOpenXrInstanceExtensions(__0, "VulkanHelpers.AddSurfaceExtensions");
		}
	}

	[HarmonyPatch(typeof(Core.KSADeviceContextEx))]
	[HarmonyPatch("AddGlfwRequiredExtensions")]
	public static class VulkanGlfwExtensionPatch
	{
		static void Postfix(HashSet<string> __0)
		{
			Logger.message("Adding other Vulkan Instance Extensions in hashmap after KSADeviceContextEx.AddGlfwRequiredExtensions");
			VulkanExtensionPatchHelpers.InjectOpenXrInstanceExtensions(__0, "KSADeviceContextEx.AddGlfwRequiredExtensions");
		}
	}

	[HarmonyPatch(typeof(Core.KSADeviceContextEx))]
	[HarmonyPatch(MethodType.Constructor)]
	[HarmonyPatch(new[] {typeof(Brutal.VulkanApi.Abstractions.VulkanHelpers.Api)})]
	public static class VulkanKSADeviceContextExCtorPatch
	{
		static void Postfix(Core.KSADeviceContextEx __instance)
		{
			Logger.message("Postfix patch of Core.KSADeviceContextEx");
			var xr = ModLoader.openxr;
			if (xr == null)
				Logger.error("Vulkan has been initialized before OpenXR was successuflly initialized. This cannot work.");
			else
				VulkanExtensionPatchHelpers.ObtainAccessToVulkanContext(__instance, xr);
		}
	}

	[HarmonyPatch]
	public static class VulkanDeviceExtensionsBuilderPatch
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			static bool IsCandidate(MethodInfo m)
			{
				if (m.IsGenericMethod)
				{
					return false;
				}

				var p = m.GetParameters();
				if (p.Length != 1 || p[0].ParameterType != typeof(HashSet<string>))
				{
					return false;
				}

				var n = m.Name;
				return n.Contains("SwapchainExtensions") || n.Contains("DeviceExtensions");
			}

			var candidates = new[]
			{
				typeof(Core.KSADeviceContextEx),
				typeof(VulkanHelpers),
			}
			.SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
			.Where(IsCandidate)
			.Cast<MethodBase>()
			.ToArray();

			if (candidates.Length == 0)
			{
				Logger.error("VulkanDeviceExtensionsBuilderPatch: no builder methods found with HashSet<string> parameter.");
			}
			else
			{
				foreach (var c in candidates)
				{
					Logger.message($"VulkanDeviceExtensionsBuilderPatch: patching {c.DeclaringType?.FullName}.{c.Name}");
				}
			}

			return candidates;
		}

		static void Postfix(HashSet<string> __0, MethodBase __originalMethod)
		{
			VulkanExtensionPatchHelpers.InjectOpenXrDeviceExtensions(
				__0,
				$"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}");
		}
	}

	static class VulkanExtensionPatchHelpers
	{
		public static void InjectOpenXrInstanceExtensions(HashSet<string> extensions, string source)
		{
			try
			{
				var xr = ModLoader.openxr;
				if (xr == null)
				{
					Logger.warning($"{source}: OpenXR is not initialized yet.");
					return;
				}

				var required = xr.GetRequiredVulkanInstanceExtensions();
				if (required == null || required.Length == 0)
				{
					Logger.warning($"{source}: OpenXR returned no Vulkan instance extensions.");
					return;
				}

				foreach (var ext in required)
				{
					if (extensions.Add(ext))
					{
						Logger.message($"{source}: added OpenXR Vulkan instance extension '{ext}'.");
					}
					else
					{
						Logger.message($"{source}: {ext} was already present in the extension list we manipulate");
					}
				}
			}
			catch (System.Exception e)
			{
				Logger.error($"{source}: failed to inject OpenXR Vulkan instance extensions: {e}");
			}
		}

		public static void InjectOpenXrDeviceExtensions(HashSet<string> extensions, string source)
		{
			try
			{
				var xr = ModLoader.openxr;
				if (xr == null)
				{
					Logger.warning($"{source}: OpenXR is not initialized yet.");
					return;
				}

				var required = xr.GetRequiredVulkanDeviceExtensions();
				if (required == null || required.Length == 0)
				{
					Logger.warning($"{source}: OpenXR returned no Vulkan device extensions.");
					return;
				}

				foreach (var ext in required)
				{
					if (extensions.Add(ext))
					{
						Logger.message($"{source}: added OpenXR Vulkan device extension '{ext}'.");
					}
					else
					{
						Logger.message($"{source}: {ext} was already present in the extension list we manipulate");
					}
				}
			}
			catch (System.Exception e)
			{
				Logger.error($"{source}: failed to inject OpenXR Vulkan device extensions: {e}");
			}
		}

		public static void ObtainAccessToVulkanContext(Core.KSADeviceContextEx VulkanDeviceContext, OpenXR xrContext)
		{
			var rawInstance = VulkanDeviceContext.Instance.Handle.VkHandle;
			var rawDevice = VulkanDeviceContext.Device.Handle.VkHandle;
			var rawPhysicalDevice = VulkanDeviceContext.PhysicalDevice.Handle.VkHandle;
			var rawQueue = VulkanDeviceContext.Graphics.VkHandle.ToInt64();
			var queueFamilyIndex = VulkanDeviceContext.Graphics.Family;
			var queueIndex = VulkanDeviceContext.Graphics.Index;

			XrGraphicsBindingVulkanKHR vulkanContext = new XrGraphicsBindingVulkanKHR();
			vulkanContext.instance = rawInstance;
			vulkanContext.device = rawDevice;
			vulkanContext.physicalDevice = rawPhysicalDevice;
			vulkanContext.queueFamilyIndex = (uint) queueFamilyIndex;
			vulkanContext.queueIndex = (uint) queueIndex;

			xrContext.SetVulkanBinding(vulkanContext);

		}
	}
}
