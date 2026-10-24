using BepInEx.Configuration;
using BepInEx.Logging;
using HalfGripTools;
using UnityEngine;

namespace HalfGripBombRace
{
	public class Mod : HalfGripMod
	{
		internal static ManualLogSource Log;

		internal static ModContext Ctx;

		internal static ConfigEntry<float> Chance;

		internal static ConfigEntry<float> SpeedShare;

		internal static ConfigEntry<float> FuseSeconds;

		internal static ConfigEntry<float> GraceSeconds;

		internal static ConfigEntry<bool> RivalBombs;

		internal static ConfigEntry<float> RivalSpeedShare;

		internal static ConfigEntry<float> RivalFuseSeconds;

		internal static ConfigEntry<bool> EliminateLast;

		internal static ConfigEntry<float> C4Width;

		internal static ConfigEntry<int> DriverSide;

		internal static ConfigEntry<float> NudgeX;

		internal static ConfigEntry<float> NudgeY;

		internal static ConfigEntry<float> DashWidth;

		internal static ConfigEntry<bool> DumpInfo;

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;
			Ctx = ctx;

			Chance = ctx.Config.Bind("Bomb Race", "Chance", 0.10f, "Chance (0 to 1) that a race in the middle of a run is a Bomb race. 1 makes every one of them a Bomb race.");
			SpeedShare = ctx.Config.Bind("Bomb Race", "SpeedShare", 0.45f, "The speed your bomb wants, as a share of your engine's top speed (the number on the Top Speed stat; the dial is full at 1). 0.45 is 80 on the dashboard with the stock engine, more with a faster one. The bomb's call-out at the start gives the number.");
			FuseSeconds = ctx.Config.Bind("Bomb Race", "FuseSeconds", 3f, "Seconds you can stay under your limit before your bomb goes off. Back over it, the fuse burns down again at the same rate.");
			GraceSeconds = ctx.Config.Bind("Bomb Race", "GraceSeconds", 5f, "Seconds after the start of the race before the bomb is armed.");
			RivalBombs = ctx.Config.Bind("Bomb Race", "RivalBombs", true, "The other cars carry a bomb too. They are not counted as your kills.");
			RivalSpeedShare = ctx.Config.Bind("Bomb Race", "RivalSpeedShare", 0.3f, "The same, for the other cars, as a share of each car's own top speed. Lower than yours because they slow for corners.");
			RivalFuseSeconds = ctx.Config.Bind("Bomb Race", "RivalFuseSeconds", 10f, "Seconds a rival can stay under its limit before its bomb goes off. More than yours, or most would blow up before the first lap is done.");
			EliminateLast = ctx.Config.Bind("Bomb Race", "EliminateLast", true, "Each time the leader starts a new lap (but the last one), the car in last place is blown up, you included.");
			C4Width = ctx.Config.Bind("Bomb Race", "C4Width", 0.4f, "Width of the C4 block on the driver-side door of a car, as a share of the car's width.");
			DriverSide = ctx.Config.Bind("Bomb Race", "DriverSide", 0, "Which side of the cars the C4 goes on: 0 picks the side the steering wheel is on, -1 is the left, 1 the right.");
			DashWidth = ctx.Config.Bind("Dash Bomb", "Width", 0.42f, "Width of the bomb block on the dash, as a share of the steering wheel's width.");
			NudgeX = ctx.Config.Bind("Dash Bomb", "NudgeX", 0f, "Moves the bomb block right (positive) or left, in steering wheel widths. It starts where the gear display would be if it were mirrored across the wheel.");
			NudgeY = ctx.Config.Bind("Dash Bomb", "NudgeY", 0f, "Moves the bomb block up (positive) or down, in steering wheel widths.");
			DumpInfo = ctx.Config.Bind("Debug", "DumpInfo", false, "Write extra detail (the map icons it copies, the fuse) to the BepInEx log.");

			ctx.PatchAll();

			ctx.OnSceneLoaded(scene => Attach());
			Attach();
		}

		// Mods switched off: take every bomb part out of the scene.
		public override void Unload()
		{
			foreach (DashBomb dash in Object.FindObjectsByType<DashBomb>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				Object.Destroy(dash.gameObject);
			}
			foreach (C4Visual c4 in Object.FindObjectsByType<C4Visual>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				Object.Destroy(c4.gameObject);
			}
			foreach (BombFuse fuse in Object.FindObjectsByType<BombFuse>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				Object.Destroy(fuse);
			}
		}

		// Same way the game's own RaceCutoff attaches itself: one component on the scene's TrackManager.
		private static void Attach()
		{
			TrackManager track = Object.FindFirstObjectByType<TrackManager>();
			if (track != null && track.GetComponent<BombFuse>() == null)
			{
				track.gameObject.AddComponent<BombFuse>().track = track;
			}
		}
	}
}
