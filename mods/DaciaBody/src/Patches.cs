using HarmonyLib;

namespace HalfGripDaciaBody
{
	// Awake, ApplySkin (the shop preview, refitting) and RandomizeSkin all end here, so this is where the shape follows the skin.
	[HarmonyPatch(typeof(CarSkinController), nameof(CarSkinController.EquipCurrentSkin))]
	internal static class EquipCurrentSkinPatch
	{
		private static void Postfix(CarSkinController __instance)
		{
			try
			{
				Model.Refit(__instance);
			}
			catch (System.Exception e)
			{
				Mod.Log.LogError("Refit failed: " + e);
			}
		}
	}
}
