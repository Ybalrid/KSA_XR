using System;
using System.Collections.Generic;
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
			//Make sure this is set
			vulkanContext.type = XrStructureType.XR_TYPE_GRAPHICS_BINDING_VULKAN_KHR;
			vulkanContextInfo = vulkanContext;
		}

		static ulong XR_MAKE_VERSION(ulong major, ulong minor, ulong patch)
		{
			return ((((major) & 0xffff) << 48) | (((minor) & 0xffff) << 32) | ((patch) & 0xffffffff));
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

		private unsafe string? PtrToString(byte* ptr)
		{
			return Marshal.PtrToStringUTF8((IntPtr)(ptr));
		}

		XrInstance instance;
		XrSession session;
		ulong systemId = 0;

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

		public OpenXR()
		{
			try
			{
				unsafe
				{
					GetListOfAvailableExtensions();

					CheckAvailableExtension(XR_KHR_VULKAN_ENABLE2_EXTENSION_NAME);
					CheckAvailableExtension(XR_KHR_VULKAN_ENABLE_EXTENSION_NAME);

					var instanceCreateInfo = new XrInstanceCreateInfo();
					instanceCreateInfo.type = XrStructureType.XR_TYPE_INSTANCE_CREATE_INFO;

					//TODO obtain version strings from the game?
					instanceCreateInfo.applicationInfo.apiVersion = XR_MAKE_VERSION(1, 0, 0);
					WriteStringToBuffer("BRUTAL", instanceCreateInfo.applicationInfo.engineName);
					WriteStringToBuffer("KittenSpaceAgency (KAS_XR mod)", instanceCreateInfo.applicationInfo.applicationName);

					//Need to enable the KHR_vulkan_enable2 extension

					var enabledExtensionsCS = new List<string>();
					enabledExtensionsCS.Add(XR_KHR_VULKAN_ENABLE_EXTENSION_NAME);
					enabledExtensionsCS.Add(XR_KHR_VULKAN_ENABLE2_EXTENSION_NAME);

					//TODO enable debug messenger

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

				}

			}
			catch (Exception e)
			{
				Logger.error(e.ToString());
			}
		}

		private unsafe void CheckAvailableExtension(string extName)
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
