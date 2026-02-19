using Brutal.Numerics;
using HarmonyLib;
using KSA;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;


namespace KSA.XR
{

	[HarmonyPatch(typeof(KSA.Camera))]
	[HarmonyPatch(nameof(KSA.Camera.OnFrame))]
	static class CameraOnFrameTrackingPatch
	{
		static void Prefix(KSA.Camera __instance)
		{
			var xr = ModInit.openxr;
			if (xr == null || xr.Session.Handle == 0)
				return;

			//We currently only hack on the camera of
			var mainViewport = Program.MainViewport;
			if (!ReferenceEquals(__instance, mainViewport.BaseCamera))
				return;

			var pose = xr.EyeViews[0].pose;
			var rot = pose.orientation;
			var dRot = new doubleQuat(rot.x, rot.y, rot.z, rot.w);
			var pos = pose.position;
			var dPos = new double3(pos.x, pos.y, pos.z);

			//Apply tracking
			__instance.LocalRotation *= dRot;
			__instance.LocalPosition += dPos;

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
				var leftEyeViewConfig = xr.EyeViews[0];
				var fov = xr.SysmetricalEyeFov[0];
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
	}
}
