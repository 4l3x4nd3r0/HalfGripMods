using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HalfGripMinigun
{
	// The Minigun rides on the SMG's gun object. When it is the fitted item, the gun is redressed and given a heat meter.
	[HarmonyPatch(typeof(NAutomaticGun), "Start")]
	internal static class GunStartPatch
	{
		private static void Postfix(NAutomaticGun __instance)
		{
			if (MinigunItem.IsFitted(__instance))
			{
				MinigunItem.Equip(__instance);
			}
		}
	}

	// The remote can be raised unless the gun is jammed: there is no chamber to be empty.
	[HarmonyPatch(typeof(NAutomaticGun), "CanEnterAimMode")]
	internal static class AimPatch
	{
		private static bool Prefix(NAutomaticGun __instance, ref bool __result)
		{
			if (!MinigunItem.IsFitted(__instance))
			{
				return true;
			}
			MiniHeat heat = __instance.GetComponent<MiniHeat>();
			__result = heat == null || heat.CanAim;
			return false;
		}
	}

	// Never "out of ammo": the game's checks for a full gun and for rounds left would otherwise look at the (missing) magazine.
	[HarmonyPatch(typeof(NAutomaticGun), "IsFull")]
	internal static class FullPatch
	{
		private static bool Prefix(NAutomaticGun __instance, ref bool __result)
		{
			if (!MinigunItem.IsFitted(__instance))
			{
				return true;
			}
			__result = true;
			return false;
		}
	}

	[HarmonyPatch(typeof(NAutomaticGun), "ShotsLeft")]
	internal static class ShotsLeftPatch
	{
		private static bool Prefix(NAutomaticGun __instance, ref int __result)
		{
			if (!MinigunItem.IsFitted(__instance))
			{
				return true;
			}
			__result = int.MaxValue;
			return false;
		}
	}

	// The trigger: the game calls Shoot every frame the button is held; the heat meter decides whether a bullet leaves.
	[HarmonyPatch(typeof(NAutomaticGun), nameof(NAutomaticGun.Shoot))]
	internal static class ShootPatch
	{
		private static bool Prefix(NAutomaticGun __instance)
		{
			if (!MinigunItem.IsFitted(__instance))
			{
				return true;
			}
			MiniHeat heat = __instance.GetComponent<MiniHeat>();
			if (heat != null)
			{
				heat.Pull();
			}
			return false;
		}
	}

	// The rack that held the SMG's magazines holds nothing for the Minigun: its rounds are removed as the race starts, and a heat
	// bar is drawn where they sat.
	[HarmonyPatch(typeof(MagazineManager), "Start")]
	internal static class RackStartPatch
	{
		private static void Postfix(MagazineManager __instance)
		{
			if (!RackPatches.Owns(__instance))
			{
				return;
			}
			try
			{
				RackPatches.Empty(__instance);
			}
			catch (Exception e)
			{
				Mod.Log.LogError("Could not clear the magazine rack for the Minigun: " + e);
			}
		}
	}

	// Magazines the game would put in the rack (ammo boxes, kills, refills) cool the gun instead. Ammo boxes are the only way
	// to take heat off while firing.
	[HarmonyPatch(typeof(MagazineManager), nameof(MagazineManager.Grant))]
	internal static class GrantPatch
	{
		private static bool Prefix(MagazineManager __instance, int magazines, ref int __result)
		{
			if (!RackPatches.Owns(__instance))
			{
				return true;
			}
			int wanted = Mathf.Max(1, Mathf.RoundToInt(Stats.Get(Stat.AmmoPickup, magazines)));
			MiniHeat heat = MiniHeat.Current;
			__result = heat != null && heat.CoolByMagazines(wanted) ? wanted : 0;
			return false;
		}
	}

	[HarmonyPatch(typeof(MagazineManager), "Spawn")]
	internal static class SpawnPatch
	{
		private static bool Prefix(MagazineManager __instance, ref int __result)
		{
			if (!RackPatches.Owns(__instance))
			{
				return true;
			}
			__result = 0;
			return false;
		}
	}

	[HarmonyPatch(typeof(MagazineManager), "SpawnEntities")]
	internal static class SpawnEntitiesPatch
	{
		private static bool Prefix(MagazineManager __instance, ref int __result)
		{
			if (!RackPatches.Owns(__instance))
			{
				return true;
			}
			__result = 0;
			return false;
		}
	}

	internal static class RackPatches
	{
		private static readonly System.Reflection.MethodInfo OwnerMethod = AccessTools.Method(typeof(MagazineManager), "Owner");

		public static bool Owns(MagazineManager rack)
		{
			if (MinigunItem.Item == null || OwnerMethod == null)
			{
				return false;
			}
			Gun owner = OwnerMethod.Invoke(rack, null) as Gun;
			return owner != null && owner.Fitted == MinigunItem.Item;
		}

		public static void Empty(MagazineManager rack)
		{
			var items = Traverse.Create(rack).Field("items").GetValue<List<NAmmo>>();
			// Where the magazines sat, for the heat bar.
			bool any = false;
			Bounds area = new Bounds();
			int layer = rack.gameObject.layer;
			int sortingLayer = 0;
			int order = 0;
			foreach (NAmmo ammo in items)
			{
				if (ammo == null)
				{
					continue;
				}
				foreach (SpriteRenderer sprite in ammo.GetComponentsInChildren<SpriteRenderer>(true))
				{
					if (!any)
					{
						area = sprite.bounds;
						layer = sprite.gameObject.layer;
						sortingLayer = sprite.sortingLayerID;
						order = sprite.sortingOrder;
						any = true;
					}
					else
					{
						area.Encapsulate(sprite.bounds);
					}
				}
			}
			foreach (NAmmo ammo in items.ToArray())
			{
				if (ammo != null)
				{
					UnityEngine.Object.Destroy(ammo.gameObject);
				}
			}
			items.Clear();
			ClearList(rack, "homeSlots");
			ClearList(rack, "grabbedLastFrame");
			ClearList(rack, "grabbableWhenOpen");
			var slots = Traverse.Create(rack).Field("slots").GetValue<IList>();
			if (slots != null)
			{
				foreach (object slot in slots)
				{
					Traverse.Create(slot).Field("occupant").SetValue(null);
				}
			}
			if (any)
			{
				HeatBar.Make(rack.transform, area, layer, sortingLayer, order + 1);
			}
			// The padlock the game puts on the rack for the opening ceasefire goes onto the remote instead, as it does for the
			// blood-fed SMG (the Bloodletter), which has no rack either.
			System.Reflection.MethodInfo ride = AccessTools.Method(typeof(MagazineManager), "RideLockOnGun");
			if (ride != null)
			{
				ride.Invoke(rack, null);
			}
			else
			{
				Mod.Log.LogWarning("The rack had no magazines to take the heat bar's place from, so there is no bar");
			}
			Mod.Log.LogInfo($"Emptied rack '{rack.name}' for the Minigun (bar {(any ? area.size.ToString() : "none")})");
		}

		private static void ClearList(MagazineManager rack, string name)
		{
			object value = Traverse.Create(rack).Field(name).GetValue();
			if (value is IList list)
			{
				list.Clear();
			}
			else if (value != null)
			{
				Traverse.Create(value).Method("Clear").GetValue();
			}
		}
	}
}
