using Brutal.ImGuiApi;
using Brutal.Numerics;
using HarmonyLib;

namespace KSA.XR
{

	/// <summary>
	/// Patches Brutal's ImGui wrapper to insert code either at the begining or the end of an existing Menu
	/// </summary>
	/// <remarks>
	/// A registered callback might be called multiple times, if multiple menus are present.
	/// TODO could hook ImGui.Begin() and ImGui.End() to also know the name of a Window to solve this eventual problem.
	/// </remarks>
	internal class ImGuiMenuPatcher
	{

		//Due to the fact that sub-menus exist, we need to store the list of menu names that have been open, so the EndMenu() hook can know which menu is terminating
		static Stack<string> MenuNames = new Stack<string>();

		/// <summary>
		/// Actions that will be inserted in the begining of a menu. The key is the name of the menu, the Action is a `void function()` called just after BeginMenu("Name") returns true.
		/// </summary>
		static public Dictionary<string, Action> ActionAddedAtMenuBegin = new Dictionary<string, Action>();

		/// <summary>
		/// Actions that will be inserted at the end of a menu. The key is the name of the menu, the Action is a `void function()` that will be called just before EndMenu() runs for the chosen menu
		/// </summary>
		static public Dictionary<string, Action> ActionAddedAtMenuEnd = new Dictionary<string, Action>();

		[HarmonyPatch(typeof(Brutal.ImGuiApi.ImGui))]
		[HarmonyPatch("BeginMenu")]
		[HarmonyPatch(new[] { typeof(ImString), typeof(bool) })]
		internal static class ImGuiBeginMenuBarPatch
		{
			private static Action? callbackAfterBeginMenu = null;
			public static void Prefix(ImString __0, bool __1)
			{
				var menuName = (string)__0;
				MenuNames.Push(menuName);

				//We need to defer the calling of the found callback to the Postfix patch function. Store the reference to the action internally
				if (ActionAddedAtMenuBegin.TryGetValue(menuName, out Action? callback))
					callbackAfterBeginMenu = callback;
				else
					callbackAfterBeginMenu = null;
			}

			public static void Postfix(ref bool __result)
			{
				if (!__result) // Menu is not open, EndMenu will not be called. This is effectively a closed menu
					MenuNames.Pop(); // Manually removed from the stack, because ImGuiEndMenuBarPatch.Prefix() will not be called for us
				else if (callbackAfterBeginMenu != null)
					callbackAfterBeginMenu();
			}
		}


		[HarmonyPatch(typeof(Brutal.ImGuiApi.ImGui))]
		[HarmonyPatch("EndMenu")]
		internal static class ImGuiEndMenuBarPatch
		{
			public static void Prefix()
			{
				if (MenuNames.TryPop(out string? MenuName))
					if (MenuName != null && ActionAddedAtMenuEnd.TryGetValue(MenuName, out Action? callback))
						callback();
			}
		}
	}

	public class DebugUI
	{
		public DebugUI()
		{
			Logger.message("Initialize Debug UI");

			ImGuiMenuPatcher.ActionAddedAtMenuEnd.Add("View", () =>
			{
				ImGui.Separator();
				ImGui.MenuItem("OpenXR Debug Window", "", ref WindowOpen);
			});
		}

		public bool TestBool = false;
		bool XrSessionStarted = false;
		float renderBufferResoltuionScale = 1f;

		public bool WindowOpen = true;
		public void StatusWindow()
		{

			if (WindowOpen && ImGui.Begin("KSA_XR", ref WindowOpen))
			{
				ImGui.TextDisabled("You can close this window and re-open it from the View menu");

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
							XrViewports.Instance.ResetStateToNormal();
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
				ImGui.End();
			}
		}
	}
}
