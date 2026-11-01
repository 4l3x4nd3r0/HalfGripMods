using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HalfGripTools;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>Test scenarios added with 0.11.0: HOST right after the garage, the mod bus, a big grid with bots, a spectator.</summary>
	internal static class RigExtra
	{
		private static IEnumerator Wait(float seconds)
		{
			yield return new WaitForSecondsRealtime(seconds);
		}

		// ------------------------------------------------------------------ the bug of 0.10.4

		/// <summary>
		/// A player leaves the multiplayer garage and presses HOST ROOM at once. Steam has not answered yet (the room is not Active), and the
		/// garage's pending restore used to switch the save sandbox off in that gap: the whole room then ran on the real save.
		/// </summary>
		internal static IEnumerator GarageHost()
		{
			yield return RigScenarios.WaitForMenu();
			Flow.OpenMultiplayerScreen();
			yield return Wait(4f);
			RigRoom.BeforeText = Rig.SaveText();
			string before = Rig.Fingerprint(RigRoom.BeforeText);
			RigScenarios.Click("GARAGE");
			yield return RigScenarios.WaitFor(() => Flow.InGarage && SaveSandbox.Active, 40f, "the solo garage is open with the save sandbox on");
			yield return Wait(4f);
			RigScenarios.Check(SaveData.GetString("inventory").Length > 0, "the garage's stand-in loadout is in memory (the very thing that must never reach the save)");
			Garage.ExitSolo(); // what Esc in the garage ends up doing
			yield return RigScenarios.WaitFor(() => Flow.InMenu, 40f, "back in the menu after the garage");
			bool dropped = false;
			bool unblocked = false;
			Room.Host(); // the first frame the menu is up: the garage's restore is still pending
			RigScenarios.Check(Room.Engaged, "the room counts as engaged as soon as HOST is pressed");
			float until = Time.realtimeSinceStartup + 30f;
			while (!(Room.Active && Room.Code.Length == 6) && Time.realtimeSinceStartup < until)
			{
				dropped |= !SaveSandbox.Active;
				unblocked |= !SaveSandbox.Blocking;
				yield return null;
			}
			RigScenarios.Check(Room.Active, "the room was made");
			until = Time.realtimeSinceStartup + 8f;
			while (Time.realtimeSinceStartup < until)
			{
				dropped |= !SaveSandbox.Active;
				unblocked |= !SaveSandbox.Blocking;
				yield return null;
			}
			RigScenarios.Check(!dropped, "the save sandbox stayed on from HOST to the room being up");
			RigScenarios.Check(!unblocked, "saving stayed blocked the whole time");
			Room.Leave("");
			yield return RigRoom.Finish(before, true);
		}

		// ------------------------------------------------------------------ the mod bus

		private static readonly List<string> got = new List<string>();
		private static int bigLength;
		private static bool left;

		private static void Listen()
		{
			got.Clear();
			Mod.Ctx.Net.OnReceive((from, data) =>
			{
				if (data.Length >= 3000)
				{
					bigLength = data.Length;
				}
				else
				{
					got.Add(Encoding.UTF8.GetString(data));
				}
			});
			HalfGripTools.Net.PlayerLeft += p => left = true;
		}

		private static bool Send(string text, bool reliable)
		{
			return Mod.Ctx.Net.Send(Encoding.UTF8.GetBytes(text), HalfGripTools.Net.Everyone, reliable);
		}

		private static NetMode BusMode()
		{
			return new NetMode { Id = "rig.bus", Name = "BUS MODE", MaxPlayers = 8, MaxLaps = 7 };
		}

		internal static IEnumerator BusHost()
		{
			RigScenarios.Check(HalfGripTools.Net.Available && HalfGripTools.Net.InRoom && HalfGripTools.Net.IsHost, "bus host: HalfGripTools.Net sees the room and the host role");
			RigScenarios.Check(HalfGripTools.Net.LocalId == Room.LocalId && HalfGripTools.Net.HostId == Room.LocalId, "bus host: the ids are the room's");
			RigScenarios.Check(HalfGripTools.Net.Players.Count == RigRoom.Players && HalfGripTools.Net.Players[0].IsHost, "bus host: the player list has everybody, the host first (" + HalfGripTools.Net.Players.Count + ")");
			RigScenarios.Check(HalfGripTools.Net.Phase == NetPhase.Lobby && HalfGripTools.Net.ModeId == "race", "bus host: the phase is the lobby and the mode race");
			Listen();
			Mod.Ctx.Net.RegisterMode(BusMode());
			RigScenarios.Check(HalfGripTools.Net.Modes.Any(m => m.Id == "rig.bus"), "bus host: the registered mode is listed");
			yield return RigScenarios.WaitSignal("busready", s => { }, 60f);

			// the host picks the mode the way the MODE arrows do
			Room.Settings.Mode = RaceSettings.ModeMod;
			Room.Settings.ModeId = "rig.bus";
			Room.Settings.Laps = 5;
			Room.BroadcastRoster();
			RigScenarios.Check(Room.Cap == 8 && Room.Settings.MaxLaps == 7, "bus host: the room's cap and lap limit are the mode's (" + Room.Cap + ", " + Room.Settings.MaxLaps + ")");
			yield return RigScenarios.WaitFor(() => HalfGripTools.Net.ModeId == "rig.bus", 10f, "bus host: mods are told the mode is rig.bus");

			RigScenarios.Check(Send("host>all reliable", true), "bus host: a reliable message went out");
			RigScenarios.Check(Mod.Ctx.Net.Send(new byte[3800]), "bus host: 3800 bytes go out reliably");
			RigScenarios.Check(!Mod.Ctx.Net.Send(new byte[3801]), "bus host: 3801 bytes are refused");
			RigScenarios.Check(!Mod.Ctx.Net.Send(new byte[1101], HalfGripTools.Net.Everyone, false), "bus host: 1101 bytes are refused unreliably");
			RigScenarios.Check(!Mod.Ctx.Net.SendToHost(new byte[1]), "bus host: SendToHost does nothing on the host");
			float until = Time.realtimeSinceStartup + 20f;
			while (!got.Contains("client>host fast") && Time.realtimeSinceStartup < until)
			{
				Send("host>all fast", false);
				yield return Wait(0.25f);
			}
			RigScenarios.Check(got.Contains("client>host reliable"), "bus host: the client's reliable message arrived");
			RigScenarios.Check(got.Contains("client>host fast"), "bus host: the client's unreliable message arrived");
			Send("host>all done", true);
			RigScenarios.Signal("hostdone");
		}

		internal static IEnumerator BusClient()
		{
			RigScenarios.Check(HalfGripTools.Net.Available && HalfGripTools.Net.InRoom && !HalfGripTools.Net.IsHost && HalfGripTools.Net.HostId == Room.HostId, "bus client: HalfGripTools.Net sees the room and the client role");
			RigScenarios.Check(HalfGripTools.Net.Players.Count == RigRoom.Players, "bus client: the player list has everybody (" + HalfGripTools.Net.Players.Count + ")");
			Listen();
			Mod.Ctx.Net.RegisterMode(BusMode());
			RigScenarios.Signal("busready");
			yield return RigScenarios.WaitFor(() => got.Contains("host>all reliable"), 30f, "bus client: the host's reliable message arrived");
			yield return RigScenarios.WaitFor(() => bigLength == 3800, 30f, "bus client: the 3800-byte message arrived whole");
			yield return RigScenarios.WaitFor(() => Room.Settings.Mode == RaceSettings.ModeMod && Room.Settings.ModeId == "rig.bus" && Room.Cap == 8, 10f, "bus client: the roster carries the registered mode (cap " + Room.Cap + ")");
			RigScenarios.Check(HalfGripTools.Net.ModeId == "rig.bus", "bus client: mods are told the mode is rig.bus");
			RigScenarios.Check(Mod.Ctx.Net.SendToHost(Encoding.UTF8.GetBytes("client>host reliable")), "bus client: a reliable message to the host went out");
			float until = Time.realtimeSinceStartup + 25f;
			while (!got.Contains("host>all done") && Time.realtimeSinceStartup < until)
			{
				Mod.Ctx.Net.SendToHost(Encoding.UTF8.GetBytes("client>host fast"), false); // keeps going until the host says it has seen one
				yield return Wait(0.25f);
			}
			RigScenarios.Check(got.Contains("host>all fast"), "bus client: the host's unreliable message arrived");
			yield return RigScenarios.WaitSignal("hostdone", s => { }, 60f);
			// the host leaves after this; the client's own end-of-scenario check covers being dropped
			yield return RigScenarios.WaitFor(() => left || !Room.Active, 40f, "bus client: mods hear that the host left");
		}

		// ------------------------------------------------------------------ a big grid and the bots

		/// <summary>Right after the lights: every car has its own spot, near the racing line, and every bot has what the BOTS settings gave it.</summary>
		internal static IEnumerator CheckBigGrid(bool same, int aiCount)
		{
			TrackManager tm = RaceSync.TM;
			List<Vector3> cars = new List<Vector3> { tm.playerCarController.transform.position };
			cars.AddRange(RaceSync.Humans.Select(r => r.transform.position));
			List<EnemyController> bots = new List<EnemyController>();
			for (int i = 0; i < aiCount; i++)
			{
				EnemyController ai = RaceSync.AiAt(i);
				if (ai != null)
				{
					bots.Add(ai);
					cars.Add(ai.transform.position);
				}
			}
			RigScenarios.Check(bots.Count == aiCount, "grid: all " + aiCount + " AI cars exist (" + bots.Count + ")");
			float nearest = float.MaxValue;
			for (int a = 0; a < cars.Count; a++)
			{
				for (int b = a + 1; b < cars.Count; b++)
				{
					nearest = Mathf.Min(nearest, Vector3.Distance(cars[a], cars[b]));
				}
			}
			PathManager line = tm.lapManager.trackLine;
			float farthest = 0f;
			foreach (Vector3 p in cars)
			{
				farthest = Mathf.Max(farthest, Vector3.Distance(p, line.sampledPoints[line.GetClosestPointIndex(p)]));
			}
			Rig.Log("grid: " + cars.Count + " cars, closest pair " + nearest.ToString("0.0") + " m, farthest from the racing line " + farthest.ToString("0.0") + " m");
			RigScenarios.Check(cars.Count > 6, "grid: more cars than the track's six spots (" + cars.Count + ")");
			RigScenarios.Check(nearest > 1.5f, "grid: no two cars share a spot (closest pair " + nearest.ToString("0.0") + " m)");
			RigScenarios.Check(farthest < 40f, "grid: every car is near the racing line (farthest " + farthest.ToString("0.0") + " m)");

			List<Bots.Plan> plans = Bots.All.ToList();
			RigScenarios.Check(plans.Count == aiCount, "bots: each AI car has a plan (" + plans.Count + ")");
			if (same)
			{
				RigScenarios.Check(plans.All(p => p.Aggression == 2 && p.Kit == Bots.KitCombat && p.NoGun == 0f && p.Ammo == 12), "bots: SAME gave every bot HARDCORE and COMBAT");
				int armed = 0;
				int tough = 0;
				int match = 0;
				foreach (EnemyController ec in bots)
				{
					EnemyGun gun = ec.GetComponentInChildren<EnemyGun>(true);
					Bots.Plan plan = Bots.For(ec);
					if (gun != null && gun.ammo == 12 && gun.bulletsShotPerSession == 3 && Mathf.Abs(gun.timeBetweenShootingSessions - 7f) < 0.01f && Mathf.Abs(ec.paceMultiplier - 1.05f) < 0.001f)
					{
						armed++;
					}
					if (ec.carController != null && ec.carController.carStats != null && ec.carController.carStats.health > 124f)
					{
						tough++;
					}
					if (plan != null && ec.isAggressive == plan.Behaviour.isAggressive)
					{
						match++;
					}
				}
				RigScenarios.Check(armed == bots.Count, "bots: every bot has its gun with 12 rounds, bursts of 3 every 7 s and pace x1.05 (" + armed + " of " + bots.Count + ")");
				RigScenarios.Check(tough == bots.Count, "bots: every bot's health is the COMBAT kit's 125 (" + tough + " of " + bots.Count + ")");
				RigScenarios.Check(match == bots.Count, "bots: every bot drives the style its plan chose (" + match + " of " + bots.Count + ")");
			}
			else
			{
				int kinds = plans.Select(p => p.Aggression * 10 + p.Kit).Distinct().Count();
				RigScenarios.Check(kinds >= 3, "bots: RANDOM gave the bots different aggression and kits (" + kinds + " different)");
			}
			yield return null;
		}

		// ------------------------------------------------------------------ a player with no car

		/// <summary>The room has more players than the mode takes: the last to join watch. This is that player's side.</summary>
		internal static IEnumerator SpectatorClient()
		{
			RigScenarios.Check(Room.Me != null && Room.Me.Spectator && Room.Me.Out, "spectator: the room put this player out of the race with no car");
			yield return RigScenarios.WaitFor(() => Spectator.Active, 15f, "spectator: the drone is on");
			RigScenarios.Check(RaceSync.Humans.Count() == 2, "spectator: the two players who have cars are on the track (" + RaceSync.Humans.Count() + ")");
			Rig.Shot("client_04d_spectator.png");
			yield return RigScenarios.WaitFor(() => Room.Phase == Phase.Results, 150f, "spectator: the race ended with results");
			RigScenarios.Check(!Room.Results.Any(l => !l.Ai && l.Id == Room.LocalId), "spectator: this player has no result line");
			yield return RigScenarios.WaitFor(() => Room.Phase == Phase.Lobby && Flow.InMenu, 90f, "spectator: back in the room after the results");
			yield return RigScenarios.WaitSignal("hostdone", s => { }, 60f);
		}
	}
}
