using BepInEx.Configuration;
using BepInEx.Logging;
using HalfGripTools;

namespace TempleOfDrift
{
	/// <summary>
	/// Temple of Drift: a mountain pass with two halves. A touge of hairpins from the summit down to a valley, a Buddhist temple and a Shinto
	/// torii, a dirt road, and a cave that forks in two and climbs back to the top. It has no scene of its own: it is built from code inside
	/// the Stadium scene when one of its races loads (as Highway does), and added to the run map's pool.
	/// </summary>
	public class Mod : HalfGripMod
	{
		internal static ManualLogSource Log;
		internal static ModContext Ctx;

		internal static ConfigEntry<bool> TestMode;
		internal static ConfigEntry<float> BenchmarkFactor;
		internal static ConfigEntry<float> DirtGrip;
		internal static ConfigEntry<float> DirtDrag;
		internal static ConfigEntry<float> PaintGrip;
		internal static ConfigEntry<float> HairpinGrip;
		internal static ConfigEntry<float> TougeGrip;
		internal static ConfigEntry<int> ForkLanterns;
		internal static ConfigEntry<bool> GuideWalls;
		internal static ConfigEntry<float> TimberEntranceBreakSpeed;
		internal static ConfigEntry<float> TimberExitBreakSpeed;
		internal static ConfigEntry<float> TimberSpeedKept;
		internal static ConfigEntry<float> BambooFellSpeed;
		internal static ConfigEntry<float> BambooSpeedKept;
		internal static ConfigEntry<float> GravelGrip;
		internal static ConfigEntry<float> GravelDrag;
		internal static ConfigEntry<float> ShortWayGrip;
		internal static ConfigEntry<float> ShortWayDrag;
		internal static ConfigEntry<float> SpeedLossPerPiece;
		internal static ConfigEntry<int> CaveLights;
		internal static ConfigEntry<int> TreeCount;
		internal static ConfigEntry<bool> PlayMusic;
		internal static ConfigEntry<bool> ShowPath;

		/// <summary>The map entry the mod adds to the run config, once it exists.</summary>
		internal static MapDefinition Map;

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;
			Ctx = ctx;

			TestMode = ctx.Config.Bind("Map", "TestMode", false, "Every race you start is on Temple of Drift, whatever the run map says. For trying the map out. Off, it is dealt like any other map.");
			BenchmarkFactor = ctx.Config.Bind("Map", "BenchmarkFactor", 1.0f, "Time objectives (\"finish under X s\") are Stadium's times scaled by lap length times this. Lower is harder. Tune it from your own times.");
			DirtGrip = ctx.Config.Bind("Dirt", "Grip", 0.78f, "Grip of the dirt road, 1 being tarmac. Tyres that cope with loose surfaces lose less.");
			DirtDrag = ctx.Config.Bind("Dirt", "Drag", 1.5f, "Rolling resistance of the dirt road, 1 being tarmac.");
			TougeGrip = ctx.Config.Bind("Touge", "TarmacGrip", 1.0f, "Grip of the mountain tarmac outside the hairpins, 1 being normal tarmac. It scales the tyres' own grip, so better tyres still grip more. Tyres marked Off-Road or All-Conditions ignore the surface and are not affected. Lower is slipperier.");
			HairpinGrip = ctx.Config.Bind("Hairpins", "TarmacGrip", 0.85f, "Grip on the tarmac of the hairpins (instead of the touge value). Together with the banking it lets the tail come out on a lift or a brake tap. Lower is slipperier.");
			PaintGrip = ctx.Config.Bind("Paint", "PaintGrip", 0.7f, "Grip on the red and white paint of the hairpins. Braking on it lets the car drift. Lower is slipperier.");
			BambooFellSpeed = ctx.Config.Bind("Bamboo", "FellSpeed", 60f, "Speed (speedometer units) from which a car breaks a bamboo clump and drives through it. Below it the clump is solid: the car glances off and the clump sways.");
			BambooSpeedKept = ctx.Config.Bind("Bamboo", "SpeedKept", 0.94f, "Share of its speed a car keeps after snapping one bamboo clump (cedars and cherries use the game's own value). Bamboo stands in groves, so a pass through several costs this many times over. Bamboo is a trigger: it never stops the car or damages it.");
			GravelGrip = ctx.Config.Bind("Gravel", "Grip", 0.92f, "Grip of the temple's gravel path, 1 being tarmac.");
			GravelDrag = ctx.Config.Bind("Gravel", "Drag", 1.2f, "Rolling resistance of the temple's gravel path, 1 being tarmac.");
			ShortWayGrip = ctx.Config.Bind("Cave", "ShortWayGrip", 0.80f, "Grip on the short, steep way through the cave (broken rock). Lower makes the shortcut cost more.");
			ShortWayDrag = ctx.Config.Bind("Cave", "ShortWayDrag", 1.9f, "Rolling resistance on the short way. Higher makes the shortcut cost more.");
			SpeedLossPerPiece = ctx.Config.Bind("Guardrail", "SpeedLossPerPiece", 0.30f, "Share of a car's speed that each piece of guardrail it touches takes (0 to 1), as on Highway.");
			CaveLights = ctx.Config.Bind("Cave", "Lights", 14, "Faint lights along the cave (glowing fungus and a shaft of daylight). Lower is cheaper. 0 leaves the cave pitch dark.");
			TreeCount = ctx.Config.Bind("Scenery", "Trees", 800, "Japanese trees round the roads (cedar, cherry, maple, pine, bamboo). Lower is cheaper.");
			PlayMusic = ctx.Config.Bind("Music", "Enabled", true, "Play the map's own track instead of one of the game's. Needs audio\\temple_of_drift.wav in the mod folder.");
			ShowPath = ctx.Config.Bind("Debug", "ShowPath", false, "Draw the sampled track line the AI and the lap counter follow.");

