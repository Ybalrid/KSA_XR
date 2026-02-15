using HarmonyLib;
using Brutal.VulkanApi.Abstractions;
using Evergine.Bindings.OpenXR;

namespace KSA
{
	namespace XR
	{
		[HarmonyPatch(typeof(VulkanHelpers))]
		[HarmonyPatch(nameof(VulkanHelpers.AddSurfaceExtensions))]
		internal static class VulkanSurfaceExtensionPatch
		{
			static void Postfix(HashSet<string> __0)
			{
				Logger.message("Adding other Vulkan Instance Extensions after VulkanHelpers.AddSurfaceExtensions");
				VulkanExtensionPatchHelpers.InjectOpenXrInstanceExtensions(__0, "VulkanHelpers.AddSurfaceExtensions");
			}
		}

		[HarmonyPatch(typeof(VulkanHelpers))]
		[HarmonyPatch(nameof(VulkanHelpers.AddSwapchainExtensions))]
		internal static class VulkanDeviceExtensionPatch
		{
			static void Postfix(HashSet<string> __0)
			{
				Logger.message("Adding other Vulkan Device Extensions after VulkanHelpers.AddSurfaceExtensions");
				VulkanExtensionPatchHelpers.InjectOpenXrDeviceExtensions(__0, "VulkanHelpers.AddSwapchainExtensions");
			}
		}

		[HarmonyPatch(typeof(Core.KSADeviceContextEx))]
		[HarmonyPatch(MethodType.Constructor)]
		[HarmonyPatch(new[] { typeof(Brutal.VulkanApi.Abstractions.VulkanHelpers.Api) })]
		internal static class VulkanKSADeviceContextExCtorPatch
		{
			static void Prefix(Brutal.VulkanApi.Abstractions.VulkanHelpers.Api __0)
			{
				Logger.message("Prefix patch of Core.KSADeviceContextEx");

				var xr = Init.openxr;
				if (xr != null)
					xr.DeclareUsedVulkanVersion(__0);
			}

			static void Postfix(Core.KSADeviceContextEx __instance)
			{
				Logger.message("Postfix patch of Core.KSADeviceContextEx");

				var xr = Init.openxr;
				if (xr == null)
					Logger.error("Vulkan has been initialized before OpenXR was successuflly initialized. This cannot work.");
				else
					VulkanExtensionPatchHelpers.ObtainAccessToVulkanContext(__instance, xr);
			}
		}

		[HarmonyPatch(typeof(Core.Renderer))]
		[HarmonyPatch("CreateGraphicsAndComputeQueue")]
		internal static class VulkanRendererCreateGraphicsAndComputerQueue
		{

			static void Prefix(Core.Renderer __instance)
			{
				Logger.message("Renderer CreateGraphicsAndCompute...");
			}

			static void Postfix(Core.Renderer __instance)
			{
				//NOTE: The enigne both initialize a "Graphics" and a "GraphicsAndCompute" queue.
				//I suppose that in most hardware, it's the same thing anyways.
				int index = __instance.GraphicsAndCompute.Index;
				int family = __instance.GraphicsAndCompute.Family;

				var xr = Init.openxr;
				if (xr != null)
					xr.SetQueue(index, family);
			}
		}

		static class VulkanExtensionPatchHelpers
		{
			internal static void InjectOpenXrInstanceExtensions(HashSet<string> extensions, string source)
			{
				try
				{
					var xr = Init.openxr;
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

			internal static void InjectOpenXrDeviceExtensions(HashSet<string> extensions, string source)
			{
				try
				{
					var xr = Init.openxr;
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

			internal static void ObtainAccessToVulkanContext(Core.KSADeviceContextEx VulkanDeviceContext, OpenXR xrContext)
			{
				XrGraphicsBindingVulkanKHR vulkanBindings = new XrGraphicsBindingVulkanKHR();
				vulkanBindings.type = XrStructureType.XR_TYPE_GRAPHICS_BINDING_VULKAN_KHR;
				vulkanBindings.instance = VulkanDeviceContext.Instance.Handle.VkHandle;
				vulkanBindings.device = VulkanDeviceContext.Device.Handle.VkHandle;
				vulkanBindings.physicalDevice = VulkanDeviceContext.PhysicalDevice.Handle.VkHandle;
				vulkanBindings.queueFamilyIndex = (uint)VulkanDeviceContext.Graphics.Family;
				vulkanBindings.queueIndex = (uint)VulkanDeviceContext.Graphics.Index;

				xrContext.SetVulkanBinding(vulkanBindings);
			}
		}
	}
}
