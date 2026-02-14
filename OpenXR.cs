using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Evergine.Bindings.OpenXR;
using static Evergine.Bindings.OpenXR.OpenXRNative;

namespace KSA_XR
{
	public class OpenXR
	{
		public readonly string RuntimeName;
		public readonly string SystemName;

		private XrGraphicsBindingVulkanKHR vulkanContextInfo;

		public void SetVulkanBinding(XrGraphicsBindingVulkanKHR vulkanContext)
		{
			vulkanContextInfo = vulkanContext;
		}

		public void SetQueue(int index, int family)
		{
			vulkanContextInfo.queueIndex = (uint)index;
			vulkanContextInfo.queueFamilyIndex = (uint)family;
		}

		static ulong XR_MAKE_VERSION(ulong major, ulong minor, ulong patch)
		{
			return ((((major) & 0xffff) << 48) | (((minor) & 0xffff) << 32) | ((patch) & 0xffffffff));
		}

		static ushort XR_VERSION_MAJOR(ulong version)
		{
			return (ushort)(((ulong)(version) >> 48) & 0xffff);
		}
		static ushort XR_VERSION_MINOR(ulong version)
		{
			return (ushort)(((ulong)(version) >> 32) & 0xffff);
		}

		static uint XR_VERSION_PATCH(ulong version)
		{
			return (uint)((ulong)(version) & 0xffffffff);
		}

		static unsafe void WriteStringToBuffer(string str, byte* buffer, int buffLen = 128)
		{
			int len = buffLen - 1 < str.Length ? buffLen : str.Length;

			for (int i = 0; i < len; ++i)
			{
				buffer[i] = (byte)str[i];
			}
			buffer[len] = 0;
		}

		private void CheckXRCall(XrResult result)
		{
			if (result != XrResult.XR_SUCCESS)
				throw new Exception($"OpenXR API call failed {result}");
		}

		private unsafe byte** BuildExtensionListPointer(List<string> extensions)
		{
			if (extensions.Count == 0)
				return null;

			var ptrArray = (byte**)Marshal.AllocHGlobal(sizeof(byte*) * extensions.Count);
			//The maximum len of a extension string is 128
			for (int i = 0; i < extensions.Count; ++i)
			{
				ptrArray[i] = (byte*)Marshal.AllocHGlobal(128);
				WriteStringToBuffer(extensions[i], ptrArray[i]);
			}

			return ptrArray;
		}

		private unsafe void FreeExtensionListPointer(byte** stringListPointers, int arrayLen)
		{
			for (int i = 0; i < arrayLen; ++i)
			{
				Marshal.FreeHGlobal((IntPtr)stringListPointers[i]);
			}

			Marshal.FreeHGlobal((IntPtr)stringListPointers);
		}

		private static unsafe string? PtrToString(byte* ptr)
		{
			return Marshal.PtrToStringUTF8((IntPtr)(ptr));
		}

		XrInstance instance;
		private XrSession session;
		ulong systemId = 0;
		public readonly XrViewConfigurationType viewConfigurationType;

		XrViewConfigurationView leftEyeView, rightEyeView;
		XrEnvironmentBlendMode blendModeToUse;

		List<string> runtimeAvailableOpenXRExtensions = new List<string>();

		private void GetListOfAvailableExtensions()
		{
			unsafe
			{
				uint count = 0;
				var result = xrEnumerateInstanceExtensionProperties(null, count, &count, null);
				CheckXRCall(result);

				var props = stackalloc XrExtensionProperties[(int)count];
				for (int i = 0; i < count; ++i)
				{
					props[i].type = XrStructureType.XR_TYPE_EXTENSION_PROPERTIES;
				}

				result = xrEnumerateInstanceExtensionProperties(null, count, &count, props);
				CheckXRCall(result);

				for (int i = 0; i < count; ++i)
				{
					var prop = props[i];
					var extName = Marshal.PtrToStringAnsi((IntPtr)prop.extensionName);
					if (extName == null)
						throw new Exception("exts must have name!");
					Logger.message($"\t- {extName} version {prop.extensionVersion}");
					runtimeAvailableOpenXRExtensions.Add(extName);
				}
			}
		}

		bool useDebugMessenger = false;


		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		public unsafe delegate XrBool32 DebugCallbackType(XrDebugUtilsMessageSeverityFlagsEXT severity, XrDebugUtilsMessageTypeFlagsEXT type, XrDebugUtilsMessengerCallbackDataEXT* data, void* userData);

