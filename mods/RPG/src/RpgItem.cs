using System;
using HalfGripTools;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HalfGripRpg
{
	/// <summary>
	/// The RPG as an item: a Legendary gun card cloned from the game's SMG item, so that it has a gun slot, a rack and a place in the
	/// shop and the save for free. What the item does in a race is the <see cref="RpgLauncher"/>'s business.
	/// </summary>
	internal static class RpgItem
	{
		public const string ItemId = "rpg";

		private static GunUpgrade item;

		/// <summary>True while the RPG is the gun the player has equipped.</summary>
		public static bool IsEquipped => item != null && PlayerStats.gunUpgrade == item;

		/// <summary>Hands the RPG to HalfGripTools, which puts it in the item catalog before the game builds its id lookup.</summary>
		public static void Register(ModContext ctx)
		{
			ctx.Items.Add(ItemId, Create);
		}

		private static GunUpgrade Create(ItemCatalog catalog)
		{
			List<GunUpgrade> guns = catalog.items.OfType<GunUpgrade>().Where(g => g != null).ToList();
			GunUpgrade smg = guns.FirstOrDefault(g => g.gunType == GunType.SMG_RIFLE);
			if (smg == null)
			{
				Mod.Log.LogError("No SMG gun item found in the catalog, so there is nothing to base the RPG on.");
				return null;
			}
			GunUpgrade revolver = guns.FirstOrDefault(g => g.gunType == GunType.REVOLVER) ?? smg;

			GunUpgrade made = Mod.Ctx.Items.Clone(smg, ItemId);
			made.description = "RPG\n<color=#7e7e7e>Kaboom, but bigger</color>\n<color=#6fb7b4>Rockets blow up on impact and hurt everything around them</color>";
			made.secondaryDescription = "";
			made.gunType = GunType.SMG_RIFLE;
			made.tier = ItemTier.Legendary;
			made.disabled = false;
			made.startsLocked = false;
			made.cost = Mathf.Max(smg.cost, revolver.cost) * 3;

			// One rocket a load. The card's damage is the damage at the centre of the blast.
			made.damage = Mathf.Max(1f, Mod.ExplosionDamage.Value);
			made.recoil = revolver.recoil * 1.4f;
			made.inaccuracy = 0f;
			made.fireRate = 0.5f;
			made.capacity = 1;
			made.magazines = Mathf.Max(1, Mod.Rockets.Value);
			made.projectilesPerShot = 1;
			made.projectileSpread = 0f;
			made.ammoWord = "rocket|rockets";

			made.muzzleFlash = revolver.muzzleFlash;
			made.shakeIntensity = revolver.shakeIntensity * 1.5f;
			made.shakeLength = revolver.shakeLength * 1.5f;
			made.shakeSharpness = revolver.shakeSharpness;

			if (made.forceHide == null)
			{
				made.forceHide = new List<string>();
			}
			made.forceHide.Add("fire rate");

			Sprite icon = Mod.Ctx.Sprites.LoadSprite("rpg_icon.png", smg.image);
			if (icon != null)
			{
				made.image = icon;
			}
			item = made;
			Mod.Log.LogInfo($"Created RPG from '{smg.name}': blast damage={made.damage} full damage out to {2f * Mod.CarLength.Value} rockets={made.magazines}");
			return made;
		}
	}
}
