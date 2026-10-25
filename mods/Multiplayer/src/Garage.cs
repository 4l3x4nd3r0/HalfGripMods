using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// The multiplayer loadout: every item the player has unlocked sits in the garage's inventory for free, and the player fits what
	/// they like onto the car. What is fitted is remembered in a small file next to the config, never in the game's save.
	/// </summary>
	internal static class Loadout
	{
		private static string FilePath { get { return Path.Combine(BepInEx.Paths.ConfigPath, "halfgrip.multiplayer.loadout.txt"); } }

		/// <summary>The game's own "slot:item,slot:item" string.</summary>
		internal static string Equipped = "";

		internal static void Load()
		{
			try
			{
				Equipped = File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : "";
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not read the multiplayer loadout: " + e.Message);
				Equipped = "";
			}
		}

		internal static void Persist()
		{
			try
			{
				File.WriteAllText(FilePath, Equipped);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not save the multiplayer loadout: " + e.Message);
			}
		}

		/// <summary>Every item the game has, mods included (the menu scene has no UpgradeMapper, the catalog is global).</summary>
		internal static List<UpgradeComponent> Items()
		{
			ItemCatalog catalog = ItemCatalog.Instance;
			if (catalog != null && catalog.items != null)
			{
				return catalog.items;
			}
			UpgradeMapper mapper = UpgradeMapper.Resolve();
			return mapper == null ? new List<UpgradeComponent>() : mapper.allUpgrades;
		}

		/// <summary>The Inverter: an hourglass in the cockpit (an <c>NInverter</c>) that rewinds time, which cannot be done in a shared race.</summary>
		private static bool RewindsTime(UpgradeComponent u)
		{
			if (u.id != null && u.id.IndexOf("invert", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
			return (u.interiorPrefab != null && u.interiorPrefab.GetComponentInChildren<NInverter>(true) != null) || (u.exteriorPrefab != null && u.exteriorPrefab.GetComponentInChildren<NInverter>(true) != null);
		}

		/// <summary>
		/// What multiplayer does not offer: slow motion (it would slow every player's game) the Inverter (it rewinds time) and everything that belongs to a single-player
		/// run: map rerolls, run boons (lives, permanent stats) and other consumables. Nitro (NO2) stays.
		/// </summary>
		internal static bool Banned(UpgradeComponent u)
		{
			if (u is NO2)
			{
				return false;
			}
			return u is SlowMotion || u is RerollMapUpgrade || u is RunBoonUpgrade || u is BeerUpgrade || u.upgradeCategory == UpgradeCategory.CONSUMABLE || RewindsTime(u);
		}

		/// <summary>The garage slot (the scene's InventorySlot names) an item of this kind is fitted into.</summary>
		private static string SlotFor(UpgradeComponent u, HashSet<string> taken)
		{
			switch (u.upgradeCategory)
			{
				case UpgradeCategory.CHASSIS: return "Aero";
				case UpgradeCategory.TIRES: return "Tires";
				case UpgradeCategory.SERVO: return "Servo";
				case UpgradeCategory.GUN: return "Gun";
				case UpgradeCategory.ENGINE: return "Engine";
				case UpgradeCategory.MEDIUM: return taken.Contains("MediumSlot") ? "MediumSlot2" : "MediumSlot";
				case UpgradeCategory.PASSIVE: return "PassiveSlot";
				case UpgradeCategory.EXTERNAL: return "ExternalSlot";
				case UpgradeCategory.TRANSMISSION: return "ShifterSlot";
				default: return null;
			}
		}

		/// <summary>
		/// What a player with nothing fitted starts with: the Gunslinger and Nitro Head starting items, fitted, without Nitro Head's
		/// handicap (only the items are taken, never a loadout's permanent stats).
		/// </summary>
		private static List<string> DefaultFitting(HashSet<string> usable)
		{
			List<string> fitted = new List<string>();
			HashSet<string> taken = new HashSet<string>();
			LoadoutCatalog catalog = LoadoutCatalog.Instance;
			if (catalog == null || catalog.loadouts == null)
			{
				return fitted;
			}
			foreach (LoadoutDefinition loadout in catalog.loadouts)
			{
				string name = loadout == null ? "" : ((loadout.Id ?? "") + " " + (loadout.loadoutName ?? "")).ToLowerInvariant().Replace(" ", "").Replace("_", "");
				if (loadout == null || loadout.startingItems == null || !(name.Contains("gunslinger") || name.Contains("nitrohead")))
				{
					continue;
				}
				foreach (UpgradeComponent item in loadout.startingItems)
				{
					string slot = item == null || !usable.Contains(item.id) ? null : SlotFor(item, taken);
					if (slot != null && taken.Add(slot))
					{
						fitted.Add(slot + ":" + item.id);
					}
				}
			}
			return fitted;
		}

		/// <summary>Fills the in-memory save the garage reads its items from: every item (but slow motion) and the remembered fitting.</summary>
		internal static void WriteToSave()
		{
			Load();
			List<UpgradeComponent> usable = Items().Where(u => u != null && !u.disabled && !Banned(u)).GroupBy(u => u.id).Select(g => g.First()).ToList();
			Mod.Log.LogInfo("Not offered in multiplayer: " + string.Join(", ", Items().Where(u => u != null && !u.disabled && Banned(u)).Select(u => u.id).ToArray()));
			HashSet<string> ids = new HashSet<string>(usable.Select(u => u.id));
			List<string> fitted = new List<string>();
			HashSet<string> used = new HashSet<string>();
			foreach (string entry in Equipped.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string[] parts = entry.Split(':');
				if (parts.Length == 2 && ids.Contains(parts[1]) && used.Add(parts[1]))
				{
					fitted.Add(entry);
				}
			}
			if (fitted.Count == 0)
			{
				fitted = DefaultFitting(ids);
				foreach (string entry in fitted)
				{
					used.Add(entry.Split(':')[1]);
				}
				Mod.Log.LogInfo("Nothing was fitted: starting with the Gunslinger and Nitro Head items (" + string.Join(", ", fitted.ToArray()) + ").");
			}
			List<string> inventory = usable.Select(u => u.id).Where(id => !used.Contains(id)).ToList();
			SaveData.SetString("equipped", string.Join(",", fitted.ToArray()));
			SaveData.SetString("inventory", string.Join(",", inventory.ToArray()));
			Mod.Log.LogInfo("Multiplayer loadout: " + usable.Count + " item(s), " + fitted.Count + " fitted");
		}

		/// <summary>The garage just changed what is fitted; remember it.</summary>
		internal static void CaptureFromSave()
		{
			Equipped = SaveData.GetString("equipped");
			Persist();
		}
	}

	/// <summary>The garage in multiplayer: no shop, no money, READY in place of the map, and the way into a race.</summary>
	internal static class Garage
	{
		internal static bool Launching;
		internal static bool Launched;
		/// <summary>The garage opened from the MULTIPLAYER screen, with no room: just to fit the car.</summary>
		internal static bool Solo;
		internal static bool SoloSeen;
		/// <summary>The menu should come up on the MULTIPLAYER screen (after the solo garage).</summary>
		internal static bool ReturnToScreen;

		/// <summary>True while the mod owns the garage: in a room or in the solo garage.</summary>
		internal static bool On { get { return Room.Active || Solo; } }
		internal static bool StandInStarted;
		private static float launchRetryAt;
		private static float launchUntil;

		internal static List<MapDefinition> Maps()
		{
			RunConfig config = RunConfig.Instance;
			List<MapDefinition> maps = new List<MapDefinition>();
			if (config != null)
			{
				foreach (MapDefinition map in config.maps)
				{
					if (map != null && !maps.Any(m => m.name == map.name))
					{
						maps.Add(map);
					}
				}
			}
			return maps;
		}

		internal static MapDefinition FindMap(string name)
		{
			return Maps().FirstOrDefault(m => m.name == name);
		}

		internal static string DefaultMap()
		{
			List<MapDefinition> maps = Maps();
			return maps.Count > 0 ? maps[0].name : "";
		}

		internal static string MapLabel(MapDefinition map)
		{
			return map == null ? "?" : (string.IsNullOrEmpty(map.displayName) ? map.name : Loc.T(map.displayName)).ToUpperInvariant();
		}

		/// <summary>The layouts this map can be raced in.</summary>
		internal static List<int> Variants(MapDefinition map)
		{
			List<int> list = new List<int> { 0 };
			if (map == null)
			{
				return list;
			}
			foreach (TrackVariant v in Enum.GetValues(typeof(TrackVariant)))
			{
				string name = v.ToString();
				bool mirror = name.IndexOf("Mirror", StringComparison.OrdinalIgnoreCase) >= 0;
				bool reverse = name.IndexOf("Revers", StringComparison.OrdinalIgnoreCase) >= 0;
				bool allowed = (mirror && reverse) ? map.allowMirrorReverse : mirror ? map.allowMirror : reverse ? map.allowReverse : v == TrackVariant.Normal;
				if (allowed && !list.Contains((int)v))
				{
					list.Add((int)v);
				}
			}
			return list;
		}

		/// <summary>Opens the garage for the room: a stand-in run so the scene has what it expects, and the multiplayer loadout in the save tables.</summary>
		internal static void Load()
		{
			if (!On || SceneLoader.Instance == null)
			{
				return;
			}
			EnsureRun();
			Loadout.WriteToSave();
			Flow.MarkLoading();
			Mod.Log.LogInfo("Loading the garage");
			SceneLoader.Instance.LoadUpgradeMenu(1f);
		}

		/// <summary>The GARAGE button of the MULTIPLAYER screen, with or without a room: the garage scene in the save sandbox, only to set the loadout.</summary>
		internal static void EnterSolo()
		{
			if (Solo || (Room.Active && Room.Phase != Phase.Lobby) || SceneLoader.Instance == null || SceneLoader.Transitioning)
			{
				return;
			}
			SaveSandbox.Enter();
			Solo = true;
			SoloSeen = false;
			ReturnToScreen = true; // whatever way the player leaves the garage, the menu comes up on the MULTIPLAYER screen
			Load();
		}

		/// <summary>Back to the MULTIPLAYER screen; the sandbox stays on until the menu is really there.</summary>
		internal static void ExitSolo()
		{
			if (!Solo)
			{
				return;
			}
			Solo = false;
			SoloSeen = false;
			ReturnToScreen = true;
			GarageUi.Remove();
			Room.restorePending = !Room.Active; // in a room the sandbox stays on
			Flow.GoToMenu();
		}

		/// <summary>Every frame: the game's own Escape in the garage takes the player to the menu, which ends the solo garage.</summary>
		internal static void Tick()
		{
			if (!Solo)
			{
				return;
			}
			if (Flow.InGarage)
			{
				SoloSeen = true;
			}
			else if (SoloSeen && Flow.InMenu && !Flow.Busy)
			{
				Solo = false;
				SoloSeen = false;
				Room.restorePending = !Room.Active;
				// the menu is already up (it loaded before this was noticed), so put it on the MULTIPLAYER screen now
				Flow.OpenMultiplayerScreen();
			}
		}

		internal static void EnsureRun()
		{
			if (!StandInStarted || !RunState.Active)
			{
				RunState.StartRun();
				StandInStarted = true;
			}
		}

		/// <summary>The garage's start button is READY in a room.</summary>
		internal static void OnStartButton()
		{
			Peer me = Room.Me;
			if (me != null)
			{
				Room.SetReady(!me.Ready);
			}
		}

		/// <summary>The host started the race: configure the stand-in run's node and press the garage's own start button.</summary>
		internal static void Launch(RaceSettings s)
		{
			launchUntil = Time.realtimeSinceStartup + 30f;
			launchRetryAt = 0f;
			TryLaunch(s);
		}

		internal static void RetryLaunch()
		{
			if (Room.Race != null && Time.realtimeSinceStartup < launchUntil && Time.realtimeSinceStartup >= launchRetryAt)
			{
				launchRetryAt = Time.realtimeSinceStartup + 0.4f;
				TryLaunch(Room.Race);
			}
		}

		private static void TryLaunch(RaceSettings s)
		{
			MenuManager menu = UnityEngine.Object.FindFirstObjectByType<MenuManager>();
			if (menu == null || SceneLoader.Instance == null || SceneLoader.Transitioning)
			{
				return;
			}
			MapDefinition map = FindMap(s.Map);
			if (map == null)
			{
				Room.Leave("This game has no map called '" + s.Map + "'.");
				return;
			}
			EnsureRun();
			if (!RunState.HasSelection)
			{
				RunState.Select(RunState.Choices[0]);
			}
			RunNode node = RunState.Selected;
			node.map = map;
			node.variant = (TrackVariant)s.Variant;
			node.unknownMap = false;
			node.shortRace = false;
			node.objective = RaceObjective.None;
			launchUntil = 0f;
			Launched = true;
			Launching = true;
			try
			{
				Mod.Log.LogInfo("Launching " + map.name + " (" + node.variant + ")");
				menu.StartRaceButtonEvent();
			}
			finally
			{
				Launching = false;
			}
			Flow.MarkLoading();
		}
	}

	// Without a shop there is nothing to deal into the shop slots.
	[HarmonyPatch(typeof(UpgradeUIManager), "Awake")]
	internal static class NoShopOffers
	{
		private static bool Prefix()
		{
			return !Garage.On;
		}
	}

	// The garage's start button is READY while in a room, unless the host's start order is being carried out.
	[HarmonyPatch(typeof(MenuManager), nameof(MenuManager.StartRaceButtonEvent))]
	internal static class StartIsReady
	{
		private static bool Prefix()
		{
			if (!Garage.On || Garage.Launching || Garage.Solo)
			{
				return true;
			}
			Garage.OnStartButton();
			return false;
		}
	}

	[HarmonyPatch(typeof(MenuManager), "Start")]
	internal static class GarageStarted
	{
		private static void Postfix(MenuManager __instance)
		{
			if (Garage.Solo)
			{
				GarageUi.Install(__instance);
			}
		}
	}

	// Whatever the player fits in the garage is the multiplayer loadout.
	[HarmonyPatch(typeof(MenuManager), nameof(MenuManager.SaveObjects))]
	internal static class RememberLoadout
	{
		private static void Postfix()
		{
			if (Garage.On)
			{
				Loadout.CaptureFromSave();
			}
		}
	}

	// The pause screen's "abandon run" means leaving the room.
	[HarmonyPatch(typeof(RunFlow), nameof(RunFlow.AbandonToMenu))]
	internal static class AbandonIsLeave
	{
		private static bool Prefix()
		{
			if (Garage.Solo)
			{
				Garage.ExitSolo();
				return false;
			}
			if (!Room.Active)
			{
				return true;
			}
			Room.Leave("You left the room.");
			return false;
		}
	}

	// The host sets the lap count; the game would use its own run settings.
	[HarmonyPatch(typeof(TrackManager), "PrepareRace")]
	internal static class RoomLaps
	{
		private static void Postfix(TrackManager __instance)
		{
			if (Room.Active && Room.Race != null && __instance.lapManager != null)
			{
				__instance.lapManager.maxLaps = Room.Race.Laps;
			}
		}
	}

	// Escape on the Multiplayer screen leaves the room too.
	[HarmonyPatch(typeof(MainMenu), nameof(MainMenu.Back))]
	internal static class BackLeavesRoom
	{
		private static bool Prefix(MainMenu __instance)
		{
			if (Room.Active && Traverse.Create(__instance).Field("view").GetValue<MainMenu.View>() == MainMenu.View.Multiplayer)
			{
				Room.Leave("You left the room.");
				return false; // stay on the MULTIPLAYER screen; Esc once more goes to the main menu
			}
			return true;
		}
	}
}
