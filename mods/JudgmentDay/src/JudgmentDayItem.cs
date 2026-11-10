using System.Collections.Generic;
using System.Linq;
using HalfGripTools;
using UnityEngine;

namespace JudgmentDay
{
	/// <summary>
	/// Judgment Day as an item: a Legendary gun card cloned from the game's pump shotgun item, so it has a gun slot, a tube that takes
	/// loose shells and a place in the shop and the save for free. Pellets, spread and recoil are the shotgun's; damage, capacity, spare
	/// shells and the tier (Race-spec) are set from the config, and the price is the shotgun's times CostMultiplier.
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
				+ "\n<color=#6fb7b4>Or work the lever by hand</color>"
				+ "\n<color=#6fb7b4>Shells go in with the lever open</color>";
			made.secondaryDescription = "";
			made.gunType = GunType.SHOTGUN;
			made.tier = ItemTier.RaceSpec;
			made.damage = Mod.Damage.Value;
			made.capacity = Mod.TubeShells.Value + 1;     // the game counts the tube and the chamber together
			made.magazines = Mod.StartShells.Value;
			made.disabled = false;
			made.startsLocked = false;
			made.cost = Mathf.Max(1, Mathf.RoundToInt(shotgun.cost * Mod.CostMultiplier.Value));

			if (shotgun.image != null)
			{
				// The icon is finer than the game's 45x11 cards (63x19): its pixel density is raised in proportion so it keeps the card's width.
				Texture2D iconTexture = Mod.Ctx.Sprites.LoadTexture("jd_icon.png");
				float density = iconTexture != null ? shotgun.image.pixelsPerUnit * iconTexture.width / shotgun.image.rect.width : 0f;
				Sprite icon = Mod.Ctx.Sprites.LoadSprite("jd_icon.png", shotgun.image, density);
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
