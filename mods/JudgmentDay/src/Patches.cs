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

	// While the lever is dragged the game moves it by the cursor's travel times the reach and clamps it at its ends, so cursor movement past
	// the end has to be undone before the lever comes back: pulling it all the way down and straight back up needed a release in between.
	// The end of the travel now absorbs that overshoot, so reversing the drag moves the lever at once (one sweep: down and up).
	[HarmonyPatch(typeof(LinearObject), "StepDirect")]
	internal static class LeverOvertravelPatch
	{
		private static readonly FieldInfo GrabStart = AccessTools.Field(typeof(LinearObject), "grabStartS");

		private static readonly FieldInfo HandStart = AccessTools.Field(typeof(LinearObject), "handStartS");

		private static readonly MethodInfo HandS = AccessTools.Method(typeof(LinearObject), "HandS");

		private static void Postfix(LinearObject __instance)
		{
			if (__instance != Lever.Pump || !__instance.IsGrabbed || GrabStart == null || HandStart == null || HandS == null)
			{
				return;
			}
			float hand = (float)HandS.Invoke(__instance, new object[] { __instance.constrainedPoint });
			float start = (float)GrabStart.GetValue(__instance);
			float target = start + (hand - (float)HandStart.GetValue(__instance)) * __instance.CDRatio;
			if (target > __instance.maxLimit)
			{
				GrabStart.SetValue(__instance, start - (target - __instance.maxLimit));
			}
			else if (target < __instance.minLimit)
			{
				GrabStart.SetValue(__instance, start + (__instance.minLimit - target));
			}
		}
	}
}
