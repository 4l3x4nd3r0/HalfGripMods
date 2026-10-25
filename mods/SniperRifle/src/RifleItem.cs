using System;
using HalfGripTools;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HalfGripSniper
{
	/// <summary>
	/// The Sniper Rifle as an item: a Legendary gun card cloned from the game's SMG item, so that it has a gun slot, a rack and a place
	/// in the shop and the save for free. What the item does in a race is the <see cref="SniperGun"/>'s business.
	/// </summary>
	internal static class RifleItem
	{
		public const string ItemId = "sniperrifle.rifle";

		private static GunUpgrade item;

		private static bool dumped;

		/// <summary>True while the rifle is the gun the player has equipped.</summary>
		public static bool IsEquipped => item != null && PlayerStats.gunUpgrade == item;

		/// <summary>The item, once it has been made.</summary>
		public static GunUpgrade Item => item;

		/// <summary>Hands the rifle to HalfGripTools, which puts it in the item catalog before the game builds its id lookup.</summary>
		public static void Register(ModContext ctx)
		{
			ctx.Items.Add(ItemId, Create);
		}

		private static GunUpgrade Create(ItemCatalog catalog)
		{
			List<GunUpgrade> guns = catalog.items.OfType<GunUpgrade>().Where(g => g != null).ToList();
			if (Mod.DumpInfo.Value && !dumped)
			{
				dumped = true;
				foreach (GunUpgrade g in guns)
				{
					Mod.Log.LogInfo($"[gun item] name='{g.name}' id='{g.id}' type={g.gunType} tier={g.tier} cost={g.cost} damage={g.damage} fireRate={g.fireRate} recoil={g.recoil} inaccuracy={g.inaccuracy} capacity={g.capacity} magazines={g.magazines} pellets={g.projectilesPerShot}");
				}
			}
			GunUpgrade smg = guns.FirstOrDefault(g => g.gunType == GunType.SMG_RIFLE);
			if (smg == null)
			{
				Mod.Log.LogError("No SMG gun item found in the catalog, so there is nothing to base the Sniper Rifle on.");
				return null;
			}
			GunUpgrade revolver = guns.FirstOrDefault(g => g.gunType == GunType.REVOLVER) ?? smg;

			GunUpgrade made = Mod.Ctx.Items.Clone(smg, ItemId);
			made.name = "Sniper Rifle";
			// One short row per line, each in the card's teal, the way the RPG's card does it.
			int quick = Mathf.RoundToInt(Mod.BaseDamage.Value);
			string[] rows =
			{
				"Press {Z} to scope in and charge up",
				quick + " to " + Mathf.RoundToInt(Mod.FullChargeDamage.Value) + " damage in " + Mod.ChargeTime.Value.ToString("0.#") + " s, " + quick + " unscoped",
				"Bolt-Action"
			};
			made.description = "Sniper Rifle\n<color=#7e7e7e>Professionals have standards.</color>";
			foreach (string row in rows)
			{
				made.description += "\n<color=#6fb7b4>" + row + "</color>";
			}
			made.secondaryDescription = "";
			made.gunType = GunType.SMG_RIFLE;
			made.tier = ItemTier.Legendary;
			made.disabled = false;
			made.startsLocked = false;
			made.cost = Mathf.Max(smg.cost, revolver.cost) * 3;

			// One round a load. The card shows the full charge; SniperGun scales the damage down to what the charge really is.
			made.damage = Mathf.Max(1f, Mod.FullChargeDamage.Value);
			made.recoil = revolver.recoil * 1.6f;
			made.inaccuracy = 0f;
			made.fireRate = 0.5f;
			made.capacity = 1;
			made.magazines = Mathf.Max(1, Mod.Rounds.Value);
			made.projectilesPerShot = 1;
			made.projectileSpread = 0f;
			made.ammoWord = "round|rounds";

			made.muzzleFlash = revolver.muzzleFlash;
			made.shakeIntensity = revolver.shakeIntensity * 1.3f;
			made.shakeLength = revolver.shakeLength * 1.3f;
			made.shakeSharpness = revolver.shakeSharpness;

			if (made.forceHide == null)
			{
				made.forceHide = new List<string>();
			}
			made.forceHide.Add("fire rate");

			Sprite icon = Mod.Ctx.Sprites.LoadSprite("sniper_icon.png", smg.image);
			if (icon != null)
			{
				made.image = icon;
			}
			Mod.Log.LogInfo($"Created Sniper Rifle from '{smg.name}': damage {Mod.BaseDamage.Value} to {made.damage} by charge, rounds={made.magazines}, cost={made.cost}");
			item = made;
			return made;
		}
	}
}
