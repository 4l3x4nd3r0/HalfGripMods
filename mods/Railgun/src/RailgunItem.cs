using System.Collections.Generic;
using System.Linq;
using HalfGripTools;
using UnityEngine;

namespace HalfGripRailgun
{
	/// <summary>
	/// The Railgun as an item: a Legendary gun card cloned from the game's pump shotgun item, so that it has a gun slot, a tube that
	/// takes loose rounds one at a time and a place in the shop and the save for free.
	/// </summary>
	internal static class RailgunItem
	{
		public const string ItemId = "railgun.gun";

		private static GunUpgrade item;

		private static bool dumped;

		public static bool IsEquipped => item != null && PlayerStats.gunUpgrade == item;

		public static GunUpgrade Item => item;

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
					Mod.Log.LogInfo($"[gun item] name='{g.name}' id='{g.id}' type={g.gunType} tier={g.tier} cost={g.cost} damage={g.damage} capacity={g.capacity} magazines={g.magazines} pellets={g.projectilesPerShot}");
				}
			}
			GunUpgrade shotgun = guns.FirstOrDefault(g => g.gunType == GunType.SHOTGUN);
			if (shotgun == null)
			{
				Mod.Log.LogError("No pump shotgun item found in the catalog, so there is nothing to base the Railgun on.");
				return null;
			}

			GunUpgrade made = Mod.Ctx.Items.Clone(shotgun, ItemId);
			made.name = "Railgun";
			string[] rows =
			{
				"Charges while driving",
				Mathf.RoundToInt(Mod.MinDamage.Value) + " damage",
				"+" + Mathf.RoundToInt(Mod.PerCapacitor) + " damage per loaded capacitor",
				RailLayout.Coils + " capacitors"
			};
			made.description = "Railgun\n<color=#7e7e7e>Physics is a good enough reason.</color>";
			foreach (string row in rows)
			{
				made.description += "\n<color=#6fb7b4>" + row + "</color>";
			}
			made.secondaryDescription = "";
			made.gunType = GunType.SHOTGUN;
			made.tier = ItemTier.Legendary;
			made.disabled = false;
			made.startsLocked = false;
			made.cost = Mathf.Max(1, Mod.Cost.Value);

			// The card shows the full charge; RailCharge scales the damage to what the charge really is.
			made.damage = Mathf.Max(1f, Mod.MaxDamage.Value);
			made.projectilesPerShot = 1;
			made.projectileSpread = 0f;
			made.inaccuracy = 0f;
			made.capacity = 4;
			made.ammoWord = "ball|balls";
			made.recoil = shotgun.recoil * 0.8f;

			if (shotgun.image != null)
			{
				Mod.Log.LogInfo($"Shotgun card icon '{shotgun.image.name}' is {shotgun.image.rect.width}x{shotgun.image.rect.height}");
				Sprite icon = Mod.Ctx.Sprites.LoadSprite("railgun_icon.png", shotgun.image);
				if (icon != null)
				{
					made.image = icon;
				}
			}

			Mod.Log.LogInfo($"Created Railgun from '{shotgun.name}': damage {Mod.MinDamage.Value} to {made.damage} by charge, capacity={made.capacity}, magazines={made.magazines}, cost={made.cost}");
			item = made;
			return made;
		}
	}
}
