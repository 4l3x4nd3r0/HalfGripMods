using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HalfGripMinigun
{
	/// <summary>
	/// The roof turret on the car in the garage. The garage's own preview car shows parts through each item's exterior prefab, which
	/// the Minigun does not have (its turret is built in code). So the turret is put in the preview's own bookkeeping right after
	/// the preview rebuilds: it joins the lists the preview keeps for its parts, and the preview then removes it, with its fly-away
	/// animation, at the same moment as any other part when the item leaves the slot, and flies it in when the item is dropped in.
	/// It is the same stock garage for a solo run and for a Multiplayer room.
	/// </summary>
	internal static class GarageTurret
	{
		private static readonly System.Reflection.FieldInfo Shown = AccessTools.Field(typeof(LoadoutPreview), "shown");

		private static readonly System.Reflection.FieldInfo Spawned = AccessTools.Field(typeof(LoadoutPreview), "spawned");

		private static readonly System.Reflection.FieldInfo SpawnedFor = AccessTools.Field(typeof(LoadoutPreview), "spawnedFor");

		private static readonly System.Reflection.FieldInfo Settled = AccessTools.Field(typeof(LoadoutPreview), "settled");

		private static readonly System.Reflection.MethodInfo FlyIn = AccessTools.Method(typeof(LoadoutPreview), "FlyIn");

		internal static bool MinigunShown(LoadoutPreview preview)
		{
			if (MinigunItem.Item == null || Shown == null)
			{
				return false;
			}
			var shown = Shown.GetValue(preview) as List<UpgradeComponent>;
			return shown != null && shown.Contains(MinigunItem.Item);
		}

		internal static void Build(LoadoutPreview preview, bool wasShown)
		{
			var spawned = Spawned?.GetValue(preview) as List<GameObject>;
			var spawnedFor = SpawnedFor?.GetValue(preview) as List<UpgradeComponent>;
			if (spawned == null || spawnedFor == null || !MinigunShown(preview) || !Mod.RoofGunVisible.Value)
			{
				return;
			}
			Transform car = preview.skinController != null ? preview.skinController.transform : preview.transform;
			RoofGun roof = RoofGun.BuildOn(car, null, null);
			if (roof == null)
			{
				return;
			}
			spawned.Add(roof.gameObject);
			spawnedFor.Add(MinigunItem.Item);
			bool settled = Settled != null && (bool)Settled.GetValue(preview);
			if (!wasShown && settled && FlyIn != null)
			{
				FlyIn.Invoke(preview, new object[] { roof.transform });
			}
		}

		/// <summary>Takes every roof turret away (the mod is switched off).</summary>
		internal static void Cleanup()
		{
			foreach (RoofGun roof in Object.FindObjectsByType<RoofGun>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				Object.Destroy(roof.gameObject);
			}
		}
	}

	// The preview rebuilds its parts whenever the equipped items change, and once when the garage opens. The turret follows each rebuild.
	[HarmonyPatch(typeof(LoadoutPreview), "Rebuild")]
	internal static class PreviewRebuildPatch
	{
		private static void Prefix(LoadoutPreview __instance, out bool __state)
		{
			__state = GarageTurret.MinigunShown(__instance);
		}

		private static void Postfix(LoadoutPreview __instance, bool __state)
		{
			GarageTurret.Build(__instance, __state);
		}
	}
}
