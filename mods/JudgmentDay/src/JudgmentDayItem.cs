using System.Collections.Generic;
using System.Linq;
using HalfGripTools;
using UnityEngine;

namespace JudgmentDay
{
	/// <summary>
	/// Judgment Day as an item: a Legendary gun card cloned from the game's pump shotgun item, so it has a gun slot, a tube that takes
	/// loose shells and a place in the shop and the save for free. Damage, pellets, spread, capacity and recoil are the shotgun's; only
	/// the price changes.
	/// </summary>
	internal static class JudgmentDayItem
	{
		public const string ItemId = "judgmentday.gun";

		private static GunUpgrade item;

		public static bool IsEquipped => item != null && PlayerStats.gunUpgrade == item;

		public static GunUpgrade Item => item;

		public static void Register(ModContext ctx)
		{
			ctx.Items.Add(ItemId, Create);
		}

		private static GunUpgrade Create(ItemCatalog catalog)
		{
			List<GunUpgrade> guns = catalog.items.OfType<GunUpgrade>().Where(g => g != null).ToList();
			if (Mod.DumpInfo.Value)
			{
				foreach (GunUpgrade g in guns)
				{
					Mod.Log.LogInfo($"[gun item] name='{g.name}' id='{g.id}' type={g.gunType} tier={g.tier} cost={g.cost} damage={g.damage} capacity={g.capacity} magazines={g.magazines} pellets={g.projectilesPerShot}");
				}
			}
			GunUpgrade shotgun = guns.FirstOrDefault(g => g.gunType == GunType.SHOTGUN);
			if (shotgun == null)
			{
				Mod.Log.LogError("No pump shotgun item found in the catalog, so there is nothing to base Judgment Day on.");
				return null;
			}

			GunUpgrade made = Mod.Ctx.Items.Clone(shotgun, ItemId);
			made.name = "Judgment Day";
			made.description = "Judgment Day\n<color=#7e7e7e>I'll be back.</color>"
				+ "\n<color=#6fb7b4>Circle the mouse while aiming to spin-cock it</color>"
				+ "\n<color=#6fb7b4>Or work the lever by hand</color>";
			made.secondaryDescription = "";
			made.gunType = GunType.SHOTGUN;
			made.tier = ItemTier.Legendary;
			made.disabled = false;
			made.startsLocked = false;
			made.cost = Mathf.Max(1, Mathf.RoundToInt(shotgun.cost * Mod.CostMultiplier.Value));

			if (shotgun.image != null)
			{
				Sprite icon = Mod.Ctx.Sprites.LoadSprite("jd_icon.png", shotgun.image);
				if (icon != null)
				{
					made.image = icon;
				}
			}

			Mod.Log.LogInfo($"Created Judgment Day from '{shotgun.name}': damage {made.damage}, pellets {made.projectilesPerShot}, capacity {made.capacity}, cost {made.cost} (shotgun {shotgun.cost})");
			item = made;
			return made;
		}
	}
}
