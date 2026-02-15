using System;
using System.Collections.Generic;
using System.Text;

using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSA
{
	namespace XR
	{
		public class DebugUI
		{
			public DebugUI()
			{
				Logger.message("Initialize Debug UI");
			}

			bool XrSessionStarted = false;
			public void StatusWindow()
			{
				ImGui.Begin("KSA_XR");

				var xr = ModLoader.openxr;
				if (xr != null)
				{
					ImGui.Text($"OpenXR Runtime {xr.RuntimeName}");
					ImGui.Text($"OpenXR System {xr.SystemName}");
					ImGui.Text($"System's viewconfig type {xr.ViewConfigurationType}");

					if (!XrSessionStarted && ImGui.Button("Try to start XrSession"))
					{
						//signaled_try_init = true;
						XrSessionStarted = xr.TryStartSession();
					}

					if (XrSessionStarted)
					{
						var yellowColor = new float4();
						yellowColor.A = 1;
						yellowColor.R = 1;
						yellowColor.G = 1;
						ImGui.TextColored(yellowColor, $"XrSession {xr.Session.Handle}");
					}
				}
				else
				{
					ImGui.Text("Initialization of OpenXR has failed.");
				}

				ImGui.End();
			}
		}
	}
}
