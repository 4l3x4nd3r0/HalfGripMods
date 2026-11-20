using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace JudgmentDay
{
	// The loose shells for this gun are black with brass heads.
	[HarmonyPatch(typeof(NShell), "Update")]
	internal static class ShellColourPatch
	{
		private static void Postfix(NShell __instance)
		{
			if (__instance.shotgun != null && __instance.shotgun.GetComponent<SpinCock>() != null)
			{
				ShellArt.Shell(__instance);
			}
		}
	}

	// Shells go in through the open action, as on the real 1887: a shell dropped on the gun is refused while the lever is closed.
	[HarmonyPatch(typeof(NShell), "TryLoad")]
	internal static class ShellLoadPatch
	{
		private static bool Prefix(NShell __instance)
		{
			if (!Mod.OpenToLoad.Value || __instance.shotgun == null || __instance.shotgun.GetComponent<SpinCock>() == null)
			{
				return true;
			}
			NPumpActionGun gun = __instance.shotgun;
			return gun.racked || (gun.pump != null && gun.pump.value > 0.5f);
		}
	}

	// The lever is the game's pump, and the game reads a drag as where the cursor's ray meets the pump's movement plane, along the pump's
	// axis. The lever is pulled down, which is a steep direction in that plane, so a few pixels of mouse became big jumps and a small wobble
	// at the bottom of the swing sent it back up. While the lever is held it is driven from the mouse's vertical travel instead (the same
	// cursor formula the game uses): down opens it, up closes it, and travel past either end is dropped, so only an upward movement closes
	// it again. The game's own spring, half-way snap, racking and chambering still run on the value this produces. A gamepad's cursor does
	// not show up in the mouse delta, so it keeps working only through the game's own drag (DragDown off).
	[HarmonyPatch(typeof(LinearObject), "StepDirect")]
	internal static class LeverDragPatch
	{
		private static readonly FieldInfo S = AccessTools.Field(typeof(LinearObject), "s");

		private static readonly FieldInfo GrabStart = AccessTools.Field(typeof(LinearObject), "grabStartS");

		private static readonly FieldInfo HandStart = AccessTools.Field(typeof(LinearObject), "handStartS");

		private static readonly MethodInfo HandS = AccessTools.Method(typeof(LinearObject), "HandS");

		private static bool wasGrabbed;

		private static float dragS;

		private static Grabber grabber;

		private static bool warned;

		private static void Prefix(LinearObject __instance)
		{
			if (__instance != Lever.Pump || !Mod.DragDown.Value)
			{
				return;
			}
			if (!__instance.IsGrabbed)
			{
				wasGrabbed = false;
				return;
			}
			if (S == null || GrabStart == null || HandStart == null || HandS == null)
			{
				if (!warned)
				{
					warned = true;
					Mod.Log.LogWarning("LinearObject fields for the lever drag were not found: the lever uses the game's own drag");
				}
				return;
			}
			if (!wasGrabbed)
			{
				wasGrabbed = true;
				dragS = (float)S.GetValue(__instance);
				if (grabber == null)
				{
					grabber = Object.FindFirstObjectByType<Grabber>();
				}
				if (Mod.DumpInfo.Value)
				{
					Mod.Log.LogInfo($"Judgment Day lever drag: grabbed at {dragS:0.00} of {__instance.minLimit}..{__instance.maxLimit}, a full pull is {Mod.LeverDrag.Value} of the screen height, grabber {(grabber != null ? "found" : "MISSING")}");
				}
			}
			if (grabber != null && !GameInput.Muted)
			{
				float heights = -Input.mousePositionDelta.y * grabber.cursorSensitivity * Grabber.HandSensitivity / 1080f;
				float travel = __instance.maxLimit - __instance.minLimit;
				dragS = Mathf.Clamp(dragS + heights / Mathf.Max(0.02f, Mod.LeverDrag.Value) * travel, __instance.minLimit, __instance.maxLimit);
			}
			// The game works out its target as grabStartS + (HandS - handStartS) * CDRatio: with these two set that is exactly dragS.
			GrabStart.SetValue(__instance, dragS);
			HandStart.SetValue(__instance, (float)HandS.Invoke(__instance, new object[] { __instance.constrainedPoint }));
		}
	}
}
