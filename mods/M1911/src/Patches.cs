using HarmonyLib;

namespace HalfGrip1911
{
	// The M1911 rides on the SMG's gun object. When it is the fitted item, make that gun semi-auto and redress it.
	[HarmonyPatch(typeof(NAutomaticGun), "Start")]
	internal static class AutomaticGunStartPatch
	{
		private static void Postfix(NAutomaticGun __instance)
		{
			if (M1911Item.IsFitted(__instance))
			{
				M1911Item.Equip(__instance);
			}
		}
	}
}
