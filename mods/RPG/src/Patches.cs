using HarmonyLib;
using UnityEngine;

namespace HalfGripRpg
{
	// The RPG takes the SMG's place in the car. As the SMG's gun component wakes with the RPG equipped, it is replaced by the
	// RPG's own launcher and does not get to wake at all.
	[HarmonyPatch(typeof(NAutomaticGun), "Awake")]
	internal static class SmgGunAwakePatch
	{
		private static bool Prefix(NAutomaticGun __instance)
		{
			if (!RpgItem.IsEquipped || __instance.RequiredGunType != GunType.SMG_RIFLE)
			{
				return true;
			}
			Conversion.ConvertGun(__instance);
			return false;
		}
	}

	// The same for the magazines in the rack: they become rockets before the rack counts and copies them.
	[HarmonyPatch(typeof(MagazineManager), "Awake")]
	internal static class RackAwakePatch
	{
		private static void Prefix(MagazineManager __instance)
		{
			if (RpgItem.IsEquipped)
			{
				Conversion.ConvertMagazines(__instance);
			}
		}
	}

	// A shot is one bullet fired along a ray. The RPG sends a rocket instead, so its shot skips the ray.
	[HarmonyPatch(typeof(Gun), "FireRay")]
	internal static class FireRayPatch
	{
		private static bool Prefix(Gun __instance, Vector3 origin, Vector3 direction, ref Vector3 __result)
		{
			if (!(__instance is RpgLauncher launcher))
			{
				return true;
			}
			__result = launcher.LaunchRocket(origin, direction);
			return false;
		}
	}
}
