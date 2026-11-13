using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace JudgmentDay
{
	// The pump shotgun keeps doing the loading and chambering; with Judgment Day fitted a Lever and a SpinCock are put on it as it wakes.
	[HarmonyPatch(typeof(NPumpActionGun), "Awake")]
	internal static class PumpAwakePatch
	{
		private static void Postfix(NPumpActionGun __instance)
		{
			if (JudgmentDayItem.IsEquipped && __instance.RequiredGunType == GunType.SHOTGUN && __instance.GetComponent<SpinCock>() == null)
			{
				__instance.gameObject.AddComponent<Lever>().Setup(__instance);
				__instance.gameObject.AddComponent<SpinCock>().Setup(__instance);
			}
		}
	}

	// No firing while the gun is flipping over, until the round is in the chamber.
	[HarmonyPatch(typeof(NPumpActionGun), nameof(NPumpActionGun.Shoot))]
	internal static class ShootPatch
	{
		private static bool Prefix(NPumpActionGun __instance)
		{
			SpinCock spin = __instance.GetComponent<SpinCock>();
			return spin == null || spin.CanFire;
		}
	}

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

	// The lever stays down once it has been pulled all the way open with the chamber empty, so there are two hands' worth of work done with
	// one cursor: pull it open, drop the shells in, push it shut (which chambers the next one). The game's pump would spring shut at once
	// whenever there are shells to feed.
	[HarmonyPatch(typeof(NPumpActionGun), "UpdatePump")]
	internal static class LeverLatchPatch
	{
		private static void Postfix(NPumpActionGun __instance)
		{
			if (Mod.OpenToLoad.Value && __instance.racked && !__instance.loaded && __instance.pump != null && !__instance.pump.IsGrabbed
				&& __instance.GetComponent<SpinCock>() != null)
			{
				__instance.pump.targetValue = 1f;
			}
			// Open the breach on a live round to load more: the lever can be dragged open even with a round chambered (the game would hold it
			// shut), and the round goes back into the tube.
			if (Mod.OpenToLoad.Value && __instance.loaded && __instance.pump != null && __instance.pump.IsGrabbed && __instance.pump.value > 0.6f
				&& __instance.GetComponent<SpinCock>() != null)
			{
				__instance.loaded = false;
				__instance.bullets++;
				__instance.racked = true;
				SoundController.PlayRandomSound2D(__instance.reloadPullSounds, __instance.gameObject);
				if (Mod.DumpInfo.Value)
				{
					Mod.Log.LogInfo($"Judgment Day: breach opened on a live round, bullets now {__instance.bullets}");
				}
			}
		}
	}

	// With a round chambered the game turns a grab on the pump into "pick the gun up to aim" (the pump is one of the gun's aim parts). The
	// lever of this gun has to be workable then (to open the breach and load more), so only the rest of the gun picks it up.
	[HarmonyPatch(typeof(NPumpActionGun), "PartsAim")]
	internal static class LeverIsNotAnAimPartPatch
	{
		private static void Postfix(NPumpActionGun __instance, ref bool __result)
		{
			if (__result && Mod.OpenToLoad.Value && __instance.GetComponent<SpinCock>() != null)
			{
				__result = false;
			}
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
