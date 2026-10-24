using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HalfGripTools;
using UnityEngine;

namespace HalfGripDaciaBody
{
	internal static class Body
	{
		private const string Id = "daciabody.legendary";
		private const string Name = "Dacia Body";
		private const string Flavour = "\"If I survived the Green Hell, nothing can kill me.\"";
		private const string Section = "Dacia Body";

		private static ConfigEntry<bool> enabled;
		private static ConfigEntry<int> cost;
		private static ConfigEntry<ItemTier> tier;
		private static ConfigEntry<float> maxHp;
		private static ConfigEntry<float> airDrag;
		private static ConfigEntry<float> lightGlow;
		private static ConfigEntry<float> headlightSetback;
		private static ConfigEntry<float> downForce;
		private static ConfigEntry<float> rollingFriction;
		private static ConfigEntry<bool> fwd;
		private static ConfigEntry<bool> awd;
		private static ConfigEntry<string> accent;
		private static ConfigEntry<string> mainColour;
		private static ConfigEntry<string> trim;

		/// <summary>Strength of the headlight and tail light glow while the Dacia is fitted, 0 to 1.</summary>
		internal static float LightGlow => lightGlow != null ? Mathf.Clamp01(lightGlow.Value) : 1f;

		/// <summary>How far back the headlight spot is moved while the Dacia is fitted, in the car's units. 0 leaves it where the game has it.</summary>
		internal static float HeadlightSetback => headlightSetback != null ? headlightSetback.Value : 0f;

		private static bool dumped;

		internal static void BindConfig(ConfigFile c)
		{
			enabled = c.Bind(Section, "Enabled", true, "Put the body in the item catalog (shop, rewards, collection).");
			cost = c.Bind(Section, "Cost", 45, "Shop price. Legendary parts cost about 35 in the base game.");
			tier = c.Bind(Section, "Tier", ItemTier.Legendary, "Stock, Modified, RaceSpec, Legendary or BlackMarket.");
			maxHp = c.Bind(Section, "MaxHp", 0.40f, "Percentage on max HP: 0.4 is +40%. Scrap Body gives +0.2.");
			lightGlow = c.Bind(Section, "LightGlow", 0.35f, "How strongly the headlights and tail lights glow while the Dacia is fitted: 1 is the game's own, 0 is dark. Lower it if the lamps bloom too much.");
			headlightSetback = c.Bind(Section, "HeadlightSetback", 0f, "Moves the headlight spot back by this much while the Dacia is fitted (the game puts it at the nose). Try 0.5 if it lights the bumper or fenders too hard; 0 leaves it alone.");
			airDrag = c.Bind(Section, "AirDrag", 1.25f, "Air resistance, stock is 1. Higher means slower at top speed.");
			downForce = c.Bind(Section, "DownForce", 1f, "Downforce, stock is 0 (Eagle Wing 1, Aero Tough 2).");
			rollingFriction = c.Bind(Section, "RollingFriction", 1f, "Rolling resistance, stock is 1.");
			fwd = c.Bind(Section, "FrontWheelDrive", true, "Front wheel drive, like the real Logan.");
			awd = c.Bind(Section, "AllWheelDrive", false, "All wheel drive (wins over front wheel drive).");
			mainColour = c.Bind(Section, "BodyColour", "#2A6FD6", "Main paint (blue). Only shows under the Default paint scheme.");
			accent = c.Bind(Section, "AccentColour", "#3CE65A", "Bumpers, skirts, mirrors and the grille frame (lime green).");
			trim = c.Bind(Section, "TrimColour", "#1B1B1F", "Grille, arch liners and wing (near black).");
		}

		/// <summary>Hands the body to HalfGripTools, which puts it in the item catalog before the game builds its id lookup.</summary>
		internal static void Register(ModContext ctx)
		{
			if (enabled.Value)
			{
				ctx.Items.Add(Id, Create);
			}
		}

		private static Color Parse(string hex, Color fallback)
		{
			return ColorUtility.TryParseHtmlString(hex, out Color colour) ? colour : fallback;
		}

		private static ChassisUpgrade Create(ItemCatalog catalog)
		{
			List<ChassisUpgrade> bodies = catalog.items.OfType<ChassisUpgrade>().Where(b => b != null).ToList();
			if (Mod.DumpInfo.Value && !dumped)
			{
				dumped = true;
				foreach (ChassisUpgrade b in bodies)
				{
					Mod.Log.LogInfo($"[body] name='{b.name}' id='{b.id}' tier={b.tier} cost={b.cost} roll={b.rollingFriction} air={b.airDrag} down={b.downForce} fwd={b.isFwd} awd={b.isAwd} skin={(b.carSkin != null ? b.carSkin.name + "#" + b.carSkin.skinIndex : "none")} boosts={string.Join(",", (b.statBoosts ?? new List<StatBoost>()).Select(s => s.stat + ":" + s.amount))}");
				}
			}

			ChassisUpgrade basis = bodies.FirstOrDefault(b => b.id == "aero_1") ?? bodies.FirstOrDefault();
			if (basis == null)
			{
				Mod.Log.LogError("No body (chassis) item found in the catalog, so there is nothing to base " + Name + " on.");
				return null;
			}

			ChassisUpgrade made = Mod.Ctx.Items.Clone(basis, Id);
			made.description = Name + "\n<color=#7e7e7e>" + Flavour + "</color>";
			made.interaction = "";
			made.secondaryDescription = "";
			made.tier = tier.Value;
			made.cost = cost.Value;
			made.disabled = false;
			made.startsLocked = false;
			made.unlockKind = UnlockKind.Discovery;
			made.hideStatLines = false;
			made.forceShow = new List<string>();
			made.forceHide = new List<string>();
			made.customSprites = new List<CustomSprite>();

			made.rollingFriction = rollingFriction.Value;
			made.airDrag = airDrag.Value;
			made.downForce = downForce.Value;
			made.isAwd = awd.Value;
			made.isFwd = fwd.Value && !awd.Value;

			made.statBoosts = new List<StatBoost>();
			if (maxHp.Value != 0f)
			{
				made.statBoosts.Add(new StatBoost { stat = Stat.MaxHp, amount = maxHp.Value });
			}

			if (basis.carSkin != null)
			{
				CarSkin skin = UnityEngine.Object.Instantiate(basis.carSkin);
				skin.hideFlags = HideFlags.HideAndDontSave;
				skin.name = Model.SkinName;
				skin.color1 = Parse(accent.Value, Color.green);
				skin.color2 = Parse(mainColour.Value, Color.blue);
				skin.color3 = Parse(trim.Value, Color.black);
				skin.skinIndex = Model.ModelIndex;
				if (Model.Textures(out Texture2D baseTexture, out Texture2D colourMask))
				{
					skin.baseTexture = baseTexture;
					skin.colorMask = colourMask;
				}
				made.carSkin = skin;
			}

			Sprite icon = Mod.Ctx.Sprites.LoadSprite("dacia_icon.png", basis.image);
			if (icon != null)
			{
				made.image = icon;
			}

			Mod.Log.LogInfo($"Created {Name} from '{basis.name}': {string.Join(", ", made.statBoosts.Select(b => b.stat + " " + b.amount.ToString("+0.##;-0.##")))} air={made.airDrag} down={made.downForce} fwd={made.isFwd}");
			return made;
		}
	}
}
