using BepInEx.Configuration;
using BepInEx.Logging;
using HalfGripTools;

namespace HalfGripHighway
{
	/// <summary>
	/// A new race map, Highway: two four-lane highways, one on a bridge over the other, joined by two ramps into a lap of about 6.5 km.
	/// It has no scene of its own. It is built from code inside the Stadium scene when a Highway race loads, and added to the run map's pool.
	/// </summary>
	public class Mod : HalfGripMod
	{
		internal static ManualLogSource Log;
		internal static ModContext Ctx;

		internal static ConfigEntry<bool> TestMode;
		internal static ConfigEntry<float> BenchmarkFactor;
		internal static ConfigEntry<int> MedianGaps;
		internal static ConfigEntry<float> MedianGapLength;
		internal static ConfigEntry<bool> ShowPath;
		internal static ConfigEntry<float> SpeedLossPerPiece;
		internal static ConfigEntry<float> SignSpeedLoss;
		internal static ConfigEntry<float> DirtGrip;
		internal static ConfigEntry<float> DirtDrag;

		/// <summary>The map entry the mod adds to the run config, once it exists.</summary>
		internal static MapDefinition Map;

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
			DirtGrip = ctx.Config.Bind("Dirt", "Grip", 0.75f, "Grip of the two dirt shortcuts, 1 being tarmac. Tyres that cope with loose surfaces lose less.");
			DirtDrag = ctx.Config.Bind("Dirt", "Drag", 1.8f, "Rolling resistance of the dirt shortcuts, 1 being tarmac.");
			ShowPath = ctx.Config.Bind("Debug", "ShowPath", false, "Draw the sampled track line the AI and the lap counter follow.");

			MapEntry.Ensure();
			TreeSource.Capture();
			ctx.OnSceneLoaded(scene =>
			{
				MapEntry.Ensure();
				TreeSource.Capture();
				HighwayScene.OnSceneLoaded(scene);
			});
			ctx.OnUnload(MapEntry.Remove);
			ctx.PatchAll();
		}

		public override void Unload()
		{
			HighwayScene.Unload();
		}
	}
}
