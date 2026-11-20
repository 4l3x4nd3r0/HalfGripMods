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
		internal static ModSetting<float> SpeedLossPerPiece;
		internal static ModSetting<float> SignSpeedLoss;
		internal static ModSetting<float> ContainSpeed;
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
			SpeedLossPerPiece = ctx.Config.Bind("Guardrail", "SpeedLossPerPiece", 0.30f, "Share of a car's speed that each piece of guardrail (or sign) it actually touches takes (0 to 1). 0.30 leaves 34% of the speed after three pieces and about 12% after six.");
			SignSpeedLoss = ctx.Config.Bind("Guardrail", "SignSpeedLoss", 0.03f, "Share of a car's speed that each sign or drum it hits takes (cones take none). 0.03 is a light bump; the guardrail's SpeedLossPerPiece is the heavy one.");
			ContainSpeed = ctx.Config.Bind("Guardrail", "ContainSpeed", 90f, "Speed into the guardrail, as the dash shows it (the part of the car's speed that points at the rail), up to which the rail holds like a wall: the car slides along it, loses nothing and takes no crash damage.");
			BreakSpeed = ctx.Config.Bind("Guardrail", "BreakSpeed", 120f, "Speed into the guardrail, as the dash shows it, from which the rail breaks. Between ContainSpeed and this the rail holds but bends and the car loses some speed. A break takes the touched pieces out and bends the two on each side.");
			BendSpeedLoss = ctx.Config.Bind("Guardrail", "BendSpeedLoss", 0.20f, "Share of the car's speed lost in a hit that only bends the rail, at the top of the bending band (0 to 1). It grows from nothing at ContainSpeed.");
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
