using Brutal.Numerics;
using Brutal.VulkanApi;
using HarmonyLib;
using KSA;
using RenderCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection.Emit;
using System.Reflection;


namespace KSA.XR
{

	internal class XrViewports
	{
		private static XrViewports? instance = null;
		
		//Singletonize me
		public static XrViewports Instance
		{
			get
			{
				if (instance != null)
					return instance;

				instance = new XrViewports();
				return instance;
			}
		}

		//State machine to keept rack of the current rendering
		public enum RenderHackPasses
		{
			NormalGame,
			XR
		};

		private RenderHackPasses currentRenderHackState = RenderHackPasses.NormalGame;
		private OpenXR.EyeIndex currentXREye = OpenXR.EyeIndex.Left;

		//State machine transition function
		public void RenderFinished()
		{
			if(currentRenderHackState == RenderHackPasses.NormalGame)
			{
				currentRenderHackState = RenderHackPasses.XR;
				currentXREye = OpenXR.EyeIndex.Left;
				return;
			}

			if(currentRenderHackState == RenderHackPasses.XR)
			{
				if(currentXREye == OpenXR.EyeIndex.Left)
				{
					currentXREye = OpenXR.EyeIndex.Right;
					return;
				}

				if(currentXREye == OpenXR.EyeIndex.Right)
				{
					currentRenderHackState = RenderHackPasses.NormalGame;
					return;
				}
			}
		}

		public void ResetStateToNormal()
		{
			currentRenderHackState = RenderHackPasses.NormalGame;
		}

		//Get current renderer state. Is it normal viewport or XR buffer
		public RenderHackPasses CurrentRenderState => currentRenderHackState;
		//Get current eye for XR, is it left or right?
		public OpenXR.EyeIndex CurrentXREye => currentXREye;

		public void DebugDisplayState()
		{
			Logger.message($"RenderHack state {CurrentRenderState}");
			if(CurrentRenderState == RenderHackPasses.XR)
				Logger.message($"Eye {CurrentXREye}");
		}


		static public int swapeye(int eye)
		{
			if (eye == 0)
				return 1;
			else
				return 0;
		}

	}

	[HarmonyPatch(typeof(KSA.Camera))]
	[HarmonyPatch(nameof(KSA.Camera.OnFrame))]
	static class CameraOnFrameTrackingPatch
	{
		static void Prefix(KSA.Camera __instance)
		{
			if (XrViewports.Instance.CurrentRenderState == XrViewports.RenderHackPasses.NormalGame)
			{
				//Compute screen correct projection
				__instance.UpdateProjection();
				return;
			}

			var xr = ModInit.openxr;
			if (xr == null || xr.Session.Handle == 0)
				return;

			//We currently only hack on the camera of
			var mainViewport = Program.MainViewport;
			if (!ReferenceEquals(__instance, mainViewport.BaseCamera))
				return;

			var pose = xr.EyeViews[((int)XrViewports.Instance.CurrentXREye)].pose;
			var rot = pose.orientation;
			var dRot = new doubleQuat(rot.x, rot.y, rot.z, rot.w);
			var pos = pose.position;
			var dPos = new double3(pos.x, pos.y, pos.z);


			//Apply Tracking
			__instance.LocalRotation *= dRot;
			__instance.LocalPosition += __instance.LocalRotation * (dPos);
			
			//Recompute projection 
			__instance.UpdateProjection();
		}
	}

	[HarmonyPatch(typeof(KSA.Camera))]
	[HarmonyPatch(nameof(KSA.Camera.UpdateProjection))]
	internal static class CameraPatches
	{
		public static float4x4 CreatePerspectiveFrustumAnglesReverseZ(
			 float leftHalfAngleRad,
			 float rightHalfAngleRad,
			 float bottomHalfAngleRad,
			 float topHalfAngleRad,
			 float nearPlaneDistance,
			 float farPlaneDistance)
		{
			ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(nearPlaneDistance, 0f, nameof(nearPlaneDistance));
			ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(farPlaneDistance, 0f, nameof(farPlaneDistance));
			ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(nearPlaneDistance, farPlaneDistance, nameof(nearPlaneDistance));

			// Near-plane extents (view space).
			// Left and bottom are negative extents by convention.
			float l = MathF.Tan(leftHalfAngleRad) * nearPlaneDistance;
			float r = MathF.Tan(rightHalfAngleRad) * nearPlaneDistance;
			float b = MathF.Tan(bottomHalfAngleRad) * nearPlaneDistance;
			float t = MathF.Tan(topHalfAngleRad) * nearPlaneDistance;

			float invRL = 1f / (r - l);
			float invTB = 1f / (t - b);

			float4x4 m = float4x4.Identity;

			m.M11 = 2f * nearPlaneDistance * invRL;
			m.M12 = 0f;
			m.M13 = (r + l) * invRL;
			m.M14 = 0f;

			m.M21 = 0f;
			m.M22 = -2f * nearPlaneDistance * invTB;
			m.M23 = (t + b) * invTB;
			m.M24 = 0f;

			m.M31 = 0f;
			m.M32 = 0f;
			m.M33 = nearPlaneDistance / (farPlaneDistance - nearPlaneDistance);
			m.M34 = -1f;

			m.M41 = 0f;
			m.M42 = 0f;
			m.M43 = (nearPlaneDistance * farPlaneDistance) / (farPlaneDistance - nearPlaneDistance);
			m.M44 = 0f;

			return m;
		}



