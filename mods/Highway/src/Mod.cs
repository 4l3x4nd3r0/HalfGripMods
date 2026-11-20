using HalfGripTools.Meshes;
﻿using HalfGripTools;
using UnityEngine;

namespace HalfGripHighway
{
	/// <summary>
	/// A new race map, Highway: two four-lane highways, one on a bridge over the other, joined by two ramps into a lap of about 6.5 km.
	/// It has no scene of its own. It is built from code inside the Stadium scene when a Highway race loads, and added to the run map's pool.
	/// </summary>
	public class Mod : HalfGripMod
	{
		internal static ModLog Log;
		internal static ModContext Ctx;

		internal static ModSetting<bool> TestMode;
		internal static ModSetting<float> BenchmarkFactor;
		internal static ModSetting<int> MedianGaps;
		internal static ModSetting<float> MedianGapLength;
		internal static ModSetting<bool> ShowPath;
		internal static ModSetting<float> BreakSpeedKept;
		internal static ModSetting<float> GlanceAngle;
		internal static ModSetting<float> BendMinSpeed;
		internal static ModSetting<float> RailBounce;
		internal static ModSetting<float> SignSpeedLoss;
		internal static ModSetting<float> BreakSpeed;
		internal static ModSetting<float> BendSpeedLoss;
		internal static ModSetting<bool> LogRailHits;
		internal static ModSetting<float> DirtGrip;
		internal static ModSetting<float> DirtDrag;

		/// <summary>The map as registered with the loader (<c>ctx.Maps</c>).</summary>
		internal static MapInfo Info;

		/// <summary>The map's entry in the run config, once the game has one.</summary>
		internal static MapDefinition Map { get { return Info != null ? Info.Definition : null; } }

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;
			Ctx = ctx;

			TestMode = ctx.Config.Bind("Map", "TestMode", false, "Every race you start is on Highway, whatever the run map says. For trying the map out. Off, Highway is dealt like any other map.");
			BenchmarkFactor = ctx.Config.Bind("Map", "BenchmarkFactor", 0.9f, "Time objectives (\"finish under X s\") are Stadium's times scaled by lap length times this. Lower is harder. Tune it from your own times.");
			MedianGaps = ctx.Config.Bind("Layout", "MedianGaps", 4, "Gaps in the concrete median of each highway, an even number, spread along both halves (not under the bridge). 0 closes the median completely.");
			MedianGapLength = ctx.Config.Bind("Layout", "MedianGapLength", 120f, "Length of a median gap along the road, in metres. 120 m lets a car cross the 10 m median at about 8 degrees at 120 on the dash. Five breakable signs stand in each gap.");
			BreakSpeedKept = ctx.Config.Bind("Guardrail", "BreakSpeedKept", 0.6f, "Share of its speed a car keeps when it breaks through the guardrail (the game's own tree keeps 0.6, so a break costs about what felling a tree does), plus crash damage like the tree's.");
			GlanceAngle = ctx.Config.Bind("Guardrail", "GlanceAngle", 12f, "Under this angle (degrees) between the car's path and the guardrail it only scrapes along it, at any speed: no bend, no loss.");
			BendMinSpeed = ctx.Config.Bind("Guardrail", "BendMinSpeed", 15f, "The least speed into the guardrail (the part of the speed that points at it, as the dash shows it) that bends it. Speed into the rail is the speed times the sine of the angle.");
			RailBounce = ctx.Config.Bind("Guardrail", "Bounce", 0.25f, "How much of the speed into a bending rail comes back out (0 to 1): a soft push back instead of a dead wall.");
			SignSpeedLoss = ctx.Config.Bind("Guardrail", "SignSpeedLoss", 0.03f, "Share of a car's speed that each sign or drum it hits takes (cones take none). 0.03 is a light bump; a broken guardrail (BreakSpeedKept) is the heavy one.");
			BreakSpeed = ctx.Config.Bind("Guardrail", "BreakSpeed", 120f, "Speed into the guardrail (as the dash shows it) from which it breaks, with crash damage. Below it the rail holds and bends.");
			BendSpeedLoss = ctx.Config.Bind("Guardrail", "BendSpeedLoss", 5f, "Speed a bending hit costs, in dash units.");
			LogRailHits = ctx.Config.Bind("Debug", "LogRailHits", false, "Write a line to the log for every guardrail hit: the speed into the rail, what it did and the speed before and after.");
			DirtGrip = ctx.Config.Bind("Dirt", "Grip", 0.75f, "Grip of the two dirt shortcuts, 1 being tarmac. Tyres that cope with loose surfaces lose less.");
			DirtDrag = ctx.Config.Bind("Dirt", "Drag", 1.8f, "Rolling resistance of the dirt shortcuts, 1 being tarmac.");
			ShowPath = ctx.Config.Bind("Debug", "ShowPath", false, "Draw the sampled track line the AI and the lap counter follow.");

			// The loader makes the map's entry in the run config, gives it its track id (20, as always) and sends the game to the Stadium scene for it.
			Info = new MapInfo
			{
				Id = "highway",
				DisplayName = "Highway",
				PreferredTrackId = 20,
				LapLength = Route.Build().Length,
				BenchmarkFactor = BenchmarkFactor.Value,
				Adjust = map =>
				{
					Sprite icon = ctx.Sprites.LoadSprite("highway_icon.png", map.icon);
					if (icon != null)
					{
						map.icon = icon;
					}
				}
			};
			ctx.Maps.Register(Info);
			TreeSource.Capture(Mod.Log);
			ctx.OnSceneLoaded(scene =>
			{
				TreeSource.Capture(Mod.Log);
				HighwayScene.OnSceneLoaded(scene);
			});
			ctx.PatchAll();
		}

		public override void Unload()
		{
			HighwayScene.Unload();
		}
	}
}
