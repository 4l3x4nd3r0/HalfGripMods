using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Multiplayer
{
	/// <summary>
	/// Keeps the scene in step with the room's phase: lobby is the main menu, garage is the garage scene, the race is a track scene
	/// (loaded through the garage's own start button). Declarative on purpose: every client just compares "what phase is the room in"
	/// with "what scene am I in".
	/// </summary>
	internal static class Flow
	{
		internal const string MenuScene = "SCENE_UI_MAIN_MENU";
		internal const string GarageScene = "SCENE_UI_UpgradeMenu";

		private static float lastLoad = -10f;

		internal static string ActiveScene { get { return SceneManager.GetActiveScene().name; } }
		internal static bool InMenu { get { return ActiveScene == MenuScene; } }
		internal static bool InGarage { get { return ActiveScene == GarageScene; } }
		internal static bool InRaceScene { get { return ActiveScene.StartsWith("SCENE_TRACK_", StringComparison.Ordinal) || UnityEngine.Object.FindFirstObjectByType<TrackManager>() != null && !InMenu && !InGarage; } }

		internal static bool Busy
		{
			get { return SceneLoader.Instance == null || SceneLoader.Transitioning || SceneLoader.Loading || Time.realtimeSinceStartup - lastLoad < 1.5f; }
		}

		internal static void Sync()
		{
			if (Busy)
			{
				return;
			}
			switch (Room.Phase)
			{
				case Phase.Lobby:
					// the room lives in the main menu: coming back from a race (or a garage) means going there
					if (!InMenu && !Garage.Solo)
					{
						GoToMenu();
					}
					break;
				case Phase.Results:
					// the results screen is part of the MULTIPLAYER screen; leave the track a moment after the race ended
					if (!InMenu && Time.realtimeSinceStartup - Room.resultsAt > 3f)
					{
						GoToMenu();
					}
					break;
				case Phase.Loading:
					// the race is started through the garage's own start button, so the garage is opened (unseen) first
					if (!Garage.Launched)
					{
						if (InGarage)
						{
							Garage.RetryLaunch();
						}
						else if (!InRaceScene)
						{
							Garage.Load();
							lastLoad = Time.realtimeSinceStartup;
						}
					}
					break;
			}
		}

		internal static void MarkLoading()
		{
			lastLoad = Time.realtimeSinceStartup;
		}

		internal static void GoToMenu()
		{
			if (InMenu || SceneLoader.Instance == null)
			{
				return;
			}
			TrackManager.endRaceInstead = null;
			TrackManager.ignoreFinish = false;
			TimeScale.ResetAll();
			SceneLoader.Instance.LoadUIScene(0, 1f);
			lastLoad = Time.realtimeSinceStartup;
		}

		internal static void OpenMultiplayerScreen()
		{
			MainMenu menu = UnityEngine.Object.FindFirstObjectByType<MainMenu>();
			if (menu != null)
			{
				menu.ShowMultiplayer();
			}
		}
	}

	/// <summary>Puts the game's in-memory state back after a room: the save tables are restored by the sandbox, but a few statics cache what they held.</summary>
	internal static class Restore
	{
		internal static void AfterRoom()
		{
			try
			{
				TrackManager.endRaceInstead = null;
				TrackManager.ignoreFinish = false;
				TimeScale.ResetAll();
				Garage.StandInStarted = false;
				// The same caches the game itself drops when it swaps the save under its own feet (speedrun restore, progress reset):
				// lifetime stats, unlocks, money, achievements, tutorial state and so on re-read the restored save.
				ResetProgressButton.ForgetCaches();
				SeedStats();
				// RaceStats keeps the room's run totals (and its unsaved seconds) in memory and writes them out lazily: into the real save.
				RaceStats.ForgetCache();
				Traverse.Create(typeof(RaceStats)).Field("unsavedSeconds").SetValue(0f);
				RunState.ClearRaceContext();
				PlayerLoadout.Forget();
				// The next menu rebuilds the real run from the restored save (its Continue button reads RunState.Load()).
				Traverse.Create(typeof(RunState)).Property("Map").SetValue(null);
				GameManager.inventoryUpgrades.Clear();
				GameManager.equippedUpgrades.Clear();
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Restoring the game's state after the room: " + e.Message);
			}
		}

		/// <summary>Stats keeps the permanent boosts it loaded; make it read them again from the restored save.</summary>
		private static void SeedStats()
		{
			Traverse stats = Traverse.Create(typeof(Stats));
			stats.Field("permanentLoaded").SetValue(false);
			object table = stats.Field("permanent").GetValue();
			if (table != null)
			{
				AccessTools.Method(table.GetType(), "Clear").Invoke(table, null);
			}
		}
	}
}
