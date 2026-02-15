using System;
using System.Collections.Generic;
using System.Text;
using Brutal.Numerics;
using KSA;
using HarmonyLib;

namespace KSA.XR
{
	internal class XrViewports
	{
		public static KSA.Viewport? AddViewport(int2 framebufferSize, bool buildRenderTarget, bool centerViewport)
		{
			try
			{
				var program = KSA.Program.Instance;
				
				var addViewport = AccessTools.Method(typeof(KSA.Program), "AddViewport");
				var before = KSA.Program.Viewports.Count;

				addViewport.Invoke(program, new object[]
				{
					framebufferSize,
					buildRenderTarget,
					centerViewport
				});

				return KSA.Program.Viewports[before];
			}
			catch (Exception e)
			{
				//trap all
				Logger.error(e.ToString());
			}

			return null;
		}
	}
}
