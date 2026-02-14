using System;
using System.Collections.Generic;
using System.Text;

using Brutal.ImGuiApi;

namespace KSA_XR
{
	public class DebugUI
	{
		public bool signaled_try_init = false;
		public void StatusWindow()
		{
			ImGui.Begin("KSA_XR");

			var xr = ModLoader.openxr;
			if(xr != null)
			{
				ImGui.Text($"OpenXR Runtime {xr.RuntimeName}");
				ImGui.Text($"OpenXR System {xr.SystemName}");
				ImGui.Text($"System's viewconfig type {xr.viewConfigurationType}");

				if (ImGui.Button("Try to start XrSession"))
				{
					signaled_try_init = true;
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
