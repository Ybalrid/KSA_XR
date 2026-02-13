using StarMap.API;

namespace KSA_XR
{
	[StarMapMod]
	public class ModLoader
	{
		static OpenXR openxr;

		[StarMapBeforeMain]
		public void preMain()
		{
			Logger.message("preMain() reached");
			openxr = new OpenXR();
		}
	}
}
