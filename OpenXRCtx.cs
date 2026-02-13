using System.Runtime.InteropServices;

namespace KSA_XR.OpenXR
{
	internal class Loader
	{
		[DllImport("openxr_loader", CallingConvention = CallingConvention.Cdecl)]
		public static extern int xrGetInstanceProcAddr(
			IntPtr instance,
			string name,
			out IntPtr function
		);
		
		public enum XrResult : int
		{
			XR_SUCCESS = 0,
			// other XrResult values as needed
		}


		public const int XR_TYPE_EXTENSION_PROPERTIES = 2;
		[StructLayout(LayoutKind.Sequential)]
		public unsafe struct XrExtensionProperties
		{
			public int type;
			public void* next;

			// XR_MAX_EXTENSION_NAME_SIZE is 128 in OpenXR
			public fixed byte extensionName[128];
			public uint extensionVersion;

			public void Init()
			{
				//Always rememeber that the damn openxr_loader alias all types to this header
				type = XR_TYPE_EXTENSION_PROPERTIES; //mandatory, API call fails without 
				next = null;
			}
		}


		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		public unsafe delegate XrResult xrEnumerateInstanceExtensionPropertiesDelegate(
			byte* layerName,
			uint propertyCapacityInput,
			uint* propertyCountOutput,
			XrExtensionProperties* properties
		);
	}

}
