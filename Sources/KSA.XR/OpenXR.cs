using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Evergine.Bindings.OpenXR;
using System.Numerics;
using System.Runtime.InteropServices;
using static Evergine.Bindings.OpenXR.OpenXRNative;

namespace KSA.XR
{
	public class OpenXR
	{
		public enum EyeIndex
		{
			Left = 0,
			Right = 1,
		};

		string? runtimeName;
		string? systemName;
		public string? RuntimeName => runtimeName;
		public string? SystemName => systemName;


		#region OpenXR Version Macros (ported from C to C#)
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

		/// <summary>
		/// Packs the Kitten Space Agency version into a single unsigned integer value.
		///
		/// This format is required if we wish to push a version of the game towards
		/// the OpenXR runtime via the XrApplicationInfo structure.
		///
		/// KSA versionning scheme is a 4 number structure
		/// [Year].[Month].(Local Build Increment).[Git Revision Increment]
		///
		/// The game's version is effectively the [Git Revision Increment]. However
		/// it is nice to also have the time of release. The "Local Build Increment"
		/// seems to be effectively random as it depend on which RW build machine the
		/// build was from.
		///
		/// This function pack these numbers into 32 bit in the folling layout (MSB to LSB)
		///
		/// YYYYYYYYMMMMRRRRRRRRRRRRRRRRRRRR
		///
		/// The Year is only encoed on 8 bits, using 0 as year 2000.
		/// The Month is encoed onto 4 bits.
		/// The Revision number occupies the remaining 20 bits.
		///
		/// </summary>
		/// <param name="year">The major version component. Must be in the range 2000 to 2255, inclusive.</param>
		/// <param name="month">The minor version component. Must be in the range 0 to 15, inclusive.</param>
		/// <param name="revisionIncrement">The patch version component. Must be in the range 0 to 1,048,575, inclusive.</param>
		/// <returns>An unsigned integer that represents the packed version, combining the major, minor, and patch components.</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when the value of <paramref name="year"/> is less than 2000 or greater than 2255, <paramref
		/// name="month"/> is less than 0 or greater than 15, or <paramref name="revisionIncrement"/> is less than 0 or greater than
		/// 1,048,575.</exception>
		static uint PackVersion(int year, int month, int revisionIncrement)
		{
			year -= 2000;
			ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 0xFF); //2000 to 2255
			ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 0xF); // 0..15
			ArgumentOutOfRangeException.ThrowIfGreaterThan(revisionIncrement, 0xFFFFF); // 0..1048575