		private DebugCallbackType? DebugCallbackObj;
		private GCHandle DebugMessengerHandle;
		private IntPtr DebugMessengerPtr = IntPtr.Zero;


		public static unsafe XrBool32 DebugCallback(XrDebugUtilsMessageSeverityFlagsEXT severity, XrDebugUtilsMessageTypeFlagsEXT type, XrDebugUtilsMessengerCallbackDataEXT* data, void* userData)
		{
			string message = $"{PtrToString(data->message)} {PtrToString(data->messageId)} {PtrToString(data->functionName)}";
			if (((ulong)severity & (ulong)XrDebugUtilsMessageSeverityFlagsEXT.XR_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT) != 0)
				Logger.error(message, "OpenXR Validation");
			else if (((ulong)severity & (ulong)XrDebugUtilsMessageSeverityFlagsEXT.XR_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT) != 0)
				Logger.warning(message, "OpenXR Validation");
			else
				Logger.message(message, "OpenXR Validation");
			return XR_FALSE;
		}

		XrDebugUtilsMessengerEXT DebugUtilsMessenger = new XrDebugUtilsMessengerEXT();

		public OpenXR()
		{
			try
			{
				unsafe
				{
					GetListOfAvailableExtensions();

					//CheckRequiredExtensionIsAvailable(XR_KHR_VULKAN_ENABLE2_EXTENSION_NAME);
					CheckRequiredExtensionIsAvailable(XR_KHR_VULKAN_ENABLE_EXTENSION_NAME);

#if DEBUG
					useDebugMessenger = CheckOptionalExtensionIsAvailable(XR_EXT_DEBUG_UTILS_EXTENSION_NAME);
#endif

					var instanceCreateInfo = new XrInstanceCreateInfo();
					instanceCreateInfo.type = XrStructureType.XR_TYPE_INSTANCE_CREATE_INFO;

					//TODO obtain version strings from the game?
					instanceCreateInfo.applicationInfo.apiVersion = XR_MAKE_VERSION(1, 0, 0);
					WriteStringToBuffer("BRUTAL", instanceCreateInfo.applicationInfo.engineName);
					WriteStringToBuffer("KittenSpaceAgency (KAS_XR mod)", instanceCreateInfo.applicationInfo.applicationName);

					//Need to enable the KHR_vulkan_enable2 extension

					var enabledExtensionsCS = new List<string>();
					enabledExtensionsCS.Add(XR_KHR_VULKAN_ENABLE_EXTENSION_NAME);
					//enabledExtensionsCS.Add(XR_KHR_VULKAN_ENABLE2_EXTENSION_NAME);
#if DEBUG
					if (useDebugMessenger)
						enabledExtensionsCS.Add(XR_EXT_DEBUG_UTILS_EXTENSION_NAME);
#endif

					instanceCreateInfo.enabledExtensionCount = (uint)enabledExtensionsCS.Count;
					instanceCreateInfo.enabledExtensionNames = BuildExtensionListPointer(enabledExtensionsCS);


					Logger.message("Attempt to create XrInstance with extensions:");
					foreach (string extensionName in enabledExtensionsCS)
						Logger.message($"\t- {extensionName}");


					XrInstance instance;
					var res = xrCreateInstance(&instanceCreateInfo, &instance);
					FreeExtensionListPointer(instanceCreateInfo.enabledExtensionNames, enabledExtensionsCS.Count);
					CheckXRCall(res);
					this.instance = instance;

					OpenXRNative.LoadFunctionPointers(instance);

					Logger.message($"Successfully created instance {this.instance.Handle} with all required extensions");


#if DEBUG
					if (useDebugMessenger)
					{
						var DebugUtilMessengerCreateInfo = new XrDebugUtilsMessengerCreateInfoEXT();
						DebugUtilMessengerCreateInfo.type = XrStructureType.XR_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT;

						DebugUtilMessengerCreateInfo.messageSeverities = 0x1111;
						DebugUtilMessengerCreateInfo.messageTypes = 1 | 2 | 4 | 8;

						DebugCallbackObj = DebugCallback;
						DebugMessengerPtr = Marshal.GetFunctionPointerForDelegate(DebugCallbackObj);
						DebugMessengerHandle = GCHandle.Alloc(DebugCallbackObj);

						DebugUtilMessengerCreateInfo.userCallback = DebugMessengerPtr;

						var DebugUtilsMessenger = new XrDebugUtilsMessengerEXT();
						var debugInstallResult = xrCreateDebugUtilsMessengerEXT(instance, &DebugUtilMessengerCreateInfo, &DebugUtilsMessenger);
						this.DebugUtilsMessenger = DebugUtilsMessenger;

					}
#endif

					XrInstanceProperties instanceProperties = new XrInstanceProperties();
					instanceProperties.type = XrStructureType.XR_TYPE_INSTANCE_PROPERTIES;

					res = xrGetInstanceProperties(instance, &instanceProperties);
					CheckXRCall(res);

					Logger.message($"Runtime is {PtrToString(instanceProperties.runtimeName)}");

					RuntimeName = PtrToString(instanceProperties.runtimeName);

					XrSystemGetInfo systemGetInfo = new XrSystemGetInfo();
					systemGetInfo.type = XrStructureType.XR_TYPE_SYSTEM_GET_INFO;
					systemGetInfo.formFactor = XrFormFactor.XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY;

					ulong systemId;
					CheckXRCall(xrGetSystem(instance, &systemGetInfo, &systemId));
					this.systemId = systemId;

					Logger.message($"Found an HMD Formfactor XR System on runtime (ID = {systemId})");

					var systemProperties = new XrSystemProperties();
					systemProperties.type = XrStructureType.XR_TYPE_SYSTEM_PROPERTIES;
					CheckXRCall(xrGetSystemProperties(instance, systemId, &systemProperties));

					Logger.message($"System Name: {PtrToString(systemProperties.systemName)}");
					SystemName = PtrToString(systemProperties.systemName);

					uint viewConfigCount = 0;
					xrEnumerateViewConfigurations(instance, systemId, viewConfigCount, &viewConfigCount, null);
					var viewConfigurationTypes = stackalloc XrViewConfigurationType[(int)viewConfigCount];
					xrEnumerateViewConfigurations(instance, systemId, viewConfigCount, &viewConfigCount, viewConfigurationTypes);
					viewConfigurationType = viewConfigurationTypes[0];
					Logger.message($"View configuration type {viewConfigurationType}");

					XrViewConfigurationProperties viewConfigurationProperties = new XrViewConfigurationProperties();
					viewConfigurationProperties.type = XrStructureType.XR_TYPE_VIEW_CONFIGURATION_PROPERTIES;
					CheckXRCall(xrGetViewConfigurationProperties(instance, systemId, viewConfigurationType, &viewConfigurationProperties));


					uint viewConfigViewCount = 0;
					xrEnumerateViewConfigurationViews(instance, systemId, viewConfigurationType, viewConfigViewCount, &viewConfigViewCount, null);
					var viewConfigViews = stackalloc XrViewConfigurationView[(int)viewConfigViewCount];
					for (int i = 0; i < viewConfigViewCount; ++i)
					{
						viewConfigViews[i].type = XrStructureType.XR_TYPE_VIEW_CONFIGURATION_VIEW;
					}
					xrEnumerateViewConfigurationViews(instance, systemId, viewConfigurationType, viewConfigViewCount, &viewConfigViewCount, viewConfigViews);

					//Should assert that we have at least 2 views (should also check that primary type is stereo then)
					leftEyeView = viewConfigViews[0];
					rightEyeView = viewConfigViews[1];

					Logger.message($"The view configuration is comprised of {viewConfigViewCount} views:");
					for (int i = 0; i < viewConfigViewCount; ++i)
					{
						Logger.message($"\tView config view #{i}");
						Logger.message($"\t\t- Recommended Width {viewConfigViews[i].recommendedImageRectWidth}");
						Logger.message($"\t\t- Recommended Height {viewConfigViews[i].recommendedImageRectHeight}");
						Logger.message($"\t\t- Recommnaded Sample count {viewConfigViews[i].recommendedSwapchainSampleCount}");
					}

					uint blendModeCount = 0;
					xrEnumerateEnvironmentBlendModes(instance, systemId, viewConfigurationType, blendModeCount, &blendModeCount, null);
					var envBlendModes = stackalloc XrEnvironmentBlendMode[(int)blendModeCount];
					xrEnumerateEnvironmentBlendModes(instance, systemId, viewConfigurationType, blendModeCount, &blendModeCount, envBlendModes);

					blendModeToUse = envBlendModes[0];

					//We should check the Vulkan version in use, strictly speaking.
					var graphicsRequirements = new XrGraphicsRequirementsVulkanKHR();
					graphicsRequirements.type = XrStructureType.XR_TYPE_GRAPHICS_REQUIREMENTS_VULKAN_KHR;
					CheckXRCall(xrGetVulkanGraphicsRequirementsKHR(instance, systemId, &graphicsRequirements));

					Logger.message($"Runtime requires vulkan minimum version {XR_VERSION_MAJOR(graphicsRequirements.minApiVersionSupported)}.{XR_VERSION_MINOR(graphicsRequirements.minApiVersionSupported)}.{XR_VERSION_PATCH(graphicsRequirements.minApiVersionSupported)}");
					Logger.message($"Runtime requires vulkan maximum version {XR_VERSION_MAJOR(graphicsRequirements.maxApiVersionSupported)}.{XR_VERSION_MINOR(graphicsRequirements.maxApiVersionSupported)}.{XR_VERSION_PATCH(graphicsRequirements.maxApiVersionSupported)}");

				}
			}
			catch (Exception e)
			{
				Logger.error(e.ToString());
			}
		}

