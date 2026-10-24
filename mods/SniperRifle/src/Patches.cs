using HarmonyLib;
using UnityEngine;

namespace HalfGripSniper
{
	// The rifle takes the SMG's place in the car. As the SMG's gun component wakes with the rifle equipped, it is replaced by the
	// rifle's own gun and does not get to wake at all.
	[HarmonyPatch(typeof(NAutomaticGun), "Awake")]
	internal static class SmgGunAwakePatch
	{
		private static bool Prefix(NAutomaticGun __instance)
		{
			if (!RifleItem.IsEquipped || __instance.RequiredGunType != GunType.SMG_RIFLE)
			{
				return true;
			}
			Conversion.ConvertGun(__instance);
			return false;
		}
	}

	// The same for the magazines in the rack: they become cartridges before the rack counts and copies them.
	[HarmonyPatch(typeof(MagazineManager), "Awake")]
	internal static class RackAwakePatch
	{
		private static void Prefix(MagazineManager __instance)
		{
			if (RifleItem.IsEquipped)
			{
				Conversion.ConvertMagazines(__instance);
			}
		}
	}

	// A shot's damage is the fitted item's damage (through Gun.Damage), so for the length of the shot the item's damage is the
	// charged damage, and it is put back afterwards, even if the shot throws.
	[HarmonyPatch(typeof(Gun), "FireRay")]
	internal static class FireRayPatch
	{
		private static void Prefix(Gun __instance, out float __state)
		{
			__state = -1f;
			if (!(__instance is SniperGun rifle) || RifleItem.Item == null)
			{
				return;
			}
			__state = RifleItem.Item.damage;
			RifleItem.Item.damage = rifle.ShotDamage;
		}

		private static void Finalizer(float __state)
		{
			if (__state >= 0f && RifleItem.Item != null)
			{
				RifleItem.Item.damage = __state;
			}
		}
	}
}
