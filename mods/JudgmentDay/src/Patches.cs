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

	// No firing while the gun is flipping over.
	[HarmonyPatch(typeof(NPumpActionGun), nameof(NPumpActionGun.Shoot))]
	internal static class ShootPatch
	{
		private static bool Prefix(NPumpActionGun __instance)
		{
			SpinCock spin = __instance.GetComponent<SpinCock>();
			return spin == null || !spin.Flipping;
		}
	}
}