		//TODO handle proper session state
		public bool TryStartSession()
		{
			try
			{
				var sessionCreateInfo = new XrSessionCreateInfo();
				sessionCreateInfo.type = XrStructureType.XR_TYPE_SESSION_CREATE_INFO;
				sessionCreateInfo.systemId = systemId;
				sessionCreateInfo.createFlags = (ulong)XrSessionCreateFlags.None;

				var graphicsBinding = new XrGraphicsBindingVulkanKHR();
				graphicsBinding.instance = vulkanContextInfo.instance;
				graphicsBinding.device = vulkanContextInfo.device;
				graphicsBinding.physicalDevice = vulkanContextInfo.physicalDevice;
				graphicsBinding.queueIndex = vulkanContextInfo.queueIndex;
				graphicsBinding.queueFamilyIndex = vulkanContextInfo.queueFamilyIndex;
				graphicsBinding.type = XrStructureType.XR_TYPE_GRAPHICS_BINDING_VULKAN_KHR;


				unsafe
				{
					IntPtr correctPhysicalDevice = Marshal.AllocHGlobal(sizeof(IntPtr));
					CheckXRCall(xrGetVulkanGraphicsDeviceKHR(instance, systemId, graphicsBinding.instance, correctPhysicalDevice));
					IntPtr selectedPhysicalDevice = *(IntPtr*)correctPhysicalDevice;

					if (selectedPhysicalDevice != graphicsBinding.physicalDevice)
					{
						Logger.error($"The device is different than expected got {selectedPhysicalDevice} wanted {graphicsBinding.physicalDevice}");
					}
					
					
					sessionCreateInfo.next = (void*)&graphicsBinding; //Khronos do love structure chains
					XrSession session = XrSession.Null;
					CheckXRCall(xrCreateSession(instance, &sessionCreateInfo, &session));
					this.session = session;
				}

				return true;
			}

			catch (Exception e)
			{
				Logger.error(e.ToString());
				return false;
			}

		}

