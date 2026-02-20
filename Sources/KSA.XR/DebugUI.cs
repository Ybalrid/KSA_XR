using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSA.XR
{
	public class DebugUI
	{
		public DebugUI()
		{
			Logger.message("Initialize Debug UI");
		}

		public bool TestBool = false;
		bool XrSessionStarted = false;
		float renderBufferResoltuionScale = 1f;
		public void StatusWindow()
		{
			if (ImGui.Begin("KSA_XR"))
			{

				var xr = ModInit.openxr;
				if (xr != null && xr.Instance.Handle != 0)
				{
					ImGui.Text($"OpenXR Runtime {xr.RuntimeName}");
					ImGui.Text($"OpenXR System {xr.SystemName}");
					ImGui.Text($"System's viewconfig type {xr.ViewConfigurationType}");

					if (!XrSessionStarted)
					{
						ImGui.DragFloat("XR Resolution Scaler", ref renderBufferResoltuionScale, 0.01f, 0.5f, 2f);
						if (ImGui.Button("Reset Scale"))
							renderBufferResoltuionScale = 1;

						float scaledEyeBufferSizeW = (xr.EyeViewConfigurations[0].recommendedImageRectWidth * renderBufferResoltuionScale);
						float scaledEyebufferSizeH = xr.EyeViewConfigurations[0].recommendedImageRectHeight * renderBufferResoltuionScale;

						//32 bit per pixel, 2 buffer per swapchain, 3 image depth per swachain;
						ulong sizeEstimation = (ulong)3 * 2 * 8 * (ulong)scaledEyeBufferSizeW * (ulong)scaledEyebufferSizeH;

						ImGui.Text($"Would allocate {(uint)scaledEyeBufferSizeW}x{(uint)scaledEyebufferSizeH} pixels per eye.");
						ImGui.Text($"Estimated Swapchain memory impact {sizeEstimation / 1024 / 1024}MB");

						if (ImGui.Button("Start XrSession"))
							XrSessionStarted = xr.CreateSesionAndAllocateSwapchains(renderBufferResoltuionScale);
					}

					if (XrSessionStarted)
					{
						if (ImGui.Button("End XrSession"))
						{
							xr.DestroySession();
							XrSessionStarted = false;
							Program.MainViewport.BaseCamera.UpdateProjection();
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

						var eye = XrViewports.Instance.CurrentXREye;
						var state = XrViewports.Instance.CurrentRenderState;

						ImGui.Text($"State {state} eye {eye}");
						ImGui.Checkbox("Disable Symetric FoV is layer submission", ref TestBool);
					}
				}
				else
				{
					ImGui.Text("OpenXR not initialized.");
				}
			}
			ImGui.End();
		}
	}
}
