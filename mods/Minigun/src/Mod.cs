using BepInEx.Configuration;
using BepInEx.Logging;
using HalfGripTools;
using UnityEngine;

namespace HalfGripMinigun
{
	public class Mod : HalfGripMod
	{
		internal static ManualLogSource Log;

		internal static ModContext Ctx;

		internal static ConfigEntry<float> Damage;

		internal static ConfigEntry<float> RoundsPerSecond;

		internal static ConfigEntry<int> Magazines;

		internal static ConfigEntry<float> SecondsPerMagazine;

		internal static ConfigEntry<float> CoolSeconds;

		internal static ConfigEntry<float> OverheatPenalty;

		internal static ConfigEntry<float> RepeatPenalty;

		internal static ConfigEntry<float> BoxLockRelief;

		internal static ConfigEntry<float> SpinUp;

		internal static ConfigEntry<int> Cost;

		internal static ConfigEntry<float> RemoteShift;

		internal static ConfigEntry<bool> RemoteAuto;

		internal static ConfigEntry<float> DisplayShift;

		internal static ConfigEntry<float> DisplayScale;

		internal static ConfigEntry<float> HeldScale;

		internal static ConfigEntry<bool> RoofGunVisible;

		internal static ConfigEntry<bool> DumpInfo;

		internal static ConfigEntry<int> ConfigVersion;

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;
			Ctx = ctx;

			Damage = ctx.Config.Bind("Minigun", "Damage", 5f, "Damage of one bullet. For scale: the SMG does 7 a bullet; a car starts a race with about 100 health.");
			RoundsPerSecond = ctx.Config.Bind("Minigun", "RoundsPerSecond", 20f, "Bullets a second while the trigger is held and the barrels are up to speed.");
			Magazines = ctx.Config.Bind("Minigun", "Magazines", 3, "How many magazines the heat meter holds. Each is worth SecondsPerMagazine of fire; the Extra Magazines stat adds more.");
			SecondsPerMagazine = ctx.Config.Bind("Minigun", "SecondsPerMagazine", 1.3f, "Seconds of fire one magazine's worth of heat lasts. An ammo box takes this much heat off. With 3 magazines a full meter is 3.9 s, 78 bullets, 390 damage: three or four cars on their starting health, then you wait.");
			CoolSeconds = ctx.Config.Bind("Minigun", "CoolSeconds", 20f, "Seconds for a full meter to cool to zero once you stop firing. A short burst per half lap to a lap is what this is balanced for.");
			OverheatPenalty = ctx.Config.Bind("Minigun", "OverheatPenalty", 60f, "Seconds the remote is locked when the meter fills. You cannot raise it or fire until the lock has run out.");
			RepeatPenalty = ctx.Config.Bind("Minigun", "RepeatPenalty", 60f, "Seconds added to the lock for each earlier overheat in the same race (the second one locks for OverheatPenalty plus this).");
			BoxLockRelief = ctx.Config.Bind("Minigun", "BoxLockRelief", 15f, "Seconds an ammo box takes off a lock that is running (it also takes a magazine of heat off the meter).");
			SpinUp = ctx.Config.Bind("Minigun", "SpinUp", 0.4f, "Seconds the barrels take to spin up before the first bullet of a burst.");
			Cost = ctx.Config.Bind("Minigun", "Cost", 120, "Shop price.");
			RemoteAuto = ctx.Config.Bind("Minigun", "RemoteAuto", true, "Place the resting remote between the rear-view mirror and the leaderboard, measured in the cockpit. Off, or if either is not found, RemoteShift is used.");
			RemoteShift = ctx.Config.Bind("Minigun", "RemoteShift", 3.6f, "Fallback: how far to the right, in cockpit units, the resting remote is moved from where the SMG sits. 0 leaves it where the SMG is.");
			DisplayShift = ctx.Config.Bind("Minigun", "DisplayShift", 1.8f, "How far to the right, in cockpit units, the HEAT display's right edge is moved from the ammo rack's right edge, so the shifter does not cover it in fifth gear.");
			DisplayScale = ctx.Config.Bind("Minigun", "DisplayScale", 0.85f, "How big the HEAT display is, as a share of its first size (5 cockpit units wide).");
			HeldScale = ctx.Config.Bind("Minigun", "HeldScale", 1f, "How big the remote is in your hand while you aim, as a share of the SMG's size. 1 is the size that was checked in the game.");
			RoofGunVisible = ctx.Config.Bind("Minigun", "RoofGun", true, "Put the minigun on the roof of the car. Its bullets leave from there, so you see the tracers start above the car.");
			DumpInfo = ctx.Config.Bind("Debug", "DumpInfo", true, "Write the game's gun items and the gun's parts to the BepInEx log. Useful when tuning.");

			// BepInEx keeps a value already in the config file when a default changes, so after a retune the old numbers would linger.
			ConfigVersion = ctx.Config.Bind("Debug", "ConfigVersion", 0, "Used by the plugin to reset retuned defaults after an update. Leave it.");
			if (ConfigVersion.Value < 7)
			{
				CoolSeconds.Value = (float)CoolSeconds.DefaultValue;
				Damage.Value = (float)Damage.DefaultValue;
				SecondsPerMagazine.Value = (float)SecondsPerMagazine.DefaultValue;
				Magazines.Value = (int)Magazines.DefaultValue;
				RemoteShift.Value = (float)RemoteShift.DefaultValue;
				RemoteAuto.Value = (bool)RemoteAuto.DefaultValue;
				DisplayShift.Value = (float)DisplayShift.DefaultValue;
				DisplayScale.Value = (float)DisplayScale.DefaultValue;
				HeldScale.Value = (float)HeldScale.DefaultValue;
				ConfigVersion.Value = 7;
				ctx.Config.Save();
			}

			MinigunItem.Register(ctx);
			ctx.PatchAll();
		}

		public override void Unload()
		{
			foreach (MiniHeat heat in Object.FindObjectsByType<MiniHeat>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				heat.Cleanup();
				Object.Destroy(heat);
			}
			foreach (HeatBar bar in Object.FindObjectsByType<HeatBar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				Object.Destroy(bar.gameObject);
			}
			GarageTurret.Cleanup();
		}
	}
}