		public void DestroySession()
		{
			xrDestroySession(session);
			//session.Handle = IntPtr.Zero;
		}

		private bool CheckOptionalExtensionIsAvailable(string extName)
		{
			return runtimeAvailableOpenXRExtensions.Contains(extName);
		}
		private unsafe void CheckRequiredExtensionIsAvailable(string extName)
		{
			if (!runtimeAvailableOpenXRExtensions.Contains(extName))
				throw new Exception($"A mandatory OpenXR extension is not available : {extName}");
		}

		public string[]? GetRequiredVulkanInstanceExtensions()
		{
			uint count = 0;

			unsafe
			{
				CheckXRCall(xrGetVulkanInstanceExtensionsKHR(instance, systemId, count, &count, null));
				var buffer = (byte*)Marshal.AllocHGlobal((int)count);
				CheckXRCall(xrGetVulkanInstanceExtensionsKHR(instance, systemId, count, &count, buffer));

				string? output = buffer != null ? PtrToString(buffer) : null;
				Marshal.FreeHGlobal((IntPtr)buffer);

				if (output != null)
				{
					return output.Split(" ");
				}
			}

			return null;
		}


		public string[]? GetRequiredVulkanDeviceExtensions()
		{
			uint count = 0;

			unsafe
			{
				CheckXRCall(xrGetVulkanDeviceExtensionsKHR(instance, systemId, count, &count, null));
				var buffer = (byte*)Marshal.AllocHGlobal((int)count);
				CheckXRCall(xrGetVulkanDeviceExtensionsKHR(instance, systemId, count, &count, buffer));

				string? output = buffer != null ? PtrToString(buffer) : null;
				Marshal.FreeHGlobal((IntPtr)buffer);

				if (output != null)
				{
					return output.Split(" ");
				}
			}

			return null;
		}

	}
}
