using HalfGripTools.Meshes;
using HalfGripTools;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>
	/// Temple of Drift: a mountain pass with two halves. A touge of hairpins from the summit down to a valley, a Buddhist temple and a Shinto
	/// torii, a dirt road, and a cave that forks in two and climbs back to the top. It has no scene of its own: it is built from code inside
	/// the Stadium scene when one of its races loads (as Highway does), and added to the run map's pool.
	/// </summary>
	public class Mod : HalfGripMod
	{
		internal static ModLog Log;
		internal static ModContext Ctx;

		internal static ModSetting<bool> TestMode;
		internal static ModSetting<float> BenchmarkFactor;
		internal static ModSetting<float> DirtGrip;
		internal static ModSetting<float> DirtDrag;
		internal static ModSetting<float> PaintGrip;
		internal static ModSetting<float> HairpinGrip;
		internal static ModSetting<float> TougeGrip;
		internal static ModSetting<int> ForkLanterns;
		internal static ModSetting<bool> GuideWalls;
		internal static ModSetting<float> TimberEntranceBreakSpeed;
		internal static ModSetting<float> TimberExitBreakSpeed;
		internal static ModSetting<float> TimberSpeedKept;
		internal static ModSetting<float> BambooFellSpeed;
		internal static ModSetting<float> BambooSpeedKept;
		internal static ModSetting<float> GravelGrip;
		internal static ModSetting<float> GravelDrag;
		internal static ModSetting<float> ShortWayGrip;
		internal static ModSetting<float> ShortWayDrag;
		internal static ModSetting<float> BreakSpeedKept;
		internal static ModSetting<float> GlanceAngle;
		internal static ModSetting<float> BendMinSpeed;
		internal static ModSetting<float> RailBounce;
		internal static ModSetting<float> BreakAngle;
		internal static ModSetting<float> BreakAngleSpeed;
		internal static ModSetting<float> BreakSpeed;
		internal static ModSetting<float> BendSpeedLoss;
		internal static ModSetting<bool> LogRailHits;
		internal static ModSetting<int> CaveLights;
		internal static ModSetting<int> TreeCount;
		internal static ModSetting<bool> PlayMusic;
		internal static ModSetting<bool> ShowPath;

		/// <summary>The map entry the mod adds to the run config, once it exists.</summary>
		internal static MapInfo Info;

		/// <summary>The map's entry in the run config, once the game has one.</summary>
		internal static MapDefinition Map { get { return Info != null ? Info.Definition : null; } }

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
			BreakSpeedKept = ctx.Config.Bind("Guardrail", "BreakSpeedKept", 0.6f, "Share of its speed a car keeps when it breaks through the guardrail (the game's own tree keeps 0.6, so a break costs about what felling a tree does), plus crash damage like the tree's.");
			GlanceAngle = ctx.Config.Bind("Guardrail", "GlanceAngle", 12f, "Under this angle (degrees) between the car's path and the guardrail it only scrapes along it, at any speed: no bend, no loss.");
			BendMinSpeed = ctx.Config.Bind("Guardrail", "BendMinSpeed", 15f, "The least speed into the guardrail (the part of the speed that points at it, as the dash shows it) that bends it. Speed into the rail is the speed times the sine of the angle.");
			BreakAngle = ctx.Config.Bind("Guardrail", "BreakAngle", 45f, "A hit at least this steep (degrees) breaks the guardrail when the car is also going BreakAngleSpeed or more.");
			BreakAngleSpeed = ctx.Config.Bind("Guardrail", "BreakAngleSpeed", 120f, "The dash speed that, at BreakAngle or steeper, is enough to break the guardrail.");
			RailBounce = ctx.Config.Bind("Guardrail", "Bounce", 0.05f, "How much of the speed into a bending rail comes back out (0 to 1): a soft push back instead of a dead wall.");
			BreakSpeed = ctx.Config.Bind("Guardrail", "BreakSpeed", 120f, "Speed into the guardrail (as the dash shows it) from which it breaks, with crash damage. Below it the rail holds and bends.");
			BendSpeedLoss = ctx.Config.Bind("Guardrail", "BendSpeedLoss", 5f, "Speed a bending hit costs, in dash units.");
			// A config file keeps an old value when a default changes: bring the retuned rail numbers up to date once.
			ModSetting<int> railVersion = ctx.Config.Bind("Guardrail", "ConfigVersion", 0, "Internal: which rail defaults this file has been brought up to.");
			if (railVersion.Value < 3)
			{
				RailBounce.Value = (float)RailBounce.DefaultValue;
				BreakAngle.Value = (float)BreakAngle.DefaultValue;
				BreakAngleSpeed.Value = (float)BreakAngleSpeed.DefaultValue;
				railVersion.Value = 3;
				ctx.Config.Save();
			}
			LogRailHits = ctx.Config.Bind("Debug", "LogRailHits", false, "Write a line to the log for every guardrail hit: the speed into the rail, what it did and the speed before and after.");
			CaveLights = ctx.Config.Bind("Cave", "Lights", 14, "Faint lights along the cave (glowing fungus and a shaft of daylight). Lower is cheaper. 0 leaves the cave pitch dark.");
			TreeCount = ctx.Config.Bind("Scenery", "Trees", 800, "Japanese trees round the roads (cedar, cherry, maple, pine, bamboo). Lower is cheaper.");
			PlayMusic = ctx.Config.Bind("Music", "Enabled", true, "Play the map's own track instead of one of the game's. Needs audio\\temple_of_drift.wav in the mod folder.");
			ShowPath = ctx.Config.Bind("Debug", "ShowPath", false, "Draw the sampled track line the AI and the lap counter follow.");

			GuideWalls = ctx.Config.Bind("Cave", "GuideWalls", true, "Smooth invisible walls along both sides of the cave roads, so a car slides along them instead of catching on rock.");
			TimberEntranceBreakSpeed = ctx.Config.Bind("Timber", "EntranceBreakSpeed", 60f, "Dashboard speed from which a car breaks the plank barricade at the entrance of the short way and goes through. Slower, the barricade holds it just short of the boards (no damage).");
			TimberExitBreakSpeed = ctx.Config.Bind("Timber", "ExitBreakSpeed", 60f, "The same for the barricade at the exit of the short way.");
			TimberSpeedKept = ctx.Config.Bind("Timber", "SpeedKept", 0.9f, "Share of its speed a car keeps after breaking a barricade.");
			ForkLanterns = ctx.Config.Bind("Cave", "ForkLanterns", 2, "Lit stone lanterns along the long way from the fork (0 for none).");
			// The cfg file keeps a saved value over a new default, so a preset version resets the three grip values once when the defaults change
			ModSetting<int> preset = ctx.Config.Bind("Info", "GripPresetVersion", 0, "Internal: which default grip values this file was last reset to. Edit the grip values, not this.");
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
			// The loader makes the map's entry in the run config, gives it its track id (21, as always) and sends the game to the Stadium scene for it.
			Info = new MapInfo
			{
				Id = "templeofdrift",
				DisplayName = "Temple of Drift",
				SaveName = "TempleOfDrift",   // the name a saved run stores: never change it
				PreferredTrackId = 21,
				LapLength = Data.Lap,
				BenchmarkFactor = BenchmarkFactor.Value,
				Adjust = map =>
				{
					map.excludeFromLast = false;
					map.notBesideSameTrack = false;
					map.altMusicTrack = null;
					map.altMusicChance = 0f;

					// Daylight forest weather with the rain switched off and a light mist; the Stadium's own is ash.
					RunConfig config = RunConfig.Instance;
					MapDefinition forest = config != null ? (config.FindMap("ForestRainyDay") ?? config.FindMap("ForestRed")) : null;
					if (forest != null && forest.weather != null)
					{
						WeatherProfile weather = Object.Instantiate(forest.weather);
						weather.hideFlags = HideFlags.HideAndDontSave;
						weather.rain = false;
						weather.snow = false;
						weather.ash = false;
						weather.headlightsActive = false;
						weather.fogActive = true;
						weather.fogColor = new Color(0.70f, 0.78f, 0.78f);
						weather.fogStart = 120f;
						weather.fogEnd = 2600f;
						weather.gripExponent = 1f;
						weather.gripMultiplier = 1f;
						map.weather = weather;
					}
					Sprite icon = ctx.Sprites.LoadSprite("templeofdrift_icon.png", map.icon);
					if (icon != null)
					{
						map.icon = icon;
					}
					if (PlayMusic.Value)
					{
						AudioClip clip = ctx.Audio.LoadWav("audio/temple_of_drift.wav");
						if (clip != null)
						{
							map.musicTrack = clip;
						}
					}
				}
			};
			ctx.Maps.Register(Info);
			TreeSource.Capture(Mod.Log, 1);
			ctx.OnSceneLoaded(scene =>
			{
				TreeSource.Capture(Mod.Log, 1);
				SceneBuilder.OnSceneLoaded(scene);
			});
			ctx.PatchAll();
		}

		public override void Unload()
		{
			SceneBuilder.Unload();
		}
	}
}
