using HarmonyLib;
using UnityEngine;

namespace HalfGripSniper
{
	/// <summary>
	/// The zoom. The car eases the view camera's field of view toward its own target every frame, so the zoom cannot simply be set:
	/// the game's value is put back just before the car runs and the zoom laid over the result just after, which keeps the two apart.
	/// </summary>
	internal static class Scope
	{
		/// <summary>The camera the rifle aims through, and how far in (0 to 1) it is zoomed.</summary>
		public static Camera Camera;

		public static float Amount;

		// The field of view the game last worked out, before the zoom.
		private static float gameFov;

		private static bool applied;

		public static void Set(Camera camera, float amount)
		{
			Camera = camera;
			Amount = amount;
		}

		/// <summary>Lets go of the camera, as the rifle is put away.</summary>
		public static void Release(Camera camera)
		{
			if (Camera == camera)
			{
				Amount = 0f;
			}
		}

		internal static void BeforeCar(CarControllerNew car)
		{
			if (applied && Camera != null && car.fpvCamera == Camera)
			{
				Camera.fieldOfView = gameFov;
			}
			applied = false;
		}

		internal static void AfterCar(CarControllerNew car)
		{
			if (Camera == null || car.fpvCamera != Camera)
			{
				return;
			}
			gameFov = Camera.fieldOfView;
			float zoom = Mathf.Lerp(1f, Mathf.Max(1f, Mod.Magnification.Value), Mathf.Clamp01(Amount));
			if (zoom > 1.001f)
			{
				Camera.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(gameFov * 0.5f * Mathf.Deg2Rad) / zoom) * Mathf.Rad2Deg;
				applied = true;
			}
		}
	}

	[HarmonyPatch(typeof(CarControllerNew), "UpdateCameraEffects")]
	internal static class CarCameraPatch
	{
		private static void Prefix(CarControllerNew __instance)
		{
			Scope.BeforeCar(__instance);
		}

		private static void Postfix(CarControllerNew __instance)
		{
			Scope.AfterCar(__instance);
		}
	}
}