			GuideWalls = ctx.Config.Bind("Cave", "GuideWalls", true, "Smooth invisible walls along both sides of the cave roads, so a car slides along them instead of catching on rock.");
			TimberEntranceBreakSpeed = ctx.Config.Bind("Timber", "EntranceBreakSpeed", 60f, "Dashboard speed from which a car breaks the plank barricade at the entrance of the short way and goes through. Slower, the barricade holds it just short of the boards (no damage).");
			TimberExitBreakSpeed = ctx.Config.Bind("Timber", "ExitBreakSpeed", 60f, "The same for the barricade at the exit of the short way.");
			TimberSpeedKept = ctx.Config.Bind("Timber", "SpeedKept", 0.9f, "Share of its speed a car keeps after breaking a barricade.");
			ForkLanterns = ctx.Config.Bind("Cave", "ForkLanterns", 2, "Lit stone lanterns along the long way from the fork (0 for none).");
			// BepInEx keeps a value saved in the cfg over a new default, so a preset version resets the three grip values once when the defaults change
			ConfigEntry<int> preset = ctx.Config.Bind("Info", "GripPresetVersion", 0, "Internal: which default grip values this file was last reset to. Edit the grip values, not this.");
			if (preset.Value < 2)
			{
				TougeGrip.Value = (float)TougeGrip.DefaultValue;
				HairpinGrip.Value = (float)HairpinGrip.DefaultValue;
				PaintGrip.Value = (float)PaintGrip.DefaultValue;
				BambooSpeedKept.Value = (float)BambooSpeedKept.DefaultValue;
				preset.Value = 2;
				ctx.Config.Save();
				Log.LogInfo("Grip values reset to the new defaults (touge " + TougeGrip.Value + ", hairpin " + HairpinGrip.Value + ", paint " + PaintGrip.Value + ")");
			}
			if (preset.Value < 3)
			{
				// version 3: bamboo is solid up to 60 and breaks above (it was 25); the grip values are left as the player has them
				BambooFellSpeed.Value = (float)BambooFellSpeed.DefaultValue;
				preset.Value = 3;
				ctx.Config.Save();
				Log.LogInfo("Bamboo fall speed reset to " + BambooFellSpeed.Value);
			}

			if (!Data.Load(ctx.Folder))
			{
				Log.LogError("Temple of Drift: its data files are missing or unreadable, so the map is not added.");
				return;
			}
			MapEntry.Ensure();
			TreeSource.Capture();
			ctx.OnSceneLoaded(scene =>
			{
				MapEntry.Ensure();
				TreeSource.Capture();
				SceneBuilder.OnSceneLoaded(scene);
			});
			ctx.OnUnload(MapEntry.Remove);
			ctx.PatchAll();
		}

		public override void Unload()
		{
			SceneBuilder.Unload();
		}
	}
}
