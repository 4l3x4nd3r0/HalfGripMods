using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;

namespace Multiplayer
{
	/// <summary>
	/// While a player is in a multiplayer room nothing they do may reach their single-player save: money, items,
	/// stats, records and Steam achievements stay as they were. On entering, a copy of the save's in-memory tables is
	/// kept and every write to disk is skipped; on leaving, the tables go back to the copy.
	/// Settings (graphics, controls) are not part of the copy and are left alone.
	/// </summary>
	internal static class SaveSandbox
	{
		private static Dictionary<string, int> ints;
		private static Dictionary<string, float> floats;
		private static Dictionary<string, string> strings;
		private static bool dirtySave;
		private static bool dirtyHistory;

		private static readonly Func<string, bool> IsSetting = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), AccessTools.Method(typeof(SaveData), "IsSetting"));

		internal static bool Active { get; private set; }

		/// <summary>Test copies of the game: no write may reach the real save, from the first frame on.</summary>
		internal static bool ReadOnly { get; set; }

		/// <summary>True when the patches below must block writes.</summary>
		/// <summary>
		/// Wider than <see cref="Active"/> on purpose: while a room is being made, in the garage, or leaving, a bug in the sandbox's own
		/// bookkeeping must still not let a write through to the player's save.
		/// </summary>
		internal static bool Blocking => Active || ReadOnly || Room.Engaged || Garage.Solo || Room.restorePending || Room.Leaving;

		/// <summary>Settings stay as they are, except the speedrun records and mode, which the room must not be able to change.</summary>
		private static bool Restored(string key)
		{
			return !IsSetting(key) || key.StartsWith("speedrun.", StringComparison.Ordinal);
		}

		internal static void Enter()
		{
			if (Active)
			{
				return;
			}
			LifetimeStats.Flush(); // totals waiting to be written belong to the real save, so they go in before the copy is taken
			SaveData.GetInt("multiplayer.sandbox"); // makes the game read its files now, so the copy is of the real save
			KeepFileCopy();
			ints = new Dictionary<string, int>(Table<int>("ints"));
			floats = new Dictionary<string, float>(Table<float>("floats"));
			strings = new Dictionary<string, string>(Table<string>("strings"));
			dirtySave = Flag("dirtySave");
			dirtyHistory = Flag("dirtyHistory");
			Active = true;
			Mod.Log.LogInfo("Save sandbox on: nothing reaches the save or Steam achievements until you leave multiplayer.");
		}

		/// <summary>
		/// A copy of save.json next to it, once per game session, so a player whose save goes missing for any reason can get it back.
		/// An empty or unreadable save is never copied over a good copy.
		/// </summary>
		private static void KeepFileCopy()
		{
			try
			{
				if (ReadOnly)
				{
					return; // a test copy of the game: nothing may be written to the player's save folder, not even the backup
				}
				if (SaveData.SaveUnreadable || Table<int>("ints").Count == 0)
				{
					Mod.Log.LogWarning("Save sandbox: the save was already empty or unreadable when the room started; no copy made.");
					return;
				}
				// One copy, refreshed on every entry into multiplayer (the sandbox is only ever entered with the real save in memory and on disk).
				if (!File.Exists(SaveData.FilePath) || new FileInfo(SaveData.FilePath).Length < 100)
				{
					return;
				}
				string target = SaveData.FilePath + ".multiplayer-backup";
				File.Copy(SaveData.FilePath, target, true);
				Mod.Log.LogInfo("Save sandbox: copy of the save kept as " + target);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Save sandbox: could not copy the save file: " + e.Message);
			}
		}

		internal static void Leave()
		{
			if (!Active)
			{
				return;
			}
			Active = false;
			Restore(Table<int>("ints"), ints);
			Restore(Table<float>("floats"), floats);
			Restore(Table<string>("strings"), strings);
			// What was waiting to be written when we entered still is; what the room changed is gone.
			SetFlag("dirtySave", dirtySave);
			SetFlag("dirtyHistory", dirtyHistory);
			ints = null;
			floats = null;
			strings = null;
			Mod.Log.LogInfo("Save sandbox off: the save is back as it was.");
		}

		private static void Restore<T>(Dictionary<string, T> live, Dictionary<string, T> copy)
		{
			foreach (string key in live.Keys.Where(Restored).ToList())
			{
				live.Remove(key);
			}
			foreach (KeyValuePair<string, T> pair in copy)
			{
				if (Restored(pair.Key))
				{
					live[pair.Key] = pair.Value;
				}
			}
		}

		private static Dictionary<string, T> Table<T>(string field)
		{
			return (Dictionary<string, T>)AccessTools.Field(typeof(SaveData), field).GetValue(null);
		}

		private static bool Flag(string field)
		{
			return (bool)AccessTools.Field(typeof(SaveData), field).GetValue(null);
		}

		private static void SetFlag(string field, bool value)
		{
			AccessTools.Field(typeof(SaveData), field).SetValue(null, value);
		}
	}

	// The game writes its files from Save() and its speedrun backup from Backup(); neither may run in a room.
	[HarmonyPatch(typeof(SaveData), nameof(SaveData.Save))]
	internal static class SkipSave
	{
		private static bool Prefix() => !SaveSandbox.Blocking;
	}

	[HarmonyPatch(typeof(SaveData), nameof(SaveData.Backup))]
	internal static class SkipBackup
	{
		private static bool Prefix() => !SaveSandbox.Blocking;
	}

	// A stand-in run starts through Speedrun.BeginRun -> RestoreBackup, which deletes save.speedrun.json from disk; that file belongs to the real save.
	[HarmonyPatch(typeof(SaveData), nameof(SaveData.RestoreBackup))]
	internal static class SkipRestoreBackup
	{
		private static bool Prefix(ref bool __result)
		{
			if (!SaveSandbox.Blocking)
			{
				return true;
			}
			__result = false;
			return false;
		}
	}

	// Run statistics: the game stores a finished or abandoned run in its history and, when the player shares stats, writes an outbox file and uploads it.
	[HarmonyPatch(typeof(RunStats), nameof(RunStats.RaceFinished))]
	internal static class SkipRaceStats
	{
		private static bool Prefix() => !SaveSandbox.Blocking;
	}

	[HarmonyPatch(typeof(RunStats), nameof(RunStats.RunAbandoned))]
	internal static class SkipRunAbandoned
	{
		private static bool Prefix() => !SaveSandbox.Blocking;
	}

	[HarmonyPatch(typeof(RunStats), "Send")]
	internal static class SkipStatsSend
	{
		private static bool Prefix() => !SaveSandbox.Blocking;
	}

	// save.speedrun.json belongs to the real save.
	[HarmonyPatch(typeof(SaveData), nameof(SaveData.DropBackup))]
	internal static class SkipDropBackup
	{
		private static bool Prefix() => !SaveSandbox.Blocking;
	}

	// Reset Progress wipes PlayerPrefs and deletes files, which the restored tables cannot undo.
	[HarmonyPatch(typeof(ResetProgressButton), nameof(ResetProgressButton.Delete))]
	internal static class SkipResetProgress
	{
		private static bool Prefix() => !SaveSandbox.Blocking;
	}

	[HarmonyPatch(typeof(SteamAchievements), nameof(SteamAchievements.ResetAll))]
	internal static class SkipResetAchievements
	{
		private static bool Prefix(ref bool __result)
		{
			if (!SaveSandbox.Blocking)
			{
				return true;
			}
			__result = false;
			return false;
		}
	}

	[HarmonyPatch(typeof(SteamAchievements), nameof(SteamAchievements.Unlock))]
	internal static class SkipUnlock
	{
		private static bool Prefix() => !SaveSandbox.Blocking;
	}

	[HarmonyPatch(typeof(SteamAchievements), nameof(SteamAchievements.Progress))]
	internal static class SkipProgress
	{
		private static bool Prefix() => !SaveSandbox.Blocking;
	}

	[HarmonyPatch(typeof(SteamAchievements), nameof(SteamAchievements.Sync))]
	internal static class SkipSync
	{
		private static bool Prefix() => !SaveSandbox.Blocking;
	}
}
