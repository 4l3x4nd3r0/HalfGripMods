using System;
using System.Collections.Generic;
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
		internal static bool Blocking => Active || ReadOnly;

		internal static void Enter()
		{
			if (Active)
			{
				return;
			}
			LifetimeStats.Flush(); // totals waiting to be written belong to the real save, so they go in before the copy is taken
			SaveData.GetInt("multiplayer.sandbox"); // makes the game read its files now, so the copy is of the real save
			ints = new Dictionary<string, int>(Table<int>("ints"));
			floats = new Dictionary<string, float>(Table<float>("floats"));
			strings = new Dictionary<string, string>(Table<string>("strings"));
			dirtySave = Flag("dirtySave");
			dirtyHistory = Flag("dirtyHistory");
			Active = true;
			Mod.Log.LogInfo("Save sandbox on: nothing reaches the save or Steam achievements until you leave multiplayer.");
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
			foreach (string key in live.Keys.Where(k => !IsSetting(k)).ToList())
			{
				live.Remove(key);
			}
			foreach (KeyValuePair<string, T> pair in copy)
			{
				if (!IsSetting(pair.Key))
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
