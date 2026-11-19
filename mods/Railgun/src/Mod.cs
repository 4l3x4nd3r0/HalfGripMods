using HalfGripTools;
using UnityEngine;

namespace HalfGripRailgun
{
	public class Mod : HalfGripMod
	{
		internal static ModLog Log;

		internal static ModContext Ctx;

		internal static ModSetting<float> MinDamage;

		internal static ModSetting<float> MaxDamage;

		internal static ModSetting<float> ChargeSeconds;

		internal static ModSetting<float> StillChargeSeconds;

		internal static ModSetting<float> FullSpeed;

		internal static ModSetting<float> DrainShare;

		internal static ModSetting<int> Cost;

		internal static ModSetting<float> GunScale;

		internal static ModSetting<float> HeldScale;

		internal static ModSetting<float> Lift;

		internal static ModSetting<int> ConfigVersion;

		internal static ModSetting<bool> ScreenBar;

		internal static ModSetting<bool> DumpInfo;

		/// <summary>The damage one full capacitor adds.</summary>
		internal static float PerCapacitor => (MaxDamage.Value - MinDamage.Value) / RailLayout.Coils;

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;
			Ctx = ctx;

			MinDamage = ctx.Config.Bind("Railgun", "MinDamage", 10f, "Damage of a shot with no capacitor full. Every full capacitor adds a sixth of the way up to MaxDamage. For scale: the SMG does 7 a bullet, the revolver 35; a car starts a race with about 100 health.");
			MaxDamage = ctx.Config.Bind("Railgun", "MaxDamage", 90f, "Damage of a shot with all six capacitors full. Shown on the shop card.");
			ChargeSeconds = ctx.Config.Bind("Railgun", "ChargeSeconds", 2f, "Seconds to fill all six capacitors while driving at FullSpeed, with the gun raised and a ball chambered. Each capacitor is a level of charge: a half-filled one does not count.");
			StillChargeSeconds = ctx.Config.Bind("Railgun", "StillChargeSeconds", 5f, "Seconds to fill all six capacitors when the car is standing still. Between standing still and FullSpeed the charging speeds up with the car's speed.");
			FullSpeed = ctx.Config.Bind("Railgun", "FullSpeed", 68.6f, "The car speed, in game units a second, at which the gun charges as fast as it can. 68.6 is the top speed of a stock car.");
			DrainShare = ctx.Config.Bind("Railgun", "DrainShare", 0.5f, "How fast the charge drains once the gun is lowered, as a share of the fastest it fills (ChargeSeconds). 0.5 empties it in twice that.");
			Cost = ctx.Config.Bind("Railgun", "Cost", 70, "Shop price.");
			GunScale = ctx.Config.Bind("Railgun", "GunScale", 1f, "How big the gun is drawn on the roof lining in the cockpit, as a share of the shotgun it replaces. 1 is the shotgun's size; its art is drawn to stay clear of the mirror and the leaderboard at 1.");
			HeldScale = ctx.Config.Bind("Railgun", "HeldScale", 1f, "How big the gun is in your hands while you aim, as a share of the shotgun's. 1 is the shotgun's own size.");
			Lift = ctx.Config.Bind("Railgun", "CockpitLift", 5f, "How many of the gun's pixels to lift it on the roof lining in the cockpit, so the battery and cable do not disappear behind the rear-view mirror. 0 leaves it where the shotgun sits.");
			ScreenBar = ctx.Config.Bind("Railgun", "ScreenBar", true, "Draw a thin charge bar under the crosshair while the gun is raised.");
			DumpInfo = ctx.Config.Bind("Debug", "DumpInfo", true, "Write the game's gun items and the gun's parts to the log. Useful when tuning.");

			// The config file keeps a value already in it when a default changes, so after a retune the old numbers would linger.
			ConfigVersion = ctx.Config.Bind("Debug", "ConfigVersion", 0, "Used by the plugin to reset retuned defaults after an update. Leave it.");
			if (ConfigVersion.Value < 7)
			{
				GunScale.Value = (float)GunScale.DefaultValue;
				MinDamage.Value = (float)MinDamage.DefaultValue;
				MaxDamage.Value = (float)MaxDamage.DefaultValue;
				ChargeSeconds.Value = (float)ChargeSeconds.DefaultValue;
				StillChargeSeconds.Value = (float)StillChargeSeconds.DefaultValue;
				FullSpeed.Value = (float)FullSpeed.DefaultValue;
				DrainShare.Value = (float)DrainShare.DefaultValue;
				HeldScale.Value = (float)HeldScale.DefaultValue;
				ConfigVersion.Value = 7;
				ctx.Config.Save();
			}

			RailgunItem.Register(ctx);
			ctx.PatchAll();
		}

		public override void Unload()
		{
			foreach (RailCharge charge in Object.FindObjectsByType<RailCharge>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				charge.Restore();
				Object.Destroy(charge);
			}
		}
	}
}
