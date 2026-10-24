using BepInEx.Configuration;
using BepInEx.Logging;
using HalfGripTools;

namespace HalfGripRpg
{
	public class Mod : HalfGripMod
	{
		internal static ManualLogSource Log;

		internal static ModContext Ctx;

		internal static ConfigEntry<float> ExplosionDamage;

		internal static ConfigEntry<float> DirectHitDamage;

		internal static ConfigEntry<float> CarLength;

		internal static ConfigEntry<float> LaunchSpeed;

		internal static ConfigEntry<float> MaxSpeed;

		internal static ConfigEntry<int> Rockets;

		internal static ConfigEntry<bool> SelfDamage;

		internal static ConfigEntry<bool> LowerAfterShot;

		internal static ConfigEntry<bool> DumpInfo;

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;
			Ctx = ctx;

			ExplosionDamage = ctx.Config.Bind("RPG", "ExplosionDamage", 120f, "Damage to a car at the centre of the blast. It falls to a quarter of this at the edge.");
			DirectHitDamage = ctx.Config.Bind("RPG", "DirectHitDamage", 80f, "Extra damage to the car the rocket actually strikes (the warhead's shaped charge), on top of the blast.");
			// Distances and speeds are in the game's own units: a car is about 13 long, and at 100 km/h it covers roughly 80 per second.
			// An RPG-7 rocket leaves the tube at about 120 m/s and tops out near 300 m/s, which is about 340 and 870 of these units a
			// second; these are a bit under that, so you can still see it fly.
			CarLength = ctx.Config.Bind("RPG", "CarLength", 13f, "How long a car is, in game units. The blast is measured in these: full damage out to 2 car lengths from the centre, half at 3, 15% at 4, and nothing past 5.");
			LaunchSpeed = ctx.Config.Bind("RPG", "RocketLaunchSpeed", 220f, "Speed the rocket leaves the tube at, in game units per second (a car at 100 km/h does about 80).");
			MaxSpeed = ctx.Config.Bind("RPG", "RocketMaxSpeed", 520f, "Speed the rocket reaches once its sustainer motor lights, in game units per second.");
			Rockets = ctx.Config.Bind("RPG", "Rockets", 3, "Spare rockets on the rack at the start.");
			SelfDamage = ctx.Config.Bind("RPG", "SelfDamage", false, "The blast also hurts your own car if it goes off close to you.");
			LowerAfterShot = ctx.Config.Bind("RPG", "LowerAfterShot", true, "Lower the launcher after a shot so you can reload straight away.");
			DumpInfo = ctx.Config.Bind("Debug", "DumpInfo", true, "Write extra detail about the RPG's parts to the BepInEx log. Useful when tuning.");

			RpgItem.Register(ctx);
			ctx.PatchAll();
		}
	}
}