			return ((uint)year << 24) | ((uint)month << 20) | (uint)revisionIncrement;
		}


		#endregion

		#region Vulkan inputs from Brutal
		private uint VK_VERSION_MAJOR(VulkanHelpers.Api version)
		{
			return (uint)(version) >> 22;
		}

		private uint VK_VERSION_MINOR(VulkanHelpers.Api version)
		{
			return ((uint)(version) >> 12) & 0x3FF;
		}

		private uint VK_VERSION_PATCH(VulkanHelpers.Api version)
		{
			return (uint)(version) & 0xFFF;
		}

		private XrGraphicsBindingVulkanKHR vulkanContextInfo;
		VulkanHelpers.Api vulkanApiVersion;
		ulong vulkanOpenXRVersion;

		/// <summary>
		/// Sets the Vulkan API version to be used and verifies its compliance with the OpenXR runtime requirements.
		/// </summary>
		/// <remarks>
		/// This method **must** be called before the OpenXR session is created.
		/// This method checks the minimum and maximum Vulkan API versions supported by the OpenXR runtime
		/// and logs warnings if the declared version is outside these bounds. It is essential to ensure that the Vulkan
		/// version is compliant with the runtime's requirements to avoid potential compatibility issues.</remarks>
		/// <param name="version">The Vulkan API version to declare for use. Obtain it from the KSADeviceContext initialization</param>
		public void DeclareUsedVulkanVersion(VulkanHelpers.Api version)
		{
			vulkanApiVersion = version;

			Logger.message($"Vulkan {VK_VERSION_MAJOR(version)}.{VK_VERSION_MINOR(version)}.{VK_VERSION_PATCH(version)}");
			vulkanOpenXRVersion = XR_MAKE_VERSION(VK_VERSION_MAJOR(version), VK_VERSION_MINOR(version), VK_VERSION_PATCH(version));

			//We should check the Vulkan version in use, strictly speaking.
			var graphicsRequirements = new XrGraphicsRequirementsVulkanKHR();
			graphicsRequirements.type = XrStructureType.XR_TYPE_GRAPHICS_REQUIREMENTS_VULKAN_KHR;

			if (instance.Handle == 0)
				return;

			unsafe { CheckXRCall(xrGetVulkanGraphicsRequirementsKHR(instance, systemId, &graphicsRequirements)); }
			Logger.message($"Runtime requires vulkan minimum version {XR_VERSION_MAJOR(graphicsRequirements.minApiVersionSupported)}.{XR_VERSION_MINOR(graphicsRequirements.minApiVersionSupported)}.{XR_VERSION_PATCH(graphicsRequirements.minApiVersionSupported)}");
			Logger.message($"Runtime requires vulkan maximum version {XR_VERSION_MAJOR(graphicsRequirements.maxApiVersionSupported)}.{XR_VERSION_MINOR(graphicsRequirements.maxApiVersionSupported)}.{XR_VERSION_PATCH(graphicsRequirements.maxApiVersionSupported)}");

			if (vulkanOpenXRVersion >= graphicsRequirements.minApiVersionSupported)
				Logger.message("The requested version is compliant with the minimal version supported.");
			else
				Logger.error("The Vulkan version requested by the engine is inferior to the minimal version required by the OpenXR runtime.");

			if (vulkanOpenXRVersion > graphicsRequirements.maxApiVersionSupported)
			{
				Logger.warning("The Vulkan version requested by BRUTAL is above the maximum requried vesrion by the OpenXR runtime.");
				Logger.warning("Per OpenXR 1.0 specification §12.20 `XR_KHR_vulkan_enable`:");
				Logger.warning(" maximum Vulkan Instance API version that the runtime has been tested on and is known to support.", "OpenXR SPEC");
				Logger.warning(" Newer Vulkan Instance API versions might work if they are compatible.", "OpenXR SPEC");
				Logger.warning($"We proceed assuming version {VK_VERSION_MAJOR(version)}.{VK_VERSION_MINOR(version)} is compatible with Vulkan {XR_VERSION_MAJOR(graphicsRequirements.maxApiVersionSupported)}.{XR_VERSION_MINOR(graphicsRequirements.maxApiVersionSupported)}");
			}
		}

		public void SetVulkanBinding(XrGraphicsBindingVulkanKHR vulkanContext)
		{
			vulkanContextInfo = vulkanContext;
		}

		public void SetBrutalVulkan(Brutal.VulkanApi.Device device, Brutal.VulkanApi.Instance instance, Brutal.VulkanApi.Queue queue)
		{
			vkInstance = instance;
			vkDevice = device;
			vkQueue = queue;
		}

		public void SetQueue(int index, int family)
		{
			vulkanContextInfo.queueIndex = (uint)index;
			vulkanContextInfo.queueFamilyIndex = (uint)family;
		}
		#endregion

		#region C string manipulation
		/// <summary>
		/// Allows printing C strings
		/// </summary>
		/// <param name="ptr">A pointer to a null terminated string</param>
		/// <returns>A string, or null</returns>
		private static unsafe string? PtrToString(byte* ptr)
		{
			return Marshal.PtrToStringUTF8((IntPtr)(ptr));
		}

		static unsafe void WriteStringToBuffer(string str, byte* buffer, int buffLen = 128)
		{
			int len = buffLen - 1 < str.Length ? buffLen : str.Length;
			for (int i = 0; i < len; ++i)
				buffer[i] = (byte)str[i];
			buffer[len] = 0;
		}
		#endregion

		#region Extension loading support 
		/// <summary>
		/// Allocates and initializes an array of pointers to byte buffers, each containing the string representation of a
		/// specified file extension.
		/// </summary>
		/// <remarks>Each extension string is allocated a buffer of up to 128 bytes. The caller is responsible for
		/// freeing the allocated memory to prevent memory leaks.</remarks>
		/// <param name="extensions">A list of file extension strings to convert into byte buffer pointers. The list must contain at least one element.</param>
		/// <returns>A pointer to an array of byte pointers, where each pointer references a buffer containing a file extension string.
		/// Returns null if the input list is empty.</returns>
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

		/// <summary>
		/// Frees unmanaged memory allocated for an array of string pointers, including each individual string and the array
		/// itself.
		/// </summary>
		/// <remarks>Use this method to release memory previously allocated for a list of unmanaged strings,
		/// preventing memory leaks. This is required to clear the memeory from BuildExtensionListPointer
		///</remarks>
		/// <param name="stringListPointers">A pointer to an array of unmanaged memory addresses, each representing a string to be freed.</param>
		/// <param name="arrayLen">The number of string pointers in the array to be freed.</param>
		private unsafe void FreeExtensionListPointer(byte** stringListPointers, int arrayLen)
		{
			for (int i = 0; i < arrayLen; ++i)
				Marshal.FreeHGlobal((IntPtr)stringListPointers[i]);

			Marshal.FreeHGlobal((IntPtr)stringListPointers);
		}

		static List<string> mandatoryOpenXRExtensions = new List<string>
			{
				XR_KHR_VULKAN_ENABLE_EXTENSION_NAME
			};

		static List<string> optionalOpenXRExtensions = new List<string>
			{
				XR_EXT_HAND_INTERACTION_EXTENSION_NAME,
			};


		Dictionary<string, bool> enabledOptionalExtensions = new Dictionary<string, bool>();
		List<string> runtimeAvailableOpenXRExtensions = new List<string>();
		private unsafe void GetListOfAvailableExtensions()
		{
			uint count = 0;
			var result = xrEnumerateInstanceExtensionProperties(null, count, &count, null);
			CheckXRCall(result);

			var props = stackalloc XrExtensionProperties[(int)count];
			for (int i = 0; i < count; ++i)
				props[i].type = XrStructureType.XR_TYPE_EXTENSION_PROPERTIES;

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
		#endregion

		private XrInstance instance;
		public XrInstance Instance => instance;
		private XrSession session;
		public XrSession Session => session;

		private bool hasSessionBegan = false;
		XrSpace applicationLocalSpace;
		ulong systemId = 0;
		XrViewConfigurationType viewConfigurationType;
		public XrViewConfigurationType ViewConfigurationType => viewConfigurationType;
		XrViewConfigurationView[] eyeViewConfigurations = new XrViewConfigurationView[2];
		public XrViewConfigurationView[] EyeViewConfigurations => eyeViewConfigurations;
		XrFovf[] symetricalEyeFov = new XrFovf[2];
		public XrFovf[] SysmetricalEyeFov => symetricalEyeFov;

		XrEnvironmentBlendMode? blendModeToUse = null;

		List<VkFormat> compatibleSwapchainVulkanFormat = new List<VkFormat>();

		XrSwapchain[] eyeSwapchains = new XrSwapchain[2];
		List<XrSwapchainImageVulkanKHR>[] eyeSwapchainImages = new List<XrSwapchainImageVulkanKHR>[2];

		int2[] eyeRenderTargetSizes = new int2[2];
		XrPosef[] eyeViewPoses = new XrPosef[2];
		public XrPosef[] MostRecentEyeViewPoses => eyeViewPoses;
		XrView[] eyeViews = new XrView[2];
		public XrView[] EyeViews => eyeViews;

		long xrDisplayTime = 0;
		XrCompositionLayerProjectionView[] layerProjectionViews = new XrCompositionLayerProjectionView[2];

		VkCommandPool copyCommandPool;
		CommandBuffer copyCommandBuffer;
		VkFence copyFence;
		Brutal.VulkanApi.Instance? vkInstance;
		Brutal.VulkanApi.Device? vkDevice;
		Brutal.VulkanApi.Queue? vkQueue;

		KSA.Viewport?[] eyeViewport = new KSA.Viewport[2];
		KSA.Camera?[] eyeCameras = new KSA.Camera[2];

		#region OpenXR Debug Infrastructure
		bool useDebugMessenger = false;

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		public unsafe delegate XrBool32 DebugCallbackType(XrDebugUtilsMessageSeverityFlagsEXT severity, XrDebugUtilsMessageTypeFlagsEXT type, XrDebugUtilsMessengerCallbackDataEXT* data, void* userData);

		private DebugCallbackType? DebugCallbackObj;
		private GCHandle DebugMessengerHandle;
		private IntPtr DebugMessengerPtr = IntPtr.Zero;

		private static string MessageType(XrDebugUtilsMessageTypeFlagsEXT type)
		{
			switch (type)
			{
				case XrDebugUtilsMessageTypeFlagsEXT.XR_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT:
					return "General";
				case XrDebugUtilsMessageTypeFlagsEXT.XR_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT:
					return "Validation";
				case XrDebugUtilsMessageTypeFlagsEXT.XR_DEBUG_UTILS_MESSAGE_TYPE_PERFORMANCE_BIT_EXT:
					return "Performance";
				case XrDebugUtilsMessageTypeFlagsEXT.XR_DEBUG_UTILS_MESSAGE_TYPE_CONFORMANCE_BIT_EXT:
					return "Conformance";
			}

			return "Unkown";
		}

		public static unsafe XrBool32 DebugCallback(XrDebugUtilsMessageSeverityFlagsEXT severity, XrDebugUtilsMessageTypeFlagsEXT type, XrDebugUtilsMessengerCallbackDataEXT* data, void* userData)
		{
			string message = $"{PtrToString(data->message)} {PtrToString(data->messageId)} {PtrToString(data->functionName)}";
			if (((ulong)severity & (ulong)XrDebugUtilsMessageSeverityFlagsEXT.XR_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT) != 0)
				Logger.error(message, $"OpenXR {MessageType(type)}");
			else if (((ulong)severity & (ulong)XrDebugUtilsMessageSeverityFlagsEXT.XR_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT) != 0)
				Logger.warning(message, $"OpenXR {MessageType(type)}");
			else
				Logger.message(message, $"OpenXR {MessageType(type)}");
			return XR_FALSE;
		}

		XrDebugUtilsMessengerEXT DebugUtilsMessenger = new XrDebugUtilsMessengerEXT();
		#endregion

		Thread? openXREventThread;
		bool openXREventThreadRunning = true;


		/// <summary>
		/// Initializes a new instance of the OpenXR class and prepares the necessary components for OpenXR functionality.
		/// </summary>
		/// <remarks>This constructor attempts to create an OpenXR instance, retrieve the head-mounted display (HMD)
		/// system, and enumerate available views. If initialization fails, an error is logged. Ensure that the OpenXR
		/// runtime is properly installed and configured before creating an instance of this class.</remarks>
		public OpenXR()
		{
			try
			{
				CreateInstance();
				GetHMDSystem();
				EnumerateViews();
				StartOpenXREventThread();
			}
			catch (Exception e)
			{
				Logger.error("The first stage initialization of OpenXR failed.");
				Logger.error(e.ToString());
			}
		}

		private void StartOpenXREventThread()
		{
			openXREventThread = new Thread(HandleXREvents);
			openXREventThreadRunning = true;
			openXREventThread.Start();
		}

		private unsafe void HandleXREvents()
		{
			HashSet<XrResult> pollEventAllowedResults = new HashSet<XrResult>() { XrResult.XR_EVENT_UNAVAILABLE };
			while (openXREventThreadRunning)
			{
				try
				{
					var eventBuffer = new XrEventDataBuffer();
					eventBuffer.type = XrStructureType.XR_TYPE_EVENT_DATA_BUFFER;
					if (instance.Handle != 0 &&
						XrResult.XR_SUCCESS == CheckXRCall(xrPollEvent(instance, &eventBuffer), pollEventAllowedResults))
					{
						switch (eventBuffer.type)
						{
							case XrStructureType.XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED:
								var sessionStateChanged = *(XrEventDataSessionStateChanged*)&eventBuffer;
								Logger.message($"XR Session {sessionStateChanged.session.Handle} changed state to {sessionStateChanged.state}");
								break;
							default:
								Logger.warning($"XR Event of type {eventBuffer.type} currently unhandled");
								break;
						}
					}
				}
				catch (Exception e)
				{
					Logger.error(e.ToString());
				}

				Thread.Sleep(1);
			}
		}

		private unsafe void InstallDebugMessenger()
		{
#if DEBUG
			if (!useDebugMessenger)
				return;
			var DebugUtilMessengerCreateInfo = new XrDebugUtilsMessengerCreateInfoEXT();
			DebugUtilMessengerCreateInfo.type = XrStructureType.XR_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT;

			DebugUtilMessengerCreateInfo.messageSeverities = (ulong)XrDebugUtilsMessageSeverityFlagsEXT.XR_DEBUG_UTILS_MESSAGE_SEVERITY_INFO_BIT_EXT
				| (ulong)XrDebugUtilsMessageSeverityFlagsEXT.XR_DEBUG_UTILS_MESSAGE_SEVERITY_VERBOSE_BIT_EXT //may want to comment this one
				| (ulong)XrDebugUtilsMessageSeverityFlagsEXT.XR_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT
				| (ulong)XrDebugUtilsMessageSeverityFlagsEXT.XR_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT;
			DebugUtilMessengerCreateInfo.messageTypes = (ulong)XrDebugUtilsMessageTypeFlagsEXT.XR_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT
				| (ulong)XrDebugUtilsMessageTypeFlagsEXT.XR_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT
				| (ulong)XrDebugUtilsMessageTypeFlagsEXT.XR_DEBUG_UTILS_MESSAGE_TYPE_CONFORMANCE_BIT_EXT
				| (ulong)XrDebugUtilsMessageTypeFlagsEXT.XR_DEBUG_UTILS_MESSAGE_TYPE_CONFORMANCE_BIT_EXT;

			DebugCallbackObj = DebugCallback;
			DebugMessengerPtr = Marshal.GetFunctionPointerForDelegate(DebugCallbackObj);
			DebugMessengerHandle = GCHandle.Alloc(DebugCallbackObj);

			DebugUtilMessengerCreateInfo.userCallback = DebugMessengerPtr;

			var DebugUtilsMessenger = new XrDebugUtilsMessengerEXT();
			var debugInstallResult = xrCreateDebugUtilsMessengerEXT(instance, &DebugUtilMessengerCreateInfo, &DebugUtilsMessenger);
			this.DebugUtilsMessenger = DebugUtilsMessenger;
#endif
		}

		private XrFovf ComputeSymetricalFov(XrFovf leftEyeFov, XrFovf rightEyeFov)
		{
			// Match old Oculus logic in tangent space:
				// combinedTanHalfFovHorizontal = max(LeftTan, RightTan)
				// combinedTanHalfFovVertical   = max(UpTan, DownTan)
				// OpenXR uses signed radians where left/down are negative.
				var leftTan = MathF.Max(
					MathF.Tan(-leftEyeFov.angleLeft),
					MathF.Tan(-rightEyeFov.angleLeft));
				var rightTan = MathF.Max(
					MathF.Tan(leftEyeFov.angleRight),
					MathF.Tan(rightEyeFov.angleRight));
				var downTan = MathF.Max(
					MathF.Tan(-leftEyeFov.angleDown),
					MathF.Tan(-rightEyeFov.angleDown));
				var upTan = MathF.Max(
					MathF.Tan(leftEyeFov.angleUp),
					MathF.Tan(rightEyeFov.angleUp));

				var horizontalTan = MathF.Max(leftTan, rightTan);
				var verticalTan = MathF.Max(upTan, downTan);

				XrFovf symmetricFov = new XrFovf();
				symmetricFov.angleLeft = -MathF.Atan(horizontalTan);
				symmetricFov.angleRight = MathF.Atan(horizontalTan);
				symmetricFov.angleDown = -MathF.Atan(verticalTan);
				symmetricFov.angleUp = MathF.Atan(verticalTan);
				return symmetricFov;
		}

		private unsafe void EnumerateViews()
		{
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
				viewConfigViews[i].type = XrStructureType.XR_TYPE_VIEW_CONFIGURATION_VIEW;
			xrEnumerateViewConfigurationViews(instance, systemId, viewConfigurationType, viewConfigViewCount, &viewConfigViewCount, viewConfigViews);

			//Should assert that we have at least 2 views (should also check that primary type is stereo then)
			eyeViewConfigurations[0] = viewConfigViews[0];
			eyeViewConfigurations[1] = viewConfigViews[1];

			Logger.message($"The view configuration is comprised of {viewConfigViewCount} views:");
			for (int i = 0; i < viewConfigViewCount; ++i)
			{
				Logger.message($"\tView config view #{i}");
				Logger.message($"\t\t- Recommended Width {viewConfigViews[i].recommendedImageRectWidth}");
				Logger.message($"\t\t- Recommended Height {viewConfigViews[i].recommendedImageRectHeight}");
				Logger.message($"\t\t- Recommnaded Sample count {viewConfigViews[i].recommendedSwapchainSampleCount}");
			}

			//Enumerate environement blend modes, and make sure OPAQUE is supported, as we are a VR appication that take over the view of the user
			uint blendModeCount = 0;
			xrEnumerateEnvironmentBlendModes(instance, systemId, viewConfigurationType, blendModeCount, &blendModeCount, null);
			var envBlendModes = stackalloc XrEnvironmentBlendMode[(int)blendModeCount];
			xrEnumerateEnvironmentBlendModes(instance, systemId, viewConfigurationType, blendModeCount, &blendModeCount, envBlendModes);
			Logger.message("Supported Environement Blend Modes:");
			for (int i = 0; i < blendModeCount; ++i)
			{
				Logger.message($"\t- {envBlendModes[i]}");
				if (envBlendModes[i] == XrEnvironmentBlendMode.XR_ENVIRONMENT_BLEND_MODE_OPAQUE)
					blendModeToUse = envBlendModes[i];
			}

			if (blendModeToUse != null)
				Logger.message("Runtime supports opaque blend mode, this is the blend mode we will use.");
		}

		/// <summary>
		/// Creates and initializes a new OpenXR instance with the specified application information and enabled extensions.
		/// </summary>
		/// <remarks>This method configures the OpenXR instance with required and optional extensions based on the
		/// current application settings. Ensure that all necessary extensions are enabled before calling this method. If the
		/// instance creation fails, an exception is thrown.</remarks>
		/// <returns>A handle to the newly created OpenXR instance, which can be used for subsequent OpenXR API calls.</returns>
		private unsafe void CreateInstance()
		{
			XrInstance instance;
			WrangleOpenXRExtensions();
			var instanceCreateInfo = new XrInstanceCreateInfo();
			instanceCreateInfo.type = XrStructureType.XR_TYPE_INSTANCE_CREATE_INFO;

			var KSAversion = typeof(KSA.App).Assembly.GetName().Version;
			var BrutalVulkanVersion = typeof(Brutal.VulkanApi.Instance).Assembly.GetName().Version;

			instanceCreateInfo.applicationInfo.apiVersion = XR_MAKE_VERSION(1, 0, 0); //OPENXR_API_VERSION_1_0
			if (KSAversion != null)
				instanceCreateInfo.applicationInfo.applicationVersion = PackVersion(KSAversion.Major, KSAversion.Minor, KSAversion.Revision);
			if (BrutalVulkanVersion != null)
				instanceCreateInfo.applicationInfo.engineVersion = PackVersion(BrutalVulkanVersion.Major, BrutalVulkanVersion.Minor, BrutalVulkanVersion.Revision);
			WriteStringToBuffer("BRUTAL", instanceCreateInfo.applicationInfo.engineName);
			WriteStringToBuffer("KittenSpaceAgency (KAS_XR mod)", instanceCreateInfo.applicationInfo.applicationName);

			var enabledExtensionsCS = new List<string>();
			//The following extensions MUST be available, and have been checked before:
			enabledExtensionsCS.Add(XR_KHR_VULKAN_ENABLE_EXTENSION_NAME);
#if DEBUG
			if (useDebugMessenger)
				enabledExtensionsCS.Add(XR_EXT_DEBUG_UTILS_EXTENSION_NAME);
#endif
			//These are optional, and stored as a <string, bool> Dictionary.
			//If the bool was flagged true, it is legal to enable it.
			foreach (var extension in enabledOptionalExtensions)
			{
				if (extension.Value)
					enabledExtensionsCS.Add(extension.Key);
			}

			instanceCreateInfo.enabledExtensionCount = (uint)enabledExtensionsCS.Count;
			instanceCreateInfo.enabledExtensionNames = BuildExtensionListPointer(enabledExtensionsCS);

			Logger.message("Attempt to create XrInstance with extensions:");
			foreach (string extensionName in enabledExtensionsCS)
				Logger.message($"\t- {extensionName}");

			var res = xrCreateInstance(&instanceCreateInfo, &instance);
			FreeExtensionListPointer(instanceCreateInfo.enabledExtensionNames, enabledExtensionsCS.Count);
			CheckXRCall(res);
			OpenXRNative.LoadFunctionPointers(instance);
			Logger.message($"Successfully created instance {instance.Handle} with all required and optional extensions");
			this.instance = instance;
			InstallDebugMessenger();

			var instanceProperties = new XrInstanceProperties();
			instanceProperties.type = XrStructureType.XR_TYPE_INSTANCE_PROPERTIES;
			CheckXRCall(xrGetInstanceProperties(instance, &instanceProperties));
			runtimeName = PtrToString(instanceProperties.runtimeName);
		}

		/// <summary>
		/// Initializes and verifies the availability of mandatory and optional OpenXR extensions required for the
		/// application.
		/// </summary>
		/// <remarks>This method checks for the presence of mandatory extensions and adds available optional
		/// extensions to the enabled list. It also conditionally checks for the debug messenger extension based on the build
		/// configuration.</remarks>
		private void WrangleOpenXRExtensions()
		{
			GetListOfAvailableExtensions();

			foreach (string extension in mandatoryOpenXRExtensions)
				CheckRequiredExtensionIsAvailable(extension);

			foreach (string extension in optionalOpenXRExtensions)
				enabledOptionalExtensions.Add(extension, CheckOptionalExtensionIsAvailable(extension));

#if DEBUG //This extension lives outisde of the optional extension system, as it's enablement is guarded by build type, not runtime
			useDebugMessenger = CheckOptionalExtensionIsAvailable(XR_EXT_DEBUG_UTILS_EXTENSION_NAME);
#endif
		}

		/// <summary>
		/// Initializes and retrieves system information for the head-mounted display (HMD) XR system, including the system ID
		/// and system name.
		/// </summary>
		/// <remarks>This method must be called after the XR instance has been properly initialized. It updates the
		/// internal system ID and system name properties based on the current runtime's HMD system. If the XR runtime does
		/// not support an HMD form factor, this method may throw an exception from the underlying XR call.</remarks>
		private unsafe void GetHMDSystem()
		{
			XrSystemGetInfo systemGetInfo = new XrSystemGetInfo();
			systemGetInfo.type = XrStructureType.XR_TYPE_SYSTEM_GET_INFO;
			systemGetInfo.formFactor = XrFormFactor.XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY;
			ulong systemId;
			CheckXRCall(xrGetSystem(instance, &systemGetInfo, &systemId));

			Logger.message($"Found an HMD Formfactor XR System on runtime (ID = {systemId})");
			this.systemId = systemId;

			var systemProperties = new XrSystemProperties();
			systemProperties.type = XrStructureType.XR_TYPE_SYSTEM_PROPERTIES;
			CheckXRCall(xrGetSystemProperties(instance, systemId, &systemProperties));

			Logger.message($"System Name: {PtrToString(systemProperties.systemName)}");
			systemName = PtrToString(systemProperties.systemName);
		}

		public bool CreateSesionAndAllocateSwapchains(float pixelScale = 1f)
		{
			try
			{
				if (vkInstance == null)
					throw new Exception("Vulkan Instance not set");
				if (vkDevice == null)
					throw new Exception("Vulkan Device not set");
				if (vkQueue == null)
					throw new Exception("Vulkan Queue not set");

				if (instance.Handle == 0)
					throw new Exception("OpenXR instance not initialized");

				var sessionCreateInfo = new XrSessionCreateInfo();
				sessionCreateInfo.type = XrStructureType.XR_TYPE_SESSION_CREATE_INFO;
				sessionCreateInfo.systemId = systemId;
				sessionCreateInfo.createFlags = (ulong)XrSessionCreateFlags.None;

				var graphicsBinding = vulkanContextInfo;
				unsafe
				{
					IntPtr ptrToSelectedPhysicalDevice = Marshal.AllocHGlobal(sizeof(IntPtr));
					CheckXRCall(xrGetVulkanGraphicsDeviceKHR(instance, systemId, graphicsBinding.instance, ptrToSelectedPhysicalDevice));
					IntPtr selectedPhysicalDevice = *(IntPtr*)ptrToSelectedPhysicalDevice;
					Marshal.FreeHGlobal(ptrToSelectedPhysicalDevice);

					if (selectedPhysicalDevice != graphicsBinding.physicalDevice)
					{
						Logger.error($"The device is different than expected got {selectedPhysicalDevice} wanted {graphicsBinding.physicalDevice}");
						return false;
					}

					sessionCreateInfo.next = &graphicsBinding;//Khronos do love structure chains
					graphicsBinding.next = null;
					var session = new XrSession();
					CheckXRCall(xrCreateSession(instance, &sessionCreateInfo, &session));
					this.session = session;
					Logger.message($"Created OpenXR Session with handle {session.Handle}");

					//The fact that we have created an OpenXR session using the Instance/Device/Queue obtained from BRUTAL means that 
					//we can effectively do Vulkan related work

					//Allocate a separate command buffer pool for internal OpenXR work.
					var commandPoolCreateInfo = new VkCommandPoolCreateInfo();
					commandPoolCreateInfo.QueueFamilyIndex = (int)graphicsBinding.queueFamilyIndex;
					commandPoolCreateInfo.Flags = VkCommandPoolCreateFlags.ResetCommandBufferBit;
					copyCommandPool = VkDeviceExtensions.CreateCommandPool(vkDevice, in commandPoolCreateInfo, null);

					var commandBufferAllocateInfo = new VkCommandBufferAllocateInfo();
					commandBufferAllocateInfo.CommandPool = copyCommandPool;
					commandBufferAllocateInfo.Level = VkCommandBufferLevel.Primary;
					//Note: Need to use this overload as there are no way to set the CommandBufferCount parameter of the above struct without hacks (it's `internal`)
					Span<CommandBuffer> copyCommandbuffers = stackalloc CommandBuffer[1];
					DeviceExtensions.AllocateCommandBuffers(vkDevice, commandBufferAllocateInfo, copyCommandbuffers);
					copyCommandBuffer = copyCommandbuffers[0];

					var fenceCreateInfo = new VkFenceCreateInfo();
					copyFence = VkDeviceExtensions.CreateFence(vkDevice, in fenceCreateInfo, null);

					var sessionBeginInfo = new XrSessionBeginInfo();
					sessionBeginInfo.type = XrStructureType.XR_TYPE_SESSION_BEGIN_INFO;
					sessionBeginInfo.primaryViewConfigurationType = viewConfigurationType;

					CheckXRCall(xrBeginSession(session, &sessionBeginInfo));
					Logger.message("Session has begun");
					hasSessionBegan = true;

					//Define a global reference space that is LOCAL, as this will be intended as a seated/simulator experience
					XrReferenceSpaceCreateInfo referenceSpaceCreateInfo = new XrReferenceSpaceCreateInfo();
					referenceSpaceCreateInfo.type = XrStructureType.XR_TYPE_REFERENCE_SPACE_CREATE_INFO;
					referenceSpaceCreateInfo.referenceSpaceType = XrReferenceSpaceType.XR_REFERENCE_SPACE_TYPE_LOCAL;
					referenceSpaceCreateInfo.poseInReferenceSpace.orientation.w = 1;//With the rest of the stuct defaulted to zero, this is effectively a identity pose
					XrSpace applicationLocalSpace = new XrSpace();
					CheckXRCall(xrCreateReferenceSpace(session, &referenceSpaceCreateInfo, &applicationLocalSpace));
					this.applicationLocalSpace = applicationLocalSpace;

					compatibleSwapchainVulkanFormat.Clear();
					uint formatCount = 0;
					CheckXRCall(xrEnumerateSwapchainFormats(session, formatCount, &formatCount, null));
					var formats = stackalloc long[(int)formatCount];
					CheckXRCall(xrEnumerateSwapchainFormats(session, formatCount, &formatCount, formats));

					Logger.message("Runtime Swapchain compatible formats");
					for (int i = 0; i < formatCount; ++i)
					{
						var format = (VkFormat)formats[i];
						Logger.message($"\t- {format}");
						compatibleSwapchainVulkanFormat.Add(format);
					}

					var formatSelected = 0; //TODO choose a format from the list for real


					for (int eye = 0; eye < 2; ++eye)
					{
						var swapchainCreateInfo = new XrSwapchainCreateInfo();
						swapchainCreateInfo.type = XrStructureType.XR_TYPE_SWAPCHAIN_CREATE_INFO;
						swapchainCreateInfo.usageFlags = (ulong)XrSwapchainUsageFlags.XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT | (ulong)XrSwapchainUsageFlags.XR_SWAPCHAIN_USAGE_TRANSFER_DST_BIT;
						swapchainCreateInfo.mipCount = 1;
						swapchainCreateInfo.sampleCount = 1;
						swapchainCreateInfo.format = (long)compatibleSwapchainVulkanFormat[formatSelected];
						swapchainCreateInfo.faceCount = 1;
						swapchainCreateInfo.arraySize = 1;
						swapchainCreateInfo.width = (uint)(eyeViewConfigurations[eye].recommendedImageRectWidth * pixelScale);
						swapchainCreateInfo.height = (uint)(eyeViewConfigurations[eye].recommendedImageRectHeight * pixelScale);
						//Save the scaled render target size.
						eyeRenderTargetSizes[eye].X = (int)swapchainCreateInfo.width;
						eyeRenderTargetSizes[eye].Y = (int)swapchainCreateInfo.height;

						var swapchain = new XrSwapchain();
						CheckXRCall(xrCreateSwapchain(session, &swapchainCreateInfo, &swapchain));
						eyeSwapchains[eye] = swapchain;

						uint imageCount = 0;
						CheckXRCall(xrEnumerateSwapchainImages(swapchain, imageCount, &imageCount, null));
#pragma warning disable CA2014 // Do not use stackalloc in loops
						var swapchainImageVulkan = stackalloc XrSwapchainImageVulkanKHR[(int)imageCount];
#pragma warning restore CA2014 // Do not use stackalloc in loops
						for (int img = 0; img < imageCount; ++img)
							swapchainImageVulkan[img].type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_VULKAN_KHR;
						CheckXRCall(xrEnumerateSwapchainImages(swapchain, imageCount, &imageCount, (XrSwapchainImageBaseHeader*)swapchainImageVulkan));
						eyeSwapchainImages[eye] = new List<XrSwapchainImageVulkanKHR>();
						for (int img = 0; img < imageCount; ++img)
							eyeSwapchainImages[eye].Add(swapchainImageVulkan[img]);

						//TODO should be able to create image view and framebuffers from those images 

						Logger.message($"Allocated swapchain for eye {(EyeIndex)eye}: Format {compatibleSwapchainVulkanFormat[formatSelected]} Size {swapchainCreateInfo.width}x{swapchainCreateInfo.height}");

						eyeCameras[eye] = new Camera(new int2((int)swapchainCreateInfo.width, (int)swapchainCreateInfo.height));

					}
				}

				var eyeRenderTargetSize = eyeRenderTargetSizes[0];

				return true;
			}

			catch (Exception e)
			{
				Logger.error(e.ToString());
				return false;
			}
		}

		static HashSet<XrResult> waitSwapchainFailsafe = new HashSet<XrResult>
			{
				XrResult.XR_TIMEOUT_EXPIRED
			};


		bool frameInFlight = false;
		bool[] hasEye = { false, false };

		private void ResetRenderingStateTracking()
		{
			frameInFlight = hasEye[0] = hasEye[1] = false;
		}

		public unsafe void OnFrame(double time)
		{
			try
			{
				if (vkInstance == null)
					throw new Exception("Vulkan Instance not set");
				if (vkDevice == null)
					throw new Exception("Vulkan Device not set");
				if (vkQueue == null)
					throw new Exception("Vulkan Queue not set");

				if (hasSessionBegan)
				{
					if (XrViewports.Instance.CurrentRenderState == XrViewports.RenderHackPasses.NormalGame && frameInFlight && (hasEye[0] && hasEye[1]))
					{
						/*
						var tmp = layerProjectionViews[0].subImage.swapchain;
						layerProjectionViews[1].subImage.swapchain = layerProjectionViews[0].subImage.swapchain;
						layerProjectionViews[0].subImage.swapchain = tmp;
						*/
						fixed (XrCompositionLayerProjectionView* ptr = layerProjectionViews)
						{
							EndAndSubmitFrame((hasEye[0] && hasEye[1]) ? ptr : null, xrDisplayTime);
							frameInFlight = false;
							hasEye[0] = hasEye[1] = false;
						}
					}

					if (XrViewports.Instance.CurrentRenderState == XrViewports.RenderHackPasses.NormalGame && !frameInFlight)
					{
						var frameState = SynchronizeAndBeginFrame();
						xrDisplayTime = frameState.predictedDisplayTime;
						LocateViews(xrDisplayTime);
						//sync actions
						frameInFlight = true;
					}

					//Roll through each eye view, and progress through their swapchain images in the order the runtime requests
					if (XrViewports.Instance.CurrentRenderState == XrViewports.RenderHackPasses.XR && frameInFlight)
					{
						var eye = (int)XrViewports.Instance.CurrentXREye;
						var otherEye = eye == 0 ? 1 : 0;


#pragma warning disable CA2014 // Do not use stackalloc in loops

						uint index = 0xFFFFFFFF;
						//obtain swapchain image for current frame
						var swapchainImageAcquireInfo = new XrSwapchainImageAcquireInfo();
						swapchainImageAcquireInfo.type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO;
						CheckXRCall(xrAcquireSwapchainImage(eyeSwapchains[eye], &swapchainImageAcquireInfo, &index));

						var swapchainWaitInfo = new XrSwapchainImageWaitInfo();
						swapchainWaitInfo.type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO;
						swapchainWaitInfo.timeout = 1000; //We allow to wait up to 1000 nanoseconds for runtime to finish using swapchain image
						var waitResult = CheckXRCall(xrWaitSwapchainImage(eyeSwapchains[eye], &swapchainWaitInfo), waitSwapchainFailsafe);

						if (waitResult == XrResult.XR_SUCCESS)
						{
							var eyeSwpachainImage = eyeSwapchainImages[eye][(int)index];
							var eyeSwapchainVkImage = new VkImage(eyeSwpachainImage.image);
							var eyeSwapchainImageSize = new int2(eyeRenderTargetSizes[eye].X, eyeRenderTargetSizes[eye].Y);

							/*
							 * Now that all the boiler plate is put in place, it is time to start worrying about pushing pixels...!
							 *
							 * The above handle is a VkImage that is owned by the XR runtiem we have been "allowed to touch"
							 * for the duration of time between the successful call to xrWaitSwapchainImage, up to when we calle xrReleaseSwapchainImage()
							 *
							 * There are two options ahead:
							 *
							 *   1. We can make BRUTAL immediately render a viewport with the above image as the color attachment. (We also can (and should) also request 
							 *      the alloation of a depth texture from the OpenXR runtime. We can even submit it to the compositor. Oculus Time/Spwacewarp technology can 
							 *      make use of it for example).
							 *
							 *   2. We cannot do the above easilly, but we can get access to a VkImage onwed by BRUTAL, in that case we can use the runtime's image as a
							 *      TRANSFER_DST target, and push a copy command to the same Graphcis Queue that we gave the OpenXR runtime access. To do this properly,
							 *      we would need to change the Swapchain image alloation to already have the right memory layout (to avoid putting an extra barrier) and 
							 *      eat the cost of the extra VRAM and copy work. Not great, not terrible. Note: This also means that we need to create a command pool,
							 *      and allocate a couple of command buffer for this work prioro to entering this hot loop, I suppose.
							 *
							 * At the time of writing this, I have **no idea** how I am going to get images out of the rest of the game and into this framebuffer... ^^'
							 *
							 * Solution #1 is better technically, but the game engine is probably not able to provide these features without a lot of hacking, and I would try to keep
							 * the surface of mandatory runtime patches as small as possible. The least I do, the least it can break by a game update.
							 * 
							 * Solution #2 is actually pretty straightfoward as long as a KSA.Viewport is availble, and assuming the games renders it properly. 
							 * The main winodw is such Viewport.
							 * 
							 * As a POC the following obtains backbuffer of main game viewport, and just blit it as-is onto the OpenXR swapchain.
							 */

							//This is a placeholder so that we have *something* to display
							BlitMainViewIntoXrSwapchainImage(eyeSwapchainVkImage, eyeSwapchainImageSize);
							Logger.message($"success blit of image for eye {eye}");
						}
						else
						{
							//TODO how are we supposed to handle timeout when acquirering OpenXR swapchain image. Do we retry? Do we discard the frame?
						}

						//When we are finished with the image, we can release it so the runtime can start ingesting it
						var swapchainImageReleaseInfo = new XrSwapchainImageReleaseInfo();
						swapchainImageReleaseInfo.type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO;
						CheckXRCall(xrReleaseSwapchainImage(eyeSwapchains[eye], &swapchainImageReleaseInfo));

						XrCompositionLayerProjectionView layer = new XrCompositionLayerProjectionView();
						layer.type = XrStructureType.XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW;
						layer.pose = EyeViews[eye].pose;
						
						if(ModInit.ui.TestBool)
							layer.fov = EyeViews[eye].fov; //TODO it is porbable that we cound fudge this if we cannot coherce the engine into rendering a asymetrical frustrum 
						else
							layer.fov = symetricalEyeFov[eye];

						layer.subImage.swapchain = eyeSwapchains[eye];
						layer.subImage.imageRect.extent.width = eyeRenderTargetSizes[eye].X;
						layer.subImage.imageRect.extent.height = eyeRenderTargetSizes[eye].Y;

						
						layerProjectionViews[eye] = layer;

						hasEye[eye] = true;
					}

#pragma warning restore CA2014 // Do not use stackalloc in loops

					XrViewports.Instance.RenderFinished();
				}
			}
			catch (Exception e)
			{
				Logger.error(e.ToString());
			}
		}

		private unsafe XrFrameState SynchronizeAndBeginFrame()
		{
			var frameState = new XrFrameState();
			frameState.type = XrStructureType.XR_TYPE_FRAME_STATE;

			var waitFrameInfo = new XrFrameWaitInfo();
			waitFrameInfo.type = XrStructureType.XR_TYPE_FRAME_WAIT_INFO;
			CheckXRCall(xrWaitFrame(session, &waitFrameInfo, &frameState));
			Logger.message("xrWaitFrame");

			var frameBeginInfo = new XrFrameBeginInfo();
			frameBeginInfo.type = XrStructureType.XR_TYPE_FRAME_BEGIN_INFO;
			CheckXRCall(xrBeginFrame(session, &frameBeginInfo));
			Logger.message("xrBeginFrame");
			return frameState;
		}

		private unsafe void EndAndSubmitFrame(XrCompositionLayerProjectionView* projectionLayerViews, long displayTime)
		{
			//We render VR, perspective projections that goes right in front of your eyes
			XrCompositionLayerProjection layerProjection = new XrCompositionLayerProjection();
			layerProjection.type = XrStructureType.XR_TYPE_COMPOSITION_LAYER_PROJECTION;
			layerProjection.space = applicationLocalSpace;
			layerProjection.viewCount = 2;
			layerProjection.views = projectionLayerViews;
			var layers = stackalloc XrCompositionLayerProjection*[1];
			layers[0] = &layerProjection;

			var frameEndInfo = new XrFrameEndInfo();
			frameEndInfo.type = XrStructureType.XR_TYPE_FRAME_END_INFO;
			if (projectionLayerViews != null)
				frameEndInfo.layerCount = 1;
			else
				frameEndInfo.layerCount = 0;

			frameEndInfo.layers = (XrCompositionLayerBaseHeader**)layers;
			frameEndInfo.environmentBlendMode = XrEnvironmentBlendMode.XR_ENVIRONMENT_BLEND_MODE_OPAQUE;
			frameEndInfo.displayTime = displayTime;
			CheckXRCall(xrEndFrame(session, &frameEndInfo));
			Logger.message("xrEndFrame");
		}

		private unsafe void LocateViews(long displayTime)
		{
			//locate views
			var viewState = new XrViewState();
			viewState.type = XrStructureType.XR_TYPE_VIEW_STATE;
			var views = stackalloc XrView[2];
			views[0].type = XrStructureType.XR_TYPE_VIEW;
			views[1].type = XrStructureType.XR_TYPE_VIEW;
			uint viewCount = 0;
			var viewLocateInfo = new XrViewLocateInfo();
			viewLocateInfo.type = XrStructureType.XR_TYPE_VIEW_LOCATE_INFO;
			viewLocateInfo.displayTime = displayTime;
			viewLocateInfo.space = applicationLocalSpace;
			viewLocateInfo.viewConfigurationType = XrViewConfigurationType.XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
			CheckXRCall(xrLocateViews(session, &viewLocateInfo, &viewState, 2, &viewCount, views));
			Logger.message("xrLocateViews");

				var sharedSymmetricFov = ComputeSymetricalFov(views[0].fov, views[1].fov);
				symetricalEyeFov[0] = sharedSymmetricFov;
				symetricalEyeFov[1] = sharedSymmetricFov;

			for (int i = 0; i < viewCount; ++i)
			{
				eyeViews[i] = views[i];
				var view = views[i];
				var eye = (EyeIndex)i;
				eyeViewPoses[i] = new XrPosef();
				if (0 != (viewState.viewStateFlags & (ulong)(XrViewStateFlags.XR_VIEW_STATE_POSITION_VALID_BIT | XrViewStateFlags.XR_VIEW_STATE_POSITION_TRACKED_BIT)))
				{
					float3 positionVector = new float3(view.pose.position.x, view.pose.position.y, view.pose.position.z);
					eyeViewPoses[i].position = view.pose.position;
				}

				if (0 != (viewState.viewStateFlags & (ulong)(XrViewStateFlags.XR_VIEW_STATE_ORIENTATION_VALID_BIT | XrViewStateFlags.XR_VIEW_STATE_ORIENTATION_TRACKED_BIT)))
				{
					var quaternion = new Quaternion(view.pose.orientation.x, view.pose.orientation.y, view.pose.orientation.z, view.pose.orientation.w);
					eyeViewPoses[i].orientation = view.pose.orientation;
				}
			}

		}

		/// <summary>
		/// Copies the contents of the main view's offscreen render target into the specified Vulkan swapchain image for XR
		/// rendering.
		/// </summary>
		/// <remarks>This method performs Vulkan image memory barrier transitions to ensure correct image layouts for
		/// the blit operation. The caller must ensure that the offscreen target is valid before invoking this
		/// method.</remarks>
		/// <param name="eyeSwapchainVkImage">The Vulkan image representing the destination swapchain target for the XR eye view.</param>
		/// <param name="eyeSwapchainImageSize">The dimensions of the destination swapchain image, specified as an integer vector.</param>
		/// <exception cref="Exception">Thrown if the offscreen render target cannot be acquired as the source for the copy operation.</exception>
		private unsafe void BlitMainViewIntoXrSwapchainImage(VkImage eyeSwapchainVkImage, int2 eyeSwapchainImageSize)
		{
			var pinstance = Program.Instance;
			var viewport = Program.MainViewport;
			var target = viewport.OffscreenTarget;
			if (target == null)
				throw new Exception("Cannot acquire offscreen target for copy source");
			var sourceImage = target.ColorImage.Image;
			var srcSize = viewport.Size;

			var originalCamera = viewport.BaseCamera;

			//TODO All of those vulkan structures can probably be allocated just once, to put them outisde this hot path
			var sourceToTransferBarrier = new VkImageMemoryBarrier();
			sourceToTransferBarrier.SrcAccessMask = VkAccessFlags.ColorAttachmentWriteBit;
			sourceToTransferBarrier.DstAccessMask = VkAccessFlags.TransferReadBit;
			sourceToTransferBarrier.OldLayout = VkImageLayout.ColorAttachmentOptimal;
			sourceToTransferBarrier.NewLayout = VkImageLayout.TransferSrcOptimal;
			sourceToTransferBarrier.SrcQueueFamilyIndex = -1;
			sourceToTransferBarrier.DstQueueFamilyIndex = -1;
			sourceToTransferBarrier.Image = sourceImage;
			sourceToTransferBarrier.SubresourceRange.AspectMask = VkImageAspectFlags.ColorBit;
			sourceToTransferBarrier.SubresourceRange.BaseMipLevel = 0;
			sourceToTransferBarrier.SubresourceRange.LevelCount = 1;
			sourceToTransferBarrier.SubresourceRange.BaseArrayLayer = 0;
			sourceToTransferBarrier.SubresourceRange.LayerCount = 1;

			var destToTransferBarrier = new VkImageMemoryBarrier();
			destToTransferBarrier.SrcAccessMask = VkAccessFlags.None;
			destToTransferBarrier.DstAccessMask = VkAccessFlags.TransferWriteBit;
			destToTransferBarrier.OldLayout = VkImageLayout.ColorAttachmentOptimal;
			destToTransferBarrier.NewLayout = VkImageLayout.TransferDstOptimal;
			destToTransferBarrier.SrcQueueFamilyIndex = -1;
			destToTransferBarrier.DstQueueFamilyIndex = -1;
			destToTransferBarrier.Image = eyeSwapchainVkImage;
			destToTransferBarrier.SubresourceRange.AspectMask = VkImageAspectFlags.ColorBit;
			destToTransferBarrier.SubresourceRange.BaseMipLevel = 0;
			destToTransferBarrier.SubresourceRange.LevelCount = 1;
			destToTransferBarrier.SubresourceRange.BaseArrayLayer = 0;
			destToTransferBarrier.SubresourceRange.LayerCount = 1;

			var sourceBackToColorBarrier = new VkImageMemoryBarrier();
			sourceBackToColorBarrier.SrcAccessMask = VkAccessFlags.TransferReadBit;
			sourceBackToColorBarrier.DstAccessMask = VkAccessFlags.ColorAttachmentWriteBit;
			sourceBackToColorBarrier.OldLayout = VkImageLayout.TransferSrcOptimal;
			sourceBackToColorBarrier.NewLayout = VkImageLayout.ColorAttachmentOptimal;
			sourceBackToColorBarrier.SrcQueueFamilyIndex = -1;
			sourceBackToColorBarrier.DstQueueFamilyIndex = -1;
			sourceBackToColorBarrier.Image = sourceImage;
			sourceBackToColorBarrier.SubresourceRange.AspectMask = VkImageAspectFlags.ColorBit;
			sourceBackToColorBarrier.SubresourceRange.BaseMipLevel = 0;
			sourceBackToColorBarrier.SubresourceRange.LevelCount = 1;
			sourceBackToColorBarrier.SubresourceRange.BaseArrayLayer = 0;
			sourceBackToColorBarrier.SubresourceRange.LayerCount = 1;

			var destToColorBarrier = new VkImageMemoryBarrier();
			destToColorBarrier.SrcAccessMask = VkAccessFlags.TransferWriteBit;
			destToColorBarrier.DstAccessMask = VkAccessFlags.MemoryReadBit;
			destToColorBarrier.OldLayout = VkImageLayout.TransferDstOptimal;
			destToColorBarrier.NewLayout = VkImageLayout.ColorAttachmentOptimal;
			destToColorBarrier.SrcQueueFamilyIndex = -1;
			destToColorBarrier.DstQueueFamilyIndex = -1;
			destToColorBarrier.Image = eyeSwapchainVkImage;
			destToColorBarrier.SubresourceRange.AspectMask = VkImageAspectFlags.ColorBit;
			destToColorBarrier.SubresourceRange.BaseMipLevel = 0;
			destToColorBarrier.SubresourceRange.LevelCount = 1;
			destToColorBarrier.SubresourceRange.BaseArrayLayer = 0;
			destToColorBarrier.SubresourceRange.LayerCount = 1;

			var blitRegion = new VkImageBlit();
			blitRegion.SrcSubresource.AspectMask = VkImageAspectFlags.ColorBit;
			blitRegion.SrcSubresource.MipLevel = 0;
			blitRegion.SrcSubresource.BaseArrayLayer = 0;
			blitRegion.SrcSubresource.LayerCount = 1;
			blitRegion.DstSubresource.AspectMask = VkImageAspectFlags.ColorBit;
			blitRegion.DstSubresource.MipLevel = 0;
			blitRegion.DstSubresource.BaseArrayLayer = 0;
			blitRegion.DstSubresource.LayerCount = 1;
			blitRegion.SrcOffsets[0] = new VkOffset3D();
			blitRegion.SrcOffsets[1] = new VkOffset3D();
			blitRegion.SrcOffsets[1].X = srcSize.X;
			blitRegion.SrcOffsets[1].Y = srcSize.Y;
			blitRegion.SrcOffsets[1].Z = 1;
			blitRegion.DstOffsets[0] = new VkOffset3D();
			blitRegion.DstOffsets[1] = new VkOffset3D();
			blitRegion.DstOffsets[1].X = eyeSwapchainImageSize.X;
			blitRegion.DstOffsets[1].Y = eyeSwapchainImageSize.Y;
			blitRegion.DstOffsets[1].Z = 1;


			Span<VkFence> fencesToReset = stackalloc VkFence[1];
			fencesToReset[0] = copyFence;
			vkDevice.ResetFences(fencesToReset);
			copyCommandBuffer.Reset(VkCommandBufferResetFlags.None);

			var beginInfo = new VkCommandBufferBeginInfo();
			beginInfo.Flags = VkCommandBufferUsageFlags.OneTimeSubmitBit;
			copyCommandBuffer.Begin(beginInfo);

			//Transition the memory layout of both texture as source and destination for copy
			Span<VkImageMemoryBarrier> imageBarriers = stackalloc VkImageMemoryBarrier[1];
			imageBarriers[0] = sourceToTransferBarrier;
			copyCommandBuffer.PipelineBarrier(
				VkPipelineStageFlags.ColorAttachmentOutputBit,
				VkPipelineStageFlags.TransferBit,
				VkDependencyFlags.None,
				default,
				default,
				imageBarriers);
			imageBarriers[0] = destToTransferBarrier;
			copyCommandBuffer.PipelineBarrier(
				VkPipelineStageFlags.TopOfPipeBit,
				VkPipelineStageFlags.TransferBit,
				VkDependencyFlags.None,
				default,
				default,
				imageBarriers);

			//BLIT one onto the other
			Span<VkImageBlit> blitRegions = stackalloc VkImageBlit[1];
			blitRegions[0] = blitRegion;
			copyCommandBuffer.BlitImage(
				sourceImage,
				VkImageLayout.TransferSrcOptimal,
				eyeSwapchainVkImage,
				VkImageLayout.TransferDstOptimal,
				blitRegions,
				VkFilter.Linear);

			//Restore source back to a color attachment
			imageBarriers[0] = sourceBackToColorBarrier;
			copyCommandBuffer.PipelineBarrier(
				VkPipelineStageFlags.TransferBit,
				VkPipelineStageFlags.ColorAttachmentOutputBit,
				VkDependencyFlags.None,
				default,
				default,
				imageBarriers);
			imageBarriers[0] = destToColorBarrier;
			copyCommandBuffer.PipelineBarrier(
				VkPipelineStageFlags.TransferBit,
				VkPipelineStageFlags.BottomOfPipeBit,
				VkDependencyFlags.None,
				default,
				default,
				imageBarriers);

			copyCommandBuffer.End();

			//Do the work
			var submitInfo = new VkSubmitInfo();
			var bufferArray = stackalloc VkCommandBuffer[1];
			bufferArray[0] = copyCommandBuffer.Handle;
			submitInfo.CommandBuffers = bufferArray;
			submitInfo.CommandBufferCount = 1;
			Span<VkSubmitInfo> submitInfos = stackalloc VkSubmitInfo[1];
			submitInfos[0] = submitInfo;
			vkQueue.Submit(submitInfos, copyFence);

			//Make sure it's done
			vkDevice.WaitForFences(fencesToReset, true, (nint)(-1));
		}

		public void DestroySession()
		{
			ResetRenderingStateTracking();

			if (vkDevice.Handle.VkHandle != 0)
			{
				vkDevice.WaitIdle();
				//Cleanup all our Vulkan objects, if allocated
				if (copyCommandBuffer.VkHandle != 0)
				{
					Span<CommandBuffer> commandBuffersToFree = stackalloc CommandBuffer[1];
					commandBuffersToFree[0] = copyCommandBuffer;
					vkDevice?.FreeCommandBuffers(copyCommandPool, commandBuffersToFree);
					copyCommandBuffer = new CommandBuffer();
				}
				if (copyCommandPool.VkHandle != 0)
				{
					vkDevice?.DestroyCommandPool(copyCommandPool, null);
					copyCommandPool = new VkCommandPool();
				}
				if (copyFence.VkHandle != 0)
				{
					vkDevice?.DestroyFence(copyFence, null);
					copyFence = new VkFence();
				}
			}

			if (session.Handle != 0)
			{
				if (hasSessionBegan)
					xrEndSession(session);

				for (int i = 0; i < 2; ++i)
				{
					if (eyeSwapchains[i].Handle != 0)
						xrDestroySwapchain(eyeSwapchains[i]);
					eyeSwapchains[i] = new XrSwapchain();
					eyeSwapchainImages[i]?.Clear();
				}

				xrDestroySpace(applicationLocalSpace);

				xrDestroySession(session);
				session = new XrSession();
				hasSessionBegan = false;
			}
		}

		public void Quit()
		{
			openXREventThreadRunning = false;
			Thread.Sleep(10);
			//DestroySession();
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

			if (instance.Handle == 0)
				return null;

			unsafe
			{
				uint count = 0;
				CheckXRCall(xrGetVulkanInstanceExtensionsKHR(instance, systemId, count, &count, null));
				var buffer = (byte*)Marshal.AllocHGlobal((int)count);
				CheckXRCall(xrGetVulkanInstanceExtensionsKHR(instance, systemId, count, &count, buffer));

				string? output = buffer != null ? PtrToString(buffer) : null;
				Marshal.FreeHGlobal((IntPtr)buffer);

				if (output != null)
					return output.Split(" ");
			}
			return null;
		}


		public string[]? GetRequiredVulkanDeviceExtensions()
		{
			if (instance.Handle == 0)
				return null;

			unsafe
			{
				uint count = 0;
				CheckXRCall(xrGetVulkanDeviceExtensionsKHR(instance, systemId, count, &count, null));
				var buffer = (byte*)Marshal.AllocHGlobal((int)count);
				CheckXRCall(xrGetVulkanDeviceExtensionsKHR(instance, systemId, count, &count, buffer));

				string? output = buffer != null ? PtrToString(buffer) : null;
				Marshal.FreeHGlobal((IntPtr)buffer);

				if (output != null)
					return output.Split(" ");
			}

			return null;
		}

		/// <summary>
		/// Validates the result of an OpenXR API call and throws an exception if the result indicates failure and is not in
		/// the list of allowed return codes.
		/// </summary>
		/// <remarks>Use this method to centralize error handling for OpenXR API calls. It allows certain non-success
		/// result codes to be treated as valid outcomes, simplifying control flow when specific non-error codes are
		/// expected.</remarks>
		/// <param name="result">The result value returned by the OpenXR API call to validate.</param>
		/// <param name="allowedReturnCodes">An optional list of additional OpenXR result codes that are considered acceptable and will not cause an exception
		/// to be thrown. If null, only a result of XR_SUCCESS is considered successful.</param>
		/// <exception cref="Exception">Thrown if the result is not XR_SUCCESS and is not included in the allowedReturnCodes list.</exception>
		/// <returns>Forward the XrResult recived, useful to handle a non-failure error code that was allowed by this API call</returns>
		private XrResult CheckXRCall(XrResult result, HashSet<XrResult>? allowedReturnCodes = null)
		{
			if (result == XrResult.XR_SUCCESS)
				return result;

			else if (allowedReturnCodes != null && allowedReturnCodes.Contains(result))
				return result;

			throw new Exception($"OpenXR API call failed {result}");
		}
	}
}
