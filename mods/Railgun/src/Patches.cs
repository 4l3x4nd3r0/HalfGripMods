using HarmonyLib;
using UnityEngine;

namespace HalfGripRailgun
{
	// The pump shotgun keeps doing the loading and chambering; with the Railgun fitted a RailCharge is put on it as it wakes.
	[HarmonyPatch(typeof(NPumpActionGun), "Awake")]
	internal static class PumpAwakePatch
	{
		private static void Postfix(NPumpActionGun __instance)
		{
			if (RailgunItem.IsEquipped && __instance.RequiredGunType == GunType.SHOTGUN && __instance.GetComponent<RailCharge>() == null)
			{
				__instance.gameObject.AddComponent<RailCharge>().Setup(__instance);
			}
		}
	}

	// A shot's damage is the fitted item's damage (through Gun.Damage), so for the length of the shot the item's damage is the
	// charged damage, and it is put back afterwards, even if the shot throws. The beam's width follows the charge. The item that is
	// changed is the one the gun itself reads (Gun.Fitted), not a copy kept by the mod, so the shot cannot silently keep the card's
	// full damage.
	[HarmonyPatch(typeof(Gun), "FireRay")]
	internal static class FireRayPatch
	{
		private static int logged;

		internal class Swap
		{
			public GunUpgrade Item;

			public float Damage;
		}

		private static void Prefix(Gun __instance, out Swap __state)
		{
			__state = null;
			RailCharge charge = __instance.GetComponent<RailCharge>();
			GunUpgrade fitted = __instance.Fitted;
			if (charge == null || fitted == null)
			{
				return;
			}
			__state = new Swap { Item = fitted, Damage = fitted.damage };
			fitted.damage = charge.ShotDamage;
			charge.ShapeBeam();
			// The first few shots say what really left the gun: the charged damage, and what the game then works out from it.
			if (Mod.DumpInfo.Value && logged < 12)
			{
				logged++;
				Mod.Log.LogInfo($"Railgun shot: charge {charge.Charge:0.00}, item damage {fitted.damage:0.#} (card {__state.Damage:0.#}), Gun.Damage {__instance.Damage:0.#}, same item as the mod's: {ReferenceEquals(fitted, RailgunItem.Item)}");
			}
		}

		private static void Finalizer(Swap __state, Gun __instance)
		{
			if (__state != null)
			{
				__state.Item.damage = __state.Damage;
				RailCharge charge = __instance.GetComponent<RailCharge>();
				if (charge != null)
				{
					charge.Spend();
				}
			}
		}
	}
}
