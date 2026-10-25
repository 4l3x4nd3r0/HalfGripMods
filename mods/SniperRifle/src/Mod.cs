using BepInEx.Configuration;
using BepInEx.Logging;
using HalfGripTools;
using UnityEngine;

namespace HalfGripSniper
{
	public class Mod : HalfGripMod
	{
		internal static ManualLogSource Log;

		internal static ModContext Ctx;

		internal static ConfigEntry<int> Rounds;

		internal static ConfigEntry<float> BaseDamage;

		internal static ConfigEntry<float> HalfChargeDamage;

		internal static ConfigEntry<float> FullChargeDamage;

		internal static ConfigEntry<float> ChargeDelay;

		internal static ConfigEntry<float> ChargeTime;

		internal static ConfigEntry<float> Magnification;

		internal static ConfigEntry<float> ZoomSpeed;

		internal static ConfigEntry<bool> LaserDot;

		internal static ConfigEntry<bool> ShowScope;

		internal static ConfigEntry<bool> LowerAfterShot;

		internal static ConfigEntry<bool> DumpInfo;

		internal static ConfigEntry<int> ConfigVersion;

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;
			Ctx = ctx;

			if (!ModKeyShim.Supported(ctx))
			{
				Log.LogError("Sniper Rifle needs HalfGripTools 1.0.2 or newer (this one is older), so it is not loaded. Update HalfGripTools.");
				return;
			}
			ModKeyShim.Add(ctx, KeyCode.Z, "Scope in / out (Sniper Rifle, raised)", Controls.Section.Looking);

			Rounds = ctx.Config.Bind("Sniper Rifle", "Rounds", 4, "Cartridges on the rack at the start of a race. Each is loaded by hand, one at a time. Every extra magazine the car gets (items, pickups, kills) is worth 2 more.");
			BaseDamage = ctx.Config.Bind("Sniper Rifle", "BaseDamage", 50f, "Damage of a quick shot, fired with no charge. For scale: the SMG does 7 a bullet, the revolver 35, the slug shotgun 60; a car starts a race with about 100 health.");
			HalfChargeDamage = ctx.Config.Bind("Sniper Rifle", "HalfChargeDamage", 80f, "Damage with the charge bar half full.");
			FullChargeDamage = ctx.Config.Bind("Sniper Rifle", "FullChargeDamage", 100f, "Damage with the charge bar full: enough for a car on its starting health. Shown on the shop card.");
			ChargeDelay = ctx.Config.Bind("Sniper Rifle", "ChargeDelay", 0.15f, "Seconds after raising the rifle before the charge starts to build.");
			ChargeTime = ctx.Config.Bind("Sniper Rifle", "ChargeTime", 1.2f, "Seconds the charge takes to fill once it has started (TF2's is about 3.3, but you are driving).");
			Magnification = ctx.Config.Bind("Sniper Rifle", "Magnification", 2.5f, "How much the view is zoomed in while the rifle is raised. 1 turns the zoom off.");
			ZoomSpeed = ctx.Config.Bind("Sniper Rifle", "ZoomSpeed", 10f, "How fast the view zooms in and out. Higher is snappier.");
			ShowScope = ctx.Config.Bind("Sniper Rifle", "ScopeOverlay", true, "Black out everything outside a round lens while the rifle is raised and draw a reticle (with a charge bar) at the aim point.");
			LaserDot = ctx.Config.Bind("Sniper Rifle", "LaserDot", true, "Show a red dot where the rifle is aimed, brighter as the charge builds. It does nothing but look the part.");
			LowerAfterShot = ctx.Config.Bind("Sniper Rifle", "LowerAfterShot", true, "Lower the rifle after a shot so you can work the bolt straight away.");
			DumpInfo = ctx.Config.Bind("Debug", "DumpInfo", true, "Write extra detail about the rifle's parts to the BepInEx log. Useful when tuning.");

			// BepInEx keeps a value already in the config file when a default changes, so after a retune the old numbers would linger.
			// Bumping this brings the tuned entries back to their new defaults once, and leaves anything changed afterwards alone.
			ConfigVersion = ctx.Config.Bind("Debug", "ConfigVersion", 0, "Used by the plugin to reset retuned defaults after an update. Leave it.");
			if (ConfigVersion.Value < 5)
			{
				Rounds.Value = (int)Rounds.DefaultValue;
				BaseDamage.Value = (float)BaseDamage.DefaultValue;
				HalfChargeDamage.Value = (float)HalfChargeDamage.DefaultValue;
				FullChargeDamage.Value = (float)FullChargeDamage.DefaultValue;
				ChargeDelay.Value = (float)ChargeDelay.DefaultValue;
				ChargeTime.Value = (float)ChargeTime.DefaultValue;
				Magnification.Value = (float)Magnification.DefaultValue;
				ConfigVersion.Value = 5;
				ctx.Config.Save();
			}

			RifleItem.Register(ctx);
			// One patch that no longer fits the game is logged and skipped, instead of taking the whole mod down with it.
			ctx.PatchAll();
		}

		// Mods switched off: take the scope, the dot and the rifle out of the scene.
		public override void Unload()
		{
			foreach (ScopeOverlay overlay in Object.FindObjectsByType<ScopeOverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				Object.Destroy(overlay.gameObject);
			}
			foreach (LaserDot dot in Object.FindObjectsByType<LaserDot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				Object.Destroy(dot.gameObject);
			}
		}
	}
}
