using System;
using System.Collections.Generic;
using System.Text;
using Brutal.Numerics;
using KSA;
using HarmonyLib;

namespace KSA.XR
{
	[HarmonyPatch(typeof(KSA.Program))]
	[HarmonyPatch("OnFrame")]
	internal static class ProgramPatches
	{
		static bool runOnce = false;
		static void Postfix(KSA.Program __instance)
		{
			if (!runOnce)
			{
				runOnce = true;
				Logger.message("OnFrame()");
			}
		}
	}
	

	internal class XrViewports
	{
		public static unsafe void RenderViewport(Brutal.VulkanApi.CommandBuffer commandBuffer, Viewport viewport, int frameIndex)
		{
			var program = KSA.Program.Instance;

			var renderViewport = AccessTools.Method(typeof(KSA.Program), "RenderViewport");

			renderViewport.Invoke(program, new object[]
			{
				commandBuffer, viewport, frameIndex
			});

		}


		public static KSA.Viewport? AddViewport(int2 framebufferSize, bool buildRenderTarget, bool centerViewport)
		{
			try
			{
				/*
				var program = KSA.Program.Instance;
				
				var addViewport = AccessTools.Method(typeof(KSA.Program), "AddViewport");
				var before = KSA.Program.Viewports.Count;

				addViewport.Invoke(program, new object[]
				{
					framebufferSize,
					buildRenderTarget,
					centerViewport
				});

				program.RebuildRenderer();

				return KSA.Program.Viewports[before];
		
				*/

/*
			Camera camera = new Camera(framebufferSize);
			camera.SetPosition(-28423595433.0, 350059017438.0, 952938436359.0);
			camera.LookAt(double3.Zero, Double3Ex.Up);
			GameSettings.Current.ApplyTo(camera);*/
			
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