		static void Postfix(KSA.Camera __instance)
		{
			if (XrViewports.Instance.CurrentRenderState == XrViewports.RenderHackPasses.NormalGame)
				return;

			//Obtain access to ViewProjection matrices
			var _vpField = AccessTools.Field(__instance.GetType(), "_vp");
			ViewProjection vp = (ViewProjection)_vpField.GetValue(__instance);

			var _vpInvField = AccessTools.Field(__instance.GetType(), "_vpInv");
			ViewProjection vpInv = (ViewProjection)_vpInvField.GetValue(__instance);

			var xr = ModInit.openxr;
			if (xr != null && xr.Instance.Handle != 0 && xr.Session.Handle != 0)
			{
				var MainCamera = Program.MainViewport.BaseCamera;
				if (!MainCamera.Equals(__instance))
					return;

				//Get the left eye view
				var fov = xr.SysmetricalEyeFov[((int)XrViewports.Instance.CurrentXREye)];

				//TODO this will support asymetrical projections, but currently we force it to not break culling
				var projectionMatrix = CreatePerspectiveFrustumAnglesReverseZ(fov.angleLeft,
					fov.angleRight,
					fov.angleDown,
					fov.angleUp,
					__instance.NearPlane, __instance.FarPlane);

				vp.projection = projectionMatrix;
				
				if (!float4x4.Invert(vp.projection, out vpInv.projection))
				{
					Logger.error("Patch computed a projection matrix that is non-inversible. Cannot apply it");
				}
				else
				{
					_vpField.SetValue(__instance, vp);
					_vpInvField.SetValue(__instance, vpInv);
				}
			}
		}
	}

	[HarmonyPatch(typeof(KSA.Program))]
	[HarmonyPatch("OnFrame")]
	internal static class ProgramOnFramePatches
	{
		static bool runOnce = false;

		static void Prefix(KSA.Program __instance)
		{
		}

		static void Postfix(KSA.Program __instance)
		{
		}
	}

	[HarmonyPatch(typeof(KSA.Program))]
	[HarmonyPatch("RenderGame")]
	internal static class ProgramRenderGamePatches
	{
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var replacementGeneric = AccessTools.Method(typeof(ProgramRenderGamePatches), nameof(RenderScreenspaceIfAllowed));
			foreach (var instruction in instructions)
			{
				if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
				    instruction.operand is MethodInfo called &&
				    called.DeclaringType == typeof(KSA.ScreenspaceRenderer) &&
				    called.Name == "Render" &&
				    called.IsGenericMethod)
				{
					var replacement = replacementGeneric.MakeGenericMethod(called.GetGenericArguments());
					instruction.opcode = OpCodes.Call;
					instruction.operand = replacement;
				}

				yield return instruction;
			}
		}

		static void Prefix(KSA.Program __instance)
		{
		}

		static void Postfix(KSA.Program __instance)
		{
		}

		static void RenderScreenspaceIfAllowed<T>(KSA.ScreenspaceRenderer renderer, CommandBuffer inCommandBuffer, int frameIndex, T pushConstant) where T : unmanaged
		{
			if (XrViewports.Instance.CurrentRenderState != XrViewports.RenderHackPasses.NormalGame)
				return;

			renderer.Render(inCommandBuffer, frameIndex, pushConstant);
		}
	}

	[HarmonyPatch]
	internal static class SkipImGuiRenderDrawDataPatch
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			foreach (var method in AccessTools.GetDeclaredMethods(typeof(KSA.ImGuiBackendVulkanImpl)))
			{
				if (method.Name == "RenderDrawData")
					yield return method;
			}
		}

		static bool Prefix()
		{
			return XrViewports.Instance.CurrentRenderState == XrViewports.RenderHackPasses.NormalGame;
		}
	}

}

