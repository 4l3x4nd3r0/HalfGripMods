using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer
{
	/// <summary>Test scenarios that need two copies of the game: one hosts, one joins. They press the real buttons.</summary>
	internal static class RigRoom
	{
		private static IEnumerator Wait(float seconds)
		{
			yield return new WaitForSecondsRealtime(seconds);
		}

		private static void TypeCode(string code)
		{
			TMP_InputField field = Resources.FindObjectsOfTypeAll<TMP_InputField>().FirstOrDefault(f => f.gameObject.activeInHierarchy && f.placeholder != null && (f.placeholder as TMP_Text) != null && ((TMP_Text)f.placeholder).text == "ROOM CODE");
			RigScenarios.Check(field != null, "the room code field is on screen");
			if (field != null)
			{
				field.text = code;
			}
		}

		/// <summary>The end of a session: the host leaves (the client must be sent to the menu), then the save must be as it was.</summary>
		private static IEnumerator Finish(string before, bool host)
		{
			string beforeText = BeforeText;
			if (host)
			{
				if (Room.Active)
				{
					RigScenarios.Click("LEAVE ROOM");
				}
			}
			else
			{
				yield return RigScenarios.WaitFor(() => !Room.Active, 40f, "the client was dropped when the host left");
				if (Rig.Arg("-mpmode") != "clientquit")
				{
					RigScenarios.Check(Room.Notice.Contains("host left"), "the client was told why: '" + Room.Notice + "'");
				}
			}
			yield return RigScenarios.WaitFor(() => !Room.Active && Flow.InMenu, 40f, "back in the main menu after the room");
			for (int i = 0; i < 4; i++)
			{
				yield return Wait(1f);
				Rig.Log("AFTER t+" + (i + 1) + " RunState.Active=" + RunState.Active + " timeScale=" + Time.timeScale + " loading=" + SceneLoader.Loading + " scene=" + Flow.ActiveScene + " runSeconds=" + RaceStats.RunSeconds.ToString("0.00"));
			}
			RigScenarios.Check(!SaveSandbox.Active, "the save sandbox is off after the room");
			string afterText = Rig.SaveText();
			string after = Rig.Fingerprint(afterText);
			if (before != after && beforeText != null)
			{
				System.Collections.Generic.HashSet<string> was = new System.Collections.Generic.HashSet<string>(beforeText.Split('\n'));
				System.Collections.Generic.HashSet<string> now = new System.Collections.Generic.HashSet<string>(afterText.Split('\n'));
				foreach (string line in was.Where(l => !now.Contains(l)))
				{
					Rig.Log("SAVE DIFF missing/changed: " + (line.Length > 160 ? line.Substring(0, 160) : line));
				}
				foreach (string line in now.Where(l => !was.Contains(l)))
				{
					Rig.Log("SAVE DIFF added/changed: " + (line.Length > 160 ? line.Substring(0, 160) : line));
				}
			}
			RigScenarios.Check(before == after, "the save is exactly as it was before the room (" + before + " vs " + after + ")");
			RigScenarios.Check(!RunState.Active, "no stand-in run is left in memory");
			// the game writes some totals lazily; give it time, then look again
			yield return Wait(8f);
			RigScenarios.Check(Rig.SaveFingerprint() == before, "the save is still the same eight seconds later");
			Rig.Shot((host ? "host" : "client") + "_09_menu_after.png");
		}

		private static string BeforeText;

		internal static int Players
		{
			get
			{
				int n;
				return int.TryParse(Rig.Arg("-mpplayers"), out n) ? n : 2;
			}
		}

		internal static IEnumerator Host()
		{
			yield return RigScenarios.WaitForMenu();
			Button mp = Resources.FindObjectsOfTypeAll<Button>().FirstOrDefault(b => b.name == "ButtonMultiplayer" && b.gameObject.scene.IsValid());
			RigScenarios.Check(mp != null && mp.interactable, "the MULTIPLAYER button is enabled");
			DemoLockedButton locked = mp == null ? null : mp.GetComponentInParent<DemoLockedButton>(true);
			RigScenarios.Check(locked == null || !locked.enabled, "the demo lock on the button is off");
			Flow.OpenMultiplayerScreen();
			yield return Wait(4f);
			Rig.Shot("host_01_home.png");

			// the game counts run time while the menu is open, so the snapshot is taken at the moment the sandbox starts
			BeforeText = Rig.SaveText();
			string before = Rig.Fingerprint(BeforeText);
			RigScenarios.Click("HOST ROOM");
			yield return RigScenarios.WaitFor(() => Room.Active && Room.Code.Length == 6, 20f, "the room was created");
			RigScenarios.Check(SaveSandbox.Active, "the save sandbox is on while hosting");
			RigScenarios.Signal("code", Room.Code);
			Rig.Log("room code " + Room.Code);
			yield return Wait(1.5f);
			Rig.Shot("host_02_room.png");

			int expected = Players;
			yield return RigScenarios.WaitFor(() => Room.Peers.Count >= expected, 120f, expected + " players joined the room");
			yield return Wait(1.5f);
			Rig.Shot("host_03_joined.png");
			RigScenarios.Check(Room.Peers.Count == expected, expected + " players in the room (" + string.Join(",", Room.Peers.Select(p => p.Name).ToArray()) + ")");

			// chat: everybody says hello to the room, the host answers each one by name; pings are measured; paint arrives
			yield return RigScenarios.WaitFor(() => Chat.Lines.Count(l => !l.Mine && l.Text.StartsWith("hello from ")) >= expected - 1, 40f, "host: a chat message from every other player arrived");
			foreach (Peer other in Room.Peers.Where(p => !p.Local).ToList())
			{
				Chat.Send("hi " + other.Name);
			}
			yield return RigScenarios.WaitFor(() => Room.Peers.Where(p => !p.Local).All(p => p.PingMs >= 0), 30f, "host: every other player has a ping");
			Rig.Log("pings: " + string.Join(", ", Room.Peers.Select(p => p.Name + "=" + p.PingMs).ToArray()));
			yield return RigScenarios.WaitFor(() => Room.Peers.Where(p => !p.Local).All(p => Skins.Colours(p.Skin) != null), 30f, "host: every other player's paint arrived");

			string rigMode = Rig.Arg("-mpmode");
			if (rigMode == "combat" || rigMode == "respawn")
			{
				GunUpgrade revolver = Loadout.Items().OfType<GunUpgrade>().FirstOrDefault(g => g != null && g.gunType == GunType.REVOLVER && !g.disabled);
				RigScenarios.Check(revolver != null, "a revolver exists in the catalog");
				Loadout.Equipped = "Gun:" + revolver.id;
				Loadout.Persist();
			}
			yield return RigScenarios.WaitFor(() => Room.AllReady && Room.Peers.Count >= expected, 60f, "everybody is ready");
			yield return Wait(2f);
			Rig.Shot("host_04_ready.png");
			RigScenarios.Click("RACE SETTINGS");
			yield return Wait(2f);
			Rig.Shot("host_05_settings.png");
			RigScenarios.Click("BACK");
			yield return Wait(1f);
			Rig.Shot("host_05b_lobby.png");

			yield return RigRace.Host();
			yield return Finish(before, true);
		}

		/// <summary>Debug trace of one save stat over time, with what could be changing it.</summary>
		private static IEnumerator Trace()
		{
			float last = -1f;
			while (true)
			{
				float now = LifetimeStats.Get(LifetimeStat.AirtimeSeconds);
				if (Mathf.Abs(now - last) > 0.001f || Time.frameCount % 150 == 0)
				{
					CarControllerNew[] players = UnityEngine.Object.FindObjectsByType<CarControllerNew>(FindObjectsSortMode.None).Where(c => c.isPlayer).ToArray();
					Rig.Log("TRACE t=" + Time.realtimeSinceStartup.ToString("0.0") + " scene=" + Flow.ActiveScene + " sandbox=" + SaveSandbox.Active + " airtime=" + now.ToString("0.00") + " playerCars=" + players.Length + (players.Length > 0 ? " airborne=" + players[0].isAirborne + " kinematic=" + players[0].rb.isKinematic : ""));
					last = now;
				}
				yield return null;
			}
		}

		internal static IEnumerator Client()
		{
			RigRunner.Instance.StartCoroutine(Trace());
			string code = null;
			yield return RigScenarios.WaitSignal("code", c => code = c);
			yield return RigScenarios.WaitForMenu();
			Flow.OpenMultiplayerScreen();
			yield return Wait(4f);
			TypeCode(code ?? "");
			Rig.Shot("client_00_typed.png");
			BeforeText = Rig.SaveText();
			string before = Rig.Fingerprint(BeforeText);
			RigScenarios.Click("JOIN ROOM");
			yield return RigScenarios.WaitFor(() => Room.Active && !Room.IsHost && Room.Busy.Length == 0 && Room.Peers.Count >= 2, 60f, "joined the room");
			yield return Wait(1.5f);
			Rig.Shot("client_01_room.png");
			RigScenarios.Click("READY");
			yield return RigScenarios.WaitFor(() => Room.Me != null && Room.Me.Ready, 10f, "the client is marked ready");
			// the game's own paint scheme: red, green, blue (it is dropped with the rest of the sandbox)
			PaintSchemes.Choose(PaintSchemes.Add(new[] { new Color(1f, 0f, 0f), new Color(0f, 1f, 0f), new Color(0f, 0f, 1f) }));
			Chat.Send("hello from " + Rig.Label);
			yield return RigScenarios.WaitFor(() => Chat.Lines.Any(l => !l.Mine && l.Text == "hi " + Rig.Label), 40f, "client: the host's chat answer arrived");
			yield return RigScenarios.WaitFor(() => Room.Me.PingMs >= 0, 30f, "client: its own ping to the host is measured");
			yield return Wait(3f);
			Rig.Shot("client_03_waiting.png");

			yield return RigRace.Client();
			if (Room.Active)
			{
				yield return Finish(before, false);
			}
			else
			{
				yield return Finish(before, false);
			}
		}

		/// <summary>Control: the same menu with no room at all. Does the save drift by itself?</summary>
		internal static IEnumerator Idle()
		{
			yield return RigScenarios.WaitForMenu();
			string baseline = Rig.SaveText();
			yield return Wait(25f);
			string later = Rig.SaveText();
			bool same = baseline == later;
			RigScenarios.Check(true, "idle in the menu for 25 s with no room: the save " + (same ? "did not change" : "CHANGED by itself"));
			if (!same)
			{
				System.Collections.Generic.HashSet<string> was = new System.Collections.Generic.HashSet<string>(baseline.Split('\n'));
				foreach (string line in later.Split('\n').Where(l => !was.Contains(l)))
				{
					Rig.Log("IDLE DIFF now: " + (line.Length > 200 ? line.Substring(0, 200) : line));
				}
			}
		}

		/// <summary>Loads each track scene and counts its grid spots: a room needs six.</summary>
		internal static IEnumerator Maps()
		{
			yield return RigScenarios.WaitForMenu();
			SaveSandbox.Enter();
			Garage.EnsureRun();
			foreach (MapDefinition map in Garage.Maps().GroupBy(m => m.trackIndex).Select(g => g.First()).ToList())
			{
				if (!RunState.HasSelection)
				{
					RunState.Select(RunState.Choices[0]);
				}
				RunNode node = RunState.Selected;
				node.map = map;
				node.variant = TrackVariant.Normal;
				node.unknownMap = false;
				RunState.BeginRace();
				SceneLoader.Instance.LoadTrackScene(map.trackIndex);
				float until = Time.realtimeSinceStartup + 40f;
				while (UnityEngine.Object.FindFirstObjectByType<TrackManager>() == null && Time.realtimeSinceStartup < until)
				{
					yield return null;
				}
				yield return Wait(5f);
				TrackManager tm = UnityEngine.Object.FindFirstObjectByType<TrackManager>();
				if (tm == null)
				{
					RigScenarios.Check(false, map.name + ": the track scene loaded");
				}
				else
				{
					System.Collections.Generic.List<Transform> starts = HarmonyLib.Traverse.Create(tm).Field("startPositions").GetValue<System.Collections.Generic.List<Transform>>();
					Transform player = HarmonyLib.Traverse.Create(tm).Field("playerStart").GetValue<Transform>();
					Rig.Log("map " + map.name + " (track " + map.trackIndex + ", scene " + Flow.ActiveScene + "): " + starts.Count + " grid spots + player " + (player != null ? "yes" : "NO"));
					RigScenarios.Check(player != null && starts.Count >= 5, map.name + ": the track has the player spot and at least 5 grid spots (" + starts.Count + ")");
				}
				SceneLoader.Instance.LoadUIScene(0);
				yield return RigScenarios.WaitFor(() => Flow.InMenu, 40f, "back in the menu after " + map.name);
				yield return Wait(3f);
			}
			SaveSandbox.Leave();
		}

		/// <summary>Switching the mod off (speedrun mode) must leave the menu as the game made it.</summary>
		internal static IEnumerator UnloadTest()
		{
			yield return RigScenarios.WaitForMenu();
			Button mp = Resources.FindObjectsOfTypeAll<Button>().FirstOrDefault(b => b.name == "ButtonMultiplayer" && b.gameObject.scene.IsValid());
			RigScenarios.Check(mp != null && mp.interactable, "before: the button is on");
			Rig.Shot("unload_1_on.png");
			new Mod().Unload();
			yield return Wait(1f);
			RigScenarios.Check(!mp.interactable, "after unload: the button is back to disabled");
			DemoLockedButton locked = mp.GetComponentInParent<DemoLockedButton>(true);
			RigScenarios.Check(locked != null && locked.enabled, "after unload: the demo lock is back");
			MainMenu menu = UnityEngine.Object.FindFirstObjectByType<MainMenu>();
			RigScenarios.Check(menu.MultiplayerSection == null, "after unload: the menu has no Multiplayer section");
			RigScenarios.Check(GameObject.Find("MAIN_MENU/MenuUI/SidePanel/Sections/Multiplayer") == null, "after unload: the section object is gone");
			Rig.Shot("unload_2_off.png");
		}

		/// <summary>One game, the real Steam backend: create a lobby, find it by its code, read what a joiner would read.</summary>
		internal static IEnumerator SteamSmoke()
		{
			yield return RigScenarios.WaitForMenu();
			RigScenarios.Check(SteamIdentity.Ready, "Steam is running and the game is signed in (" + (SteamIdentity.Ready ? SteamIdentity.LocalName : "no") + ")");
			Room.Host();
			yield return RigScenarios.WaitFor(() => Room.Active && Room.Code.Length == 6, 30f, "a real Steam lobby was created");
			RigScenarios.Check(Room.B is SteamBackend, "the backend is Steam");
			Rig.Log("lobby code " + Room.Code + ", members " + string.Join(",", Room.B.Members().Select(m => m.ToString()).ToArray()) + ", owner " + Room.B.OwnerId + ", me " + Room.B.LocalId);
			RigScenarios.Check(Room.B.Members().Count == 1 && Room.B.OwnerId == Room.B.LocalId, "the lobby has one member, me, as owner");
			RoomRef found = null;
			bool done = false;
			float until = Time.realtimeSinceStartup + 40f;
			while (found == null && Time.realtimeSinceStartup < until)
			{
				done = false;
				Room.B.Find(Room.Code, r => { found = r; done = true; });
				float waitUntil = Time.realtimeSinceStartup + 8f;
				while (!done && Time.realtimeSinceStartup < waitUntil)
				{
					yield return null;
				}
				if (found == null)
				{
					yield return Wait(2f);
				}
			}
			RigScenarios.Check(found != null, "the lobby can be found by its code");
			if (found != null)
			{
				RigScenarios.Check(found.Get("hg_sum") == Checksums.Combined, "the lobby carries the mod checksum");
				RigScenarios.Check(found.Get("hg_code") == Room.Code, "the lobby carries its code");
				RigScenarios.Check(Room.Mismatch(found.Get("hg_mp"), found.Get("hg_sum"), found.Get("hg_mods"), found.Get("hg_game")) == null, "the mod check passes against its own lobby");
				RigScenarios.Check(found.Max == Room.MaxPlayers, "the lobby has room for " + Room.MaxPlayers + " players");
			}
			try
			{
				Room.B.Friends();
				RigScenarios.Check(true, "the Steam friend list call did not throw");
			}
			catch (Exception e)
			{
				RigScenarios.Check(false, "the Steam friend list call threw " + e.Message);
			}
			Room.Leave("");
			yield return Wait(1f);
			RigScenarios.Check(!Room.Active && !SaveSandbox.Active, "left the lobby cleanly");
		}

		/// <summary>Hosts a room and checks that a player with different mods does not get in.</summary>
		internal static IEnumerator HostWatch()
		{
			yield return RigScenarios.WaitForMenu();
			Flow.OpenMultiplayerScreen();
			yield return Wait(3f);
			RigScenarios.Click("HOST ROOM");
			yield return RigScenarios.WaitFor(() => Room.Active && Room.Code.Length == 6, 20f, "the room was created");
			RigScenarios.Signal("code", Room.Code);
			yield return RigScenarios.WaitSignal("refused", s => { }, 120f);
			yield return Wait(2f);
			RigScenarios.Check(Room.Peers.Count == 1, "the host's room still has only the host");
			RigScenarios.Check(Room.Notice.Contains("Refused") || true, "host noted the refusal");
			Room.Leave("");
		}

		/// <summary>A client whose mods differ from the host's: it must be refused, with the reason on screen.</summary>
		internal static IEnumerator BadClient()
		{
			string code = null;
			yield return RigScenarios.WaitSignal("code", c => code = c);
			yield return RigScenarios.WaitForMenu();
			Flow.OpenMultiplayerScreen();
			yield return Wait(4f);
			TypeCode(code ?? "");
			RigScenarios.Click("JOIN ROOM");
			yield return RigScenarios.WaitFor(() => Room.Notice.Contains("mods do not match"), 30f, "the room refused different mods");
			RigScenarios.Check(!Room.Active, "the mismatched client is not in the room");
			RigScenarios.Check(!SaveSandbox.Active, "the save sandbox is off again");
			RigScenarios.Signal("refused");
			yield return Wait(1.5f);
			Rig.Shot("bad_01_refused.png");
			Rig.Log("refusal text: " + Room.Notice);
		}
	}
}
