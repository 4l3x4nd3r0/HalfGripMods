using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Multiplayer
{
	internal enum Phase
	{
		None,
		Lobby,
		Garage,
		Loading,
		Racing,
		Results
	}

	/// <summary>One person in the room.</summary>
	internal sealed class Peer
	{
		internal ulong Id;
		internal string Name = "";
		internal bool Ready;
		internal bool Loaded;
		internal bool Finished;
		internal bool Out;
		internal bool Gone;
		internal float Time;
		internal int Grid = -1;
		internal float Progress;
		internal int Place;
		internal ulong LastAttacker;
		internal float LastAttackTime;
		internal float BestLap;
		internal int Kos;
		internal int Wos;
		internal float Score;

		internal bool Local { get { return Id == Room.LocalId; } }
	}

	/// <summary>The result line for one player.</summary>
	internal struct ResultLine
	{
		internal ulong Id;
		internal string Name;
		internal float Time;
		internal float BestLap;
		internal int Kos;
		internal int Wos;
		internal float Score; // money earned in the race; not kept for AI cars
		internal bool Ai;
		internal float Progress; // host only: how far a car got, to order those who did not finish
		internal int State; // 0 finished, 1 did not finish, 2 wrecked
	}

	/// <summary>
	/// The room: who is in it, what phase it is in, and the host-authoritative rules for getting from lobby to garage to race and back.
	/// The host holds the truth and broadcasts a roster; clients send requests (ready, finished, wrecked) and mirror what the host says.
	/// </summary>
	internal static class Room
	{
		internal const int MaxPlayers = 6;

		internal static Backend B;
		internal static bool Active;
		internal static bool IsHost;
		internal static ulong HostId;
		internal static string Code = "";
		internal static Phase Phase;
		internal static bool Locked;
		internal static readonly List<Peer> Peers = new List<Peer>();
		internal static RaceSettings Settings = new RaceSettings();
		/// <summary>The map of the last race this machine started as host, so RANDOM does not roll it twice in a row.</summary>
		private static string lastMap = "";
		internal static RaceSettings Race; // the settings of the race in progress
		internal static List<int> AiGrid = new List<int>();
		internal static int Seed;
		internal static bool GoReceived;
		internal static List<ResultLine> Results = new List<ResultLine>();
		internal static string Busy = "";
		internal static string Notice = "";
		internal static float NoticeUntil;
		internal static event Action Changed;

		internal static bool restorePending;
		private static float helloDeadline;
		private static float helloResend;
		private static float loadDeadline;
		internal static float resultsAt;
		private static float firstFinishAt;
		private static float lastPing;
		private static readonly Dictionary<ulong, float> lastHeard = new Dictionary<ulong, float>();

		internal static ulong LocalId { get { return B == null ? 0UL : B.LocalId; } }

		internal static Peer Me { get { return Peers.FirstOrDefault(p => p.Local); } }

		internal static Peer Find(ulong id) { return Peers.FirstOrDefault(p => p.Id == id); }

		internal static bool AllReady { get { return Peers.Count > 0 && Peers.Where(p => p.Id != HostId).All(p => p.Ready); } }

		internal static bool InRace { get { return Phase == Phase.Loading || Phase == Phase.Racing || Phase == Phase.Results; } }

		internal static void Notify()
		{
			if (Changed != null)
			{
				Changed();
			}
		}

		internal static void Say(string text, float seconds = 6f)
		{
			Notice = text;
			NoticeUntil = Time.realtimeSinceStartup + seconds;
			Mod.Log.LogInfo("Room: " + text);
			Notify();
		}

		// ------------------------------------------------------------------ backend

		internal static bool EnsureBackend()
		{
			if (B == null)
			{
				if (Rig.On && Rig.Arg("-mplocal") != null)
				{
					ulong id;
					ulong.TryParse(Rig.Arg("-mpid") ?? "1", out id);
					B = new LocalBackend(System.IO.Path.Combine(Rig.Dir, "net"), id, Rig.Label);
				}
				else
				{
					B = new SteamBackend();
				}
				B.MemberJoined = OnMemberJoined;
				B.MemberLeft = OnMemberLeft;
				B.InviteRequested = OnInviteRequested;
				Mod.Log.LogInfo("Network backend: " + B.Name + (B.Ready ? "" : " (not ready)"));
			}
			if (!B.Ready)
			{
				Say("Steam is not running, so there is no multiplayer. Start the game through Steam.");
				return false;
			}
			return true;
		}

		internal static void Shutdown()
		{
			if (Active)
			{
				Leave("");
			}
			if (B != null)
			{
				B.Dispose();
				B = null;
			}
		}

		// ------------------------------------------------------------------ host

		internal static string NewCode()
		{
			const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
			System.Random random = new System.Random();
			StringBuilder code = new StringBuilder();
			for (int i = 0; i < 6; i++)
			{
				code.Append(alphabet[random.Next(alphabet.Length)]);
			}
			return code.ToString();
		}

		internal static string NormalizeCode(string text)
		{
			StringBuilder code = new StringBuilder();
			foreach (char c in (text ?? "").ToUpperInvariant())
			{
				if (char.IsLetterOrDigit(c))
				{
					code.Append(c);
				}
			}
			return code.ToString();
		}

		internal static void Host()
		{
			if (Active || Busy.Length > 0 || !EnsureBackend())
			{
				return;
			}
			SaveSandbox.Enter();
			Busy = "Creating the room...";
			Notify();
			B.Create(MaxPlayers, (ok, error) =>
			{
				Busy = "";
				if (!ok)
				{
					Fail(error);
					return;
				}
				Code = NewCode();
				B.SetRoomData("hg_mp", Checksums.Protocol.ToString());
				B.SetRoomData("hg_code", Code);
				B.SetRoomData("hg_sum", Checksums.Combined);
				B.SetRoomData("hg_mods", Checksums.ModLine);
				B.SetRoomData("hg_game", Application.version);
				B.SetRoomData("hg_host", B.LocalName);
				Active = true;
				IsHost = true;
				HostId = LocalId;
				Phase = Phase.Lobby;
				Locked = false;
				Peers.Clear();
				Peers.Add(new Peer { Id = LocalId, Name = CleanName(B.LocalName) });
				Settings = Pvp.Defaults();
				Settings.Map = Garage.DefaultMap();
				lastHeard.Clear();
				Mod.Log.LogInfo("Hosting room " + Code + " on " + B.Name);
				Notify();
			});
		}

		internal static void SetLocked(bool locked)
		{
			if (!IsHost || !Active)
			{
				return;
			}
			Locked = locked;
			B.SetJoinable(!locked);
			B.SetRoomData("hg_locked", locked ? "1" : "0");
			BroadcastRoster();
		}

		// ------------------------------------------------------------------ join

		internal static void JoinCode(string text)
		{
			string code = NormalizeCode(text);
			if (Active || Busy.Length > 0)
			{
				return;
			}
			if (code.Length != 6)
			{
				Say("Room codes have 6 letters and numbers.");
				return;
			}
			if (!EnsureBackend())
			{
				return;
			}
			SaveSandbox.Enter();
			Busy = "Looking for room " + code + "...";
			Notify();
			B.Find(code, room =>
			{
				if (room == null)
				{
					Fail("There is no room with that code.");
					return;
				}
				JoinRoom(room);
			});
		}

		/// <summary>Joins a room found by code, by an invite or from the command line.</summary>
		internal static void JoinRoom(RoomRef room)
		{
			if (Active || (Busy.Length > 0 && !Busy.StartsWith("Looking")))
			{
				return;
			}
			if (!EnsureBackend())
			{
				return;
			}
			SaveSandbox.Enter();
			string mismatch = room.Data.Count > 0 ? Mismatch(room.Get("hg_mp"), room.Get("hg_sum"), room.Get("hg_mods"), room.Get("hg_game")) : null;
			if (mismatch != null)
			{
				Fail(mismatch);
				return;
			}
			Busy = "Joining...";
			Notify();
			B.Join(room, (ok, error) =>
			{
				if (!ok)
				{
					Fail(error);
					return;
				}
				// Invites carry no room data until we are inside, so check again with the real numbers.
				string late = Mismatch(B.GetRoomData("hg_mp"), B.GetRoomData("hg_sum"), B.GetRoomData("hg_mods"), B.GetRoomData("hg_game"));
				if (late != null)
				{
					B.Leave();
					Fail(late);
					return;
				}
				Active = true;
				IsHost = false;
				HostId = B.OwnerId;
				Code = B.GetRoomData("hg_code");
				Phase = Phase.Lobby;
				Peers.Clear();
				Peers.Add(new Peer { Id = LocalId, Name = CleanName(B.LocalName) });
				lastHeard.Clear();
				SendHello();
				helloDeadline = Time.realtimeSinceStartup + 10f;
				helloResend = Time.realtimeSinceStartup + 1.5f;
				Busy = "Waiting for the host...";
				Notify();
			});
		}

		private static void SendHello()
		{
			W w = new W(Msg.Hello).I32(Checksums.Protocol).Str(CleanName(B.LocalName)).Str(Checksums.Combined).Str(Checksums.ModLine).Str(Application.version);
			B.Send(HostId, w.Bytes(), true);
		}

		/// <summary>Null when the room has exactly the mods and game this player has; otherwise a sentence saying what differs.</summary>
		internal static string Mismatch(string protocol, string sum, string mods, string game)
		{
			if (protocol != Checksums.Protocol.ToString())
			{
				return "That room runs a different version of the Multiplayer mod.";
			}
			if (sum == Checksums.Combined)
			{
				return null;
			}
			if (!string.IsNullOrEmpty(game) && game != Application.version)
			{
				return "That room plays game version " + game + ", this is " + Application.version + ".";
			}
			Dictionary<string, string> theirs = ParseMods(mods);
			Dictionary<string, string> ours = ParseMods(Checksums.ModLine);
			List<string> missing = theirs.Keys.Where(k => !ours.ContainsKey(k)).ToList();
			List<string> extra = ours.Keys.Where(k => !theirs.ContainsKey(k)).ToList();
			List<string> different = theirs.Keys.Where(k => ours.ContainsKey(k) && ours[k] != theirs[k]).ToList();
			StringBuilder text = new StringBuilder("Your mods do not match the room's.");
			if (missing.Count > 0)
			{
				text.Append(" Missing: ").Append(string.Join(", ", missing.ToArray())).Append('.');
			}
			if (extra.Count > 0)
			{
				text.Append(" Not in the room: ").Append(string.Join(", ", extra.ToArray())).Append('.');
			}
			if (different.Count > 0)
			{
				text.Append(" Different version or files: ").Append(string.Join(", ", different.ToArray())).Append('.');
			}
			if (missing.Count + extra.Count + different.Count == 0)
			{
				text.Append(" The game or HalfGripTools version is different.");
			}
			return text.ToString();
		}

		private static Dictionary<string, string> ParseMods(string line)
		{
			Dictionary<string, string> map = new Dictionary<string, string>();
			foreach (string entry in (line ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				int at = entry.IndexOf('@');
				if (at > 0)
				{
					map[entry.Substring(0, at)] = entry.Substring(at + 1);
				}
			}
			return map;
		}

		private static void OnInviteRequested(RoomRef room)
		{
			if (Active)
			{
				Say("Leave this room first to join another.");
				return;
			}
			if (!Flow.InMenu)
			{
				// Joining puts the game in the save sandbox; never do that in the middle of somebody's run.
				Say("An invite came in. Go back to the main menu, then use it again.", 10f);
				return;
			}
			Mod.Log.LogInfo("Invite received for room " + room.Handle);
			Flow.OpenMultiplayerScreen();
			JoinRoom(room);
		}

		/// <summary>The game was started from a Steam invite: <c>+connect_lobby &lt;id&gt;</c>.</summary>
		internal static void JoinFromCommandLine()
		{
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length - 1; i++)
			{
				ulong id;
				if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out id) && id != 0UL)
				{
					Mod.Log.LogInfo("Started from an invite to room " + id);
					OnInviteRequested(new RoomRef { Handle = id });
					return;
				}
			}
		}

		// ------------------------------------------------------------------ leave

		internal static void Fail(string text)
		{
			Busy = "";
			if (!Active)
			{
				SaveSandbox.Leave();
				Restore.AfterRoom();
			}
			Say(text, 10f);
		}

		internal static void Leave(string notice)
		{
			if (!Active)
			{
				return; // already left (a second goodbye from the network, say); never touch the sandbox twice
			}
			bool wasActive = Active; Pause.Release(); ViewingResults = false; Garage.Solo = false; Garage.SoloSeen = false; Garage.ReturnToScreen = Flow.ActiveScene != Flow.MenuScene;
			if (Active && B != null)
			{
				// what the others are told is about the leaver, not the leaver's own message
				byte[] bye = new W(Msg.Bye).Str(IsHost ? "The host left the room." : "").Bytes();
				foreach (Peer p in Peers.Where(p => !p.Local).ToList())
				{
					B.Send(p.Id, bye, true);
				}
				B.Leave();
			}
			Active = false;
			IsHost = false;
			Phase = Phase.None;
			Peers.Clear();
			Busy = "";
			Race = null;
			GoReceived = false;
			Locked = false;
			Spectator.Stop();
			Hud.Remove();
			RaceSync.Remove();
			if (wasActive && !Flow.InMenu)
			{
				// The race or garage scene is still alive for a moment while the menu loads, and the game keeps counting stats in it.
				// The sandbox stays on until the menu is really there (see Tick).
				restorePending = true;
				Flow.GoToMenu();
			}
			else
			{
				SaveSandbox.Leave();
				Restore.AfterRoom();
				if (wasActive)
				{
					Flow.GoToMenu();
				}
			}
			if (!string.IsNullOrEmpty(notice))
			{
				Say(notice, 10f);
			}
			Notify();
		}

		private static void OnMemberLeft(ulong id)
		{
			if (!Active || id == LocalId)
			{
				return;
			}
			if (id == HostId)
			{
				Leave("The host left the room.");
				return;
			}
			if (IsHost)
			{
				RemovePeer(id, InRace ? "disconnected" : "left");
			}
		}

		private static void OnMemberJoined(ulong id)
		{
			Mod.Log.LogInfo("Room member joined: " + id);
		}

		private static void RemovePeer(ulong id, string how)
		{
			Peer peer = Find(id);
			if (peer == null)
			{
				return;
			}
			Say(peer.Name + " " + how + ".");
			peer.Gone = true;
			if (Phase == Phase.Racing || Phase == Phase.Loading || Phase == Phase.Results)
			{
				RaceSync.PeerLeft(peer);
				peer.Out = true;
			}
			Peers.Remove(peer);
			lastHeard.Remove(id);
			if (IsHost)
			{
				BroadcastRoster();
				CheckRaceEnd();
			}
			Notify();
		}

		internal static void Kick(ulong id, string reason)
		{
			if (!IsHost)
			{
				return;
			}
			B.Send(id, new W(Msg.Kick).Str(reason).Bytes(), true);
			RemovePeer(id, "was removed");
		}

		// ------------------------------------------------------------------ messages

		internal static void SendTo(ulong id, W w, bool reliable = true)
		{
			if (B != null && id != LocalId)
			{
				B.Send(id, w.Bytes(), reliable);
			}
		}

		internal static void SendAll(W w, bool reliable = true)
		{
			if (B == null)
			{
				return;
			}
			byte[] bytes = w.Bytes();
			foreach (Peer p in Peers)
			{
				if (!p.Local && !p.Gone)
				{
					B.Send(p.Id, bytes, reliable);
				}
			}
		}

		internal static void SendHost(W w, bool reliable = true)
		{
			if (B != null && !IsHost)
			{
				B.Send(HostId, w.Bytes(), reliable);
			}
		}

		private static void WriteRoster(W w)
		{
			w.U8((int)Phase).Bool(Locked);
			Settings.Write(w);
			w.U64(HostId).Str(Code);
			w.U8(Peers.Count);
			foreach (Peer p in Peers)
			{
				w.U64(p.Id).Str(p.Name).Bool(p.Ready).Bool(p.Out).Bool(p.Finished);
			}
		}

		internal static void BroadcastRoster()
		{
			if (!IsHost || !Active)
			{
				return;
			}
			W w = new W(Msg.Roster);
			WriteRoster(w);
			SendAll(w);
			Notify();
		}

		internal static void Receive(ulong from, byte[] data)
		{
			R r;
			try
			{
				r = new R(data);
			}
			catch (Exception)
			{
				return;
			}
			try
			{
				lastHeard[from] = Time.realtimeSinceStartup;
				Handle(from, r);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Dropped a " + r.Type + " packet from " + from + ": " + e.Message);
			}
		}

		private static void Handle(ulong from, R r)
		{
			if (!Active && r.Type != Msg.Kick && r.Type != Msg.Bye)
			{
				return;
			}
			if (r.Type == Msg.Hello)
			{
				if (IsHost)
				{
					OnHello(from, r);
				}
				return;
			}
			bool fromHost = from == HostId;
			Peer sender = Find(from);
			if (sender == null && !fromHost && r.Type != Msg.Welcome && r.Type != Msg.Kick && r.Type != Msg.Bye)
			{
				return; // not somebody in our room
			}
			switch (r.Type)
			{
				case Msg.Welcome:
					if (fromHost && !IsHost)
					{
						OnWelcome(r);
					}
					break;
				case Msg.Roster:
					if (fromHost && !IsHost)
					{
						ApplyRoster(r);
					}
					break;
				case Msg.Kick:
					if (fromHost)
					{
						Leave(r.Str());
					}
					break;
				case Msg.Bye:
					{
						string why = r.Str();
						if (fromHost && !IsHost)
						{
							Leave(string.IsNullOrEmpty(why) ? "The host closed the room." : why);
						}
						else if (IsHost)
						{
							RemovePeer(from, InRace ? "disconnected" : "left");
						}
					}
					break;
				case Msg.SetReady:
					if (IsHost && sender != null)
					{
						sender.Ready = r.Bool() && Phase == Phase.Lobby;
						BroadcastRoster();
					}
					break;
				case Msg.Start:
					if (fromHost && !IsHost)
					{
						OnStart(r);
					}
					break;
				case Msg.Loaded:
					if (IsHost && sender != null)
					{
						sender.Loaded = true;
						CheckAllLoaded();
					}
					break;
				case Msg.Go:
					if (fromHost && !IsHost)
					{
						Phase = Phase.Racing;
						GoReceived = true;
						Notify();
					}
					break;
				case Msg.Car:
					RaceSync.OnCar(from, r);
					break;
				case Msg.Hit:
					Combat.OnHit(from, r);
					break;
				case Msg.Shot:
					Cues.OnShot(from, r);
					break;
				case Msg.Rocket:
					Cues.OnRocket(from, r);
					break;
				case Msg.RocketEnd:
					Cues.OnRocketEnd(from, r);
					break;
				case Msg.Wrecked:
					OnWrecked(from, r);
					break;
				case Msg.Respawned:
					RaceSync.OnRespawned(from, r);
					break;
				case Msg.Finished:
					OnFinished(from, r);
					break;
				case Msg.Results:
					if (fromHost && !IsHost)
					{
						OnResults(r);
					}
					break;
				case Msg.AiState:
					if (fromHost && !IsHost)
					{
						RaceSync.OnAiState(r);
					}
					break;
				case Msg.AiHit:
					if (IsHost)
					{
						Combat.OnAiHit(from, r);
					}
					break;
				case Msg.AiDead:
					if (fromHost && !IsHost)
					{
						RaceSync.OnAiDead(r);
					}
					break;
				case Msg.Despawn:
					RaceSync.OnDespawn(from);
					break;
				case Msg.EndRace:
					if (IsHost && sender != null)
					{
						ForceEnd();
					}
					break;
				case Msg.Stats:
					if (sender != null)
					{
						sender.BestLap = r.F32();
						sender.Kos = r.U8();
						sender.Score = r.F32();
					}
					break;
				case Msg.Ping:
					break;
			}
		}

		private static void OnHello(ulong from, R r)
		{
			int protocol = r.I32();
			string name = CleanName(r.Str());
			string sum = r.Str();
			string mods = r.Str();
			string game = r.Str();
			Peer existing = Find(from);
			string reason = null;
			if (existing != null)
			{
				SendTo(from, WelcomeMessage(true, ""));
				return;
			}
			if (protocol != Checksums.Protocol || sum != Checksums.Combined)
			{
				reason = Mismatch(protocol.ToString(), sum, mods, game);
			}
			else if (Locked)
			{
				reason = "The host has locked the room.";
			}
			else if (Phase > Phase.Lobby)
			{
				reason = "A race is in progress. Try again when it is over.";
			}
			else if (Peers.Count >= MaxPlayers)
			{
				reason = "The room is full.";
			}
			if (reason != null)
			{
				Mod.Log.LogInfo("Refused " + name + " (" + from + "): " + reason);
				SendTo(from, WelcomeMessage(false, reason));
				return;
			}
			Peers.Add(new Peer { Id = from, Name = name });
			Mod.Log.LogInfo(name + " joined the room (" + from + ")");
			Say(name + " joined.");
			SendTo(from, WelcomeMessage(true, ""));
			BroadcastRoster();
		}

		private static W WelcomeMessage(bool ok, string reason)
		{
			W w = new W(Msg.Welcome).Bool(ok).Str(reason);
			WriteRoster(w);
			return w;
		}

		private static void OnWelcome(R r)
		{
			bool ok = r.Bool();
			string reason = r.Str();
			if (!ok)
			{
				Leave(reason);
				return;
			}
			helloDeadline = 0f;
			Busy = "";
			ApplyRoster(r);
		}

		private static void ApplyRoster(R r)
		{
			Phase phase = (Phase)r.U8();
			bool locked = r.Bool();
			RaceSettings settings = RaceSettings.Read(r);
			ulong host = r.U64();
			string code = r.Str();
			int count = r.U8();
			List<Peer> fresh = new List<Peer>();
			for (int i = 0; i < count && i < MaxPlayers; i++)
			{
				ulong id = r.U64();
				Peer peer = Find(id) ?? new Peer { Id = id };
				peer.Name = CleanName(r.Str());
				peer.Ready = r.Bool();
				peer.Out = r.Bool();
				peer.Finished = r.Bool();
				fresh.Add(peer);
			}
			HostId = host;
			Code = code;
			Locked = locked;
			Settings = settings;
			Peers.Clear();
			Peers.AddRange(fresh);
			Phase old = Phase;
			if (phase != Phase.Loading && phase != Phase.Racing)
			{
				Phase = phase;
				if (phase == Phase.Lobby)
				{
					ViewingResults = false;
				}
			}
			else if (Phase == Phase.Lobby || Phase == Phase.Garage || Phase == Phase.None)
			{
				Phase = phase;
			}
			if (old != Phase)
			{
				Mod.Log.LogInfo("Phase " + old + " -> " + Phase);
			}
			Notify();
		}

		// ------------------------------------------------------------------ phases

		internal static void SetReady(bool ready)
		{
			Peer me = Me;
			if (me == null || Phase != Phase.Lobby)
			{
				return;
			}
			me.Ready = ready;
			if (IsHost)
			{
				BroadcastRoster();
			}
			else
			{
				SendHost(new W(Msg.SetReady).Bool(ready));
			}
			Notify();
		}

		/// <summary>Host: start the race with the chosen settings.</summary>
		internal static void StartRace()
		{
			if (!IsHost || Phase != Phase.Lobby || !AllReady)
			{
				return;
			}
			RaceSettings s = Settings.Copy();
			System.Random random = new System.Random();
			if (s.Map == RaceSettings.RandomMap)
			{
				// Everybody else is told the real map: only the host rolls. Never the map just raced, if there is another.
				List<MapDefinition> maps = Garage.Maps();
				List<MapDefinition> pool = maps.Where(m => m.name != lastMap).ToList();
				if (pool.Count == 0)
				{
					pool = maps;
				}
				if (pool.Count > 0)
				{
					MapDefinition rolled = pool[random.Next(pool.Count)];
					List<int> layouts = Garage.Variants(rolled);
					s.Map = rolled.name;
					s.Variant = layouts[random.Next(layouts.Count)];
					Mod.Log.LogInfo("Random map: " + s.Map + " (" + (TrackVariant)s.Variant + ")");
				}
			}
			if (Garage.FindMap(s.Map) == null)
			{
				Say("Pick a map first.");
				return;
			}
			lastMap = s.Map;
			Pvp.Save(s);
			List<int> slots = Enumerable.Range(0, 6).OrderBy(i => random.Next()).ToList();
			int humans = Peers.Count;
			W w = new W(Msg.Start);
			s.Write(w);
			int seed = random.Next();
			w.I32(seed).U8(humans);
			for (int i = 0; i < humans; i++)
			{
				w.U64(Peers[i].Id).U8(slots[i]);
			}
			int ai = Math.Min(s.Ai, 6 - humans);
			s.Ai = ai;
			w.U8(ai);
			for (int i = 0; i < ai; i++)
			{
				w.U8(slots[humans + i]);
			}
			Mod.Log.LogInfo("Starting a " + RaceSettings.ModeName(s.Mode) + " on " + s.Map + ", " + s.Laps + " lap(s), " + humans + " player(s), " + ai + " AI");
			SendAll(w);
			// The host runs the same code the clients do, on the bytes it just sent.
			R mine = new R(w.Bytes());
			OnStart(mine);
		}

		private static void OnStart(R r)
		{
			RaceSettings s = RaceSettings.Read(r);
			int seed = r.I32();
			int humans = r.U8();
			for (int i = 0; i < humans; i++)
			{
				ulong id = r.U64();
				int grid = r.U8();
				Peer p = Find(id);
				if (p != null)
				{
					p.Grid = grid;
				}
			}
			int ai = r.U8();
			AiGrid = new List<int>();
			for (int i = 0; i < ai; i++)
			{
				AiGrid.Add(r.U8());
			}
			Race = s;
			Garage.Launched = false; Garage.Solo = false; Garage.SoloSeen = false; ViewingResults = false; humansDoneAt = 0f; RaceSync.Remove(); Cover.Show(); Seed = seed;
			GoReceived = false;
			Phase = Phase.Loading;
			Results.Clear();
			foreach (Peer p in Peers)
			{
				p.Ready = false;
				p.Loaded = false;
				p.Finished = false;
				p.Out = false;
				p.Time = 0f;
				p.Progress = 0f;
				p.Place = 0;
				p.LastAttacker = 0;
				p.BestLap = 0f;
				p.Kos = 0;
				p.Wos = 0;
				p.Score = 0f;
			}
			loadDeadline = Time.realtimeSinceStartup + 40f;
			firstFinishAt = 0f;
			Pvp.LogRevolver(s);
			Garage.Launch(s);
			if (IsHost)
			{
				BroadcastRoster();
			}
			Notify();
		}

		private static void CheckAllLoaded()
		{
			if (!IsHost || Phase != Phase.Loading)
			{
				return;
			}
			if (Peers.All(p => p.Loaded || p.Gone))
			{
				SendGo();
			}
		}

		private static void SendGo()
		{
			Phase = Phase.Racing;
			GoReceived = true;
			SendAll(new W(Msg.Go));
			Mod.Log.LogInfo("Go: all " + Peers.Count + " players loaded");
			Notify();
		}

		/// <summary>The local race scene is built and this player is waiting for the start.</summary>
		internal static void LocalLoaded()
		{
			Peer me = Me;
			if (me != null)
			{
				me.Loaded = true;
			}
			if (IsHost)
			{
				CheckAllLoaded();
			}
			else
			{
				SendHost(new W(Msg.Loaded));
			}
		}

		// ------------------------------------------------------------------ finish, wreck, results

		internal static void LocalFinished(float time, bool crossedLine)
		{
			Peer me = Me;
			if (me == null || me.Finished)
			{
				return;
			}
			me.Finished = crossedLine;
			me.Time = time;
			W w = new W(Msg.Finished).Bool(crossedLine).F32(time);
			if (IsHost)
			{
				SendAll(w);
				MarkFirstFinish(crossedLine);
				CheckRaceEnd();
			}
			else
			{
				SendHost(w);
			}
			Notify();
		}

		private static void OnFinished(ulong from, R r)
		{
			Peer p = Find(from);
			if (p == null)
			{
				return;
			}
			bool crossed = r.Bool();
			float time = r.F32();
			p.Finished = crossed;
			p.Time = time;
			if (IsHost)
			{
				// relay so everybody's roster mirror agrees
				W w = new W(Msg.Finished).Bool(crossed).F32(time);
				foreach (Peer q in Peers)
				{
					if (!q.Local && q.Id != from)
					{
						SendTo(q.Id, w);
					}
				}
				MarkFirstFinish(crossed);
				CheckRaceEnd();
			}
			Notify();
		}

		private static void MarkFirstFinish(bool crossed)
		{
			if (crossed && firstFinishAt <= 0f)
			{
				firstFinishAt = Time.realtimeSinceStartup;
			}
		}

		internal static void LocalWrecked(ulong killer, int aiKiller = 255)
		{
			Peer me = Me;
			if (me == null)
			{
				return;
			}
			bool out_ = Race != null && (Race.Wreck == RaceSettings.WreckSpectate || Race.Mode == RaceSettings.ModeDemolition);
			me.Wos++;
			W w = new W(Msg.Wrecked).U64(killer).U8(aiKiller);
			if (aiKiller != 255 && IsHost)
			{
				RaceSync.AddAiKo(aiKiller);
			}
			if (out_)
			{
				me.Out = true;
			}
			SendAll(w);
			RaceSync.OnWreckedPeer(me, killer);
			if (IsHost)
			{
				CheckRaceEnd();
			}
			Notify();
		}

		private static void OnWrecked(ulong from, R r)
		{
			Peer p = Find(from);
			if (p == null)
			{
				return;
			}
			ulong killer = r.U64();
			int aiKiller = r.U8();
			if (aiKiller != 255 && IsHost)
			{
				RaceSync.AddAiKo(aiKiller);
			}
			p.Wos++;
			if (Race != null && (Race.Wreck == RaceSettings.WreckSpectate || Race.Mode == RaceSettings.ModeDemolition))
			{
				p.Out = true;
			}
			RaceSync.OnWreckedPeer(p, killer);
			if (IsHost)
			{
				CheckRaceEnd();
			}
			Notify();
		}

		private static float humansDoneAt;
		private static float nextPublish;

		/// <summary>Host: has everybody finished or dropped out, the AI cars included? Then the race is over.</summary>
		internal static void CheckRaceEnd()
		{
			if (!IsHost || Phase != Phase.Racing || Race == null)
			{
				return;
			}
			List<Peer> racers = Peers.Where(p => !p.Gone).ToList();
			bool over;
			if (Race.Mode == RaceSettings.ModeDemolition)
			{
				int alive = racers.Count(p => !p.Out) + RaceSync.AiAlive();
				over = racers.Count + RaceSync.AiCount() <= 1 ? alive == 0 : alive <= 1;
			}
			else
			{
				bool humansDone = racers.All(p => p.Finished || p.Out);
				if (humansDone && humansDoneAt <= 0f)
				{
					humansDoneAt = Time.realtimeSinceStartup;
				}
				// the AI cars get to finish too; nobody waits for them for ever
				over = humansDone && (RaceSync.AiAllDone() || Time.realtimeSinceStartup - humansDoneAt > 90f);
			}
			if (over)
			{
				EndRace();
			}
		}

		/// <summary>Every player (not the AI cars) has crossed the line or is out.</summary>
		internal static bool AllPlayersDone
		{
			get { return Peers.Where(p => !p.Gone).All(p => p.Finished || p.Out); }
		}

		/// <summary>Host: a player asked for the results while only AI cars are still racing: the race ends now and those cars do not finish.</summary>
		internal static void ForceEnd()
		{
			if (IsHost && Phase == Phase.Racing && Race != null && AllPlayersDone)
			{
				EndRace();
			}
		}

		/// <summary>The lines of the results, in order, with each one's score (the single-player rule: place payout, survival, kills, earnings).</summary>
		private static List<ResultLine> BuildLines()
		{
			// everybody who drove: the players and, from the host's own cars, the AI rivals
			List<ResultLine> all = new List<ResultLine>();
			foreach (Peer p in Peers)
			{
				bool finishedRace = Race.Mode == RaceSettings.ModeDemolition ? !p.Out : p.Finished;
				all.Add(new ResultLine { Id = p.Id, Name = p.Name, Time = Race.Mode == RaceSettings.ModeDemolition || p.Finished ? p.Time : 0f, BestLap = p.BestLap, Kos = p.Kos, Wos = p.Wos, Score = p.Score, Progress = p.Progress, State = finishedRace ? 0 : (p.Out ? 2 : 1) });
			}
			all.AddRange(RaceSync.AiResults());
			List<ResultLine> lines;
			if (Race.Mode == RaceSettings.ModeDemolition)
			{
				// last one standing first, then by when they were wrecked (later is better)
				lines = all.OrderBy(l => l.State == 2 ? 1 : 0).ThenByDescending(l => l.Time).ToList();
			}
			else
			{
				lines = all.Where(l => l.State == 0).OrderBy(l => l.Time).Concat(all.Where(l => l.State != 0).OrderByDescending(l => l.Progress)).ToList();
			}
			RunConfig config = RunConfig.Instance;
			for (int i = 0; i < lines.Count; i++)
			{
				ResultLine line = lines[i];
				// as in a single-player run: a wrecked car is paid nothing; the others get the payout of their place (only
				// for finishing), survival, the kills and what they picked up on the way (chickens and so on)
				float score = 0f;
				if (config != null && line.State != 2)
				{
					score += line.State == 0 && config.placeGain != null && i < config.placeGain.Length ? config.placeGain[i] : 0f;
					score += config.survivalGain;
					score += line.Kos * config.enemyKillGain;
					score += line.Ai ? 0f : line.Score;
				}
				line.Score = score;
				lines[i] = line;
			}
			return lines;
		}

		/// <summary>Host: sends the results as they stand (the race is still on), or the final ones.</summary>
		private static void PublishResults(bool final)
		{
			if (Race == null)
			{
				return;
			}
			List<ResultLine> lines = BuildLines();
			Results = lines;
			W w = new W(Msg.Results).Bool(final).U8(lines.Count);
			foreach (ResultLine line in lines)
			{
				w.U64(line.Id).Str(line.Name).F32(line.Time).F32(line.BestLap).U8(Math.Min(line.Kos, 255)).U8(Math.Min(line.Wos, 255)).F32(line.Score).Bool(line.Ai).U8(line.State);
			}
			SendAll(w);
			Notify();
			if (final)
			{
				Mod.Log.LogInfo("Race over: " + string.Join(", ", lines.Select(l => l.Name + (l.State == 0 ? " " + l.Time.ToString("0.0") + "s" : l.State == 2 ? " wrecked" : " DNF")).ToArray()));
			}
		}

		private static void EndRace()
		{
			if (Phase != Phase.Racing)
			{
				return;
			}
			PublishResults(true);
			ShowResults();
		}

		private static void OnResults(R r)
		{
			bool final = r.Bool();
			int count = r.U8();
			List<ResultLine> lines = new List<ResultLine>();
			for (int i = 0; i < count && i < 12; i++)
			{
				lines.Add(new ResultLine { Id = r.U64(), Name = CleanName(r.Str()), Time = r.F32(), BestLap = r.F32(), Kos = r.U8(), Wos = r.U8(), Score = r.F32(), Ai = r.Bool(), State = r.U8() });
			}
			Results = lines;
			if (final)
			{
				ShowResults();
			}
			Notify();
		}

		/// <summary>This player looks at the results while others may still be racing: the car leaves the track and the menu opens on the results.</summary>
		internal static bool ViewingResults;

		internal static void LocalEnterResults()
		{
			if (!Active)
			{
				return;
			}
			ViewingResults = true;
			SendAll(new W(Msg.Despawn));
			Flow.GoToMenu();
			Notify();
		}
		private static void ShowResults()
		{
			Phase = Phase.Results;
			ViewingResults = true;
			resultsAt = Time.realtimeSinceStartup;
			RaceSync.OnResults();
			Notify();
		}

		/// <summary>Host: back to the garage for the next race.</summary>
		internal static void BackToLobby()
		{
			if (!IsHost || (Phase != Phase.Results && Phase != Phase.Racing && Phase != Phase.Loading))
			{
				return;
			}
			Phase = Phase.Lobby;
			ViewingResults = false;
			foreach (Peer p in Peers)
			{
				p.Ready = false;
				p.Out = false;
				p.Finished = false;
				p.Loaded = false;
			}
			Race = null;
			BroadcastRoster();
		}

		// ------------------------------------------------------------------ per frame

		internal static void Tick()
		{
			if (restorePending && Flow.InMenu && !Flow.Busy)
			{
				restorePending = false;
				SaveSandbox.Leave();
				Restore.AfterRoom();
				Notify();
			}
			if (B == null)
			{
				return;
			}
			B.Tick();
			ulong from;
			byte[] data;
			int guard = 0;
			while (guard++ < 400 && B.Receive(out from, out data))
			{
				Receive(from, data);
			}
			if (Notice.Length > 0 && Time.realtimeSinceStartup > NoticeUntil)
			{
				Notice = "";
				Notify();
			}
			if (!Active)
			{
				return;
			}
			float now = Time.realtimeSinceStartup;
			if (!IsHost && helloDeadline > 0f)
			{
				if (now > helloDeadline)
				{
					helloDeadline = 0f;
					Leave("The host did not answer.");
					return;
				}
				if (now > helloResend)
				{
					helloResend = now + 1.5f;
					SendHello();
				}
			}
			if (IsHost)
			{
				if (Phase == Phase.Loading && now > loadDeadline)
				{
					foreach (Peer p in Peers.Where(p => !p.Loaded && !p.Local).ToList())
					{
						Kick(p.Id, "Your game took too long to load the race.");
					}
					CheckAllLoaded();
				}
				if (Phase == Phase.Racing && Race != null && now >= nextPublish)
				{
					// the results fill in as cars cross the line (the AI cars too); the race is over when all are in
					nextPublish = now + 1f;
					CheckRaceEnd();
					if (Phase == Phase.Racing)
					{
						PublishResults(false);
					}
				}
				if (Phase == Phase.Racing && firstFinishAt > 0f && now - firstFinishAt > 60f && Race != null && Race.Mode == RaceSettings.ModeRace)
				{
					foreach (Peer p in Peers.Where(p => !p.Finished && !p.Out))
					{
						p.Out = true;
					}
					CheckRaceEnd();
				}
				if (Phase == Phase.Results && now - resultsAt > 120f)
				{
					BackToLobby();
				}
				if (now - lastPing > 2f)
				{
					lastPing = now;
					foreach (Peer p in Peers.Where(p => !p.Local).ToList())
					{
						SendTo(p.Id, new W(Msg.Ping));
						float heard;
						if (lastHeard.TryGetValue(p.Id, out heard) && now - heard > 25f && Phase != Phase.Loading)
						{
							Kick(p.Id, "Lost contact with the host.");
						}
					}
				}
			}
			else if (now - lastPing > 2f)
			{
				lastPing = now;
				SendHost(new W(Msg.Ping));
				float heard;
				if (lastHeard.TryGetValue(HostId, out heard) && now - heard > 25f)
				{
					Leave("Lost contact with the host.");
					return;
				}
			}
			Flow.Sync();
		}

		internal static string CleanName(string name)
		{
			string s = (name ?? "").Replace("<", "").Replace(">", "").Trim();
			if (s.Length == 0)
			{
				s = "Player";
			}
			return s.Length > 15 ? s.Substring(0, 15) : s;
		}
	}
}
