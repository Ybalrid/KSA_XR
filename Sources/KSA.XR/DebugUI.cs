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

				var xr = ModInit.openxr;
				if (xr != null)
				{
					ImGui.Text($"OpenXR Runtime {xr.RuntimeName}");
					ImGui.Text($"OpenXR System {xr.SystemName}");
					ImGui.Text($"System's viewconfig type {xr.ViewConfigurationType}");

					if (!XrSessionStarted && ImGui.Button("Try to start XrSession"))
					{
						XrSessionStarted = xr.CreateSesionAndAllocateSwapchains();
					}

					if (XrSessionStarted)
					{
						if (ImGui.Button("End XrSession"))
						{
							xr.DestroySession();
							XrSessionStarted = false;
						}

						var yellowColor = new float4();
						yellowColor.A = 1;
						yellowColor.R = 1;
						yellowColor.G = 1;
						ImGui.TextColored(yellowColor, $"XrSession {xr.Session.Handle}");

						for (int i = 0; i < 2; ++i)
						{
							var pose = xr.MostRecentEyeViewPoses[i];
							ImGui.Text($"{(OpenXR.EyeIndex)i} eye view pose in LOCAL space:");
							ImGui.Text($"Pos({pose.position.x}, {pose.position.y}, {pose.position.z})");
							ImGui.Text($"Rot({pose.orientation.x}, {pose.orientation.y}, {pose.orientation.z}, {pose.orientation.w})");
						}

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
