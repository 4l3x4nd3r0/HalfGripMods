using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>Drives the player's car along the track line without input, so a race can be run headless.</summary>
	internal sealed class RigBot : MonoBehaviour
	{
		internal static bool Active;
		internal static Vector3 Velocity;

		internal float Speed = 55f;
		private TrackManager tm;
		private PathManager path;
		private Rigidbody rb;
		private int index;

		internal static void Attach(TrackManager manager, float speed)
		{
			CarControllerNew car = manager.playerCarController;
			if (car == null || car.GetComponent<RigBot>() != null)
			{
				return;
			}
			RigBot bot = car.gameObject.AddComponent<RigBot>();
			bot.Speed = speed;
			bot.tm = manager;
			bot.path = manager.lapManager.trackLine;
			bot.rb = car.rb != null ? car.rb : car.GetComponent<Rigidbody>();
			bot.index = bot.path.GetClosestPointIndex(bot.rb.position);
			bot.rb.isKinematic = true;
			Active = true;
			Rig.Log("bot driving at " + speed + " m/s from sample " + bot.index + " of " + bot.path.sampledPoints.Count);
		}

		private void FixedUpdate()
		{
			if (tm == null || !tm.raceStarted || rb == null)
			{
				return;
			}
			List<Vector3> pts = path.sampledPoints;
			Vector3 target = pts[(index + 1) % pts.Count] + Vector3.up * 0.8f;
			Vector3 delta = target - rb.position;
			float step = Speed * Time.fixedDeltaTime;
			if (delta.magnitude <= step)
			{
				index = (index + 1) % pts.Count;
			}
			Vector3 dir = delta.sqrMagnitude > 0.0001f ? delta.normalized : transform.forward;
			rb.MovePosition(Vector3.MoveTowards(rb.position, target, step));
			Vector3 flat = new Vector3(dir.x, 0f, dir.z);
			if (flat.sqrMagnitude > 0.0001f)
			{
				rb.MoveRotation(Quaternion.LookRotation(flat.normalized, Vector3.up));
			}
			Velocity = dir * Speed;
		}
	}

	internal static class RigRace
	{
		private static string Mode { get { return Rig.Arg("-mpmode") ?? "race"; } }

		/// <summary>The results without the AI rows (the AI cars are listed too, with no score).</summary>
		private static List<ResultLine> HumanResults()
		{
			return Room.Results.Where(l => !l.Ai).ToList();
		}

		internal static IEnumerator WaitRaceStarted(string who)
		{
			yield return RigScenarios.WaitFor(() => RaceSync.TM != null && Room.Phase == Phase.Racing, 90f, who + ": the race scene is built and the host said go");
			yield return RigScenarios.WaitFor(() => RaceSync.TM != null && RaceSync.TM.raceStarted, 40f, who + ": the lights went out");
		}

		/// <summary>Host: configures the race the mode asks for and presses START RACE.</summary>
		private static void Configure()
		{
			RaceSettings s = Room.Settings;
			List<MapDefinition> maps = Garage.Maps();
			string wanted = Rig.Arg("-mpmap");
			s.Map = (maps.FirstOrDefault(m => m.name == (wanted ?? "Stadium")) ?? maps[0]).name;
			s.Variant = 0;
			s.Laps = 1;
			s.Ai = 0;
			s.Mode = RaceSettings.ModeRace;
			s.Wreck = RaceSettings.WreckSpectate;
			s.RespawnSeconds = 3f;
			string mode = Mode;
			if (mode == "ai")
			{
				s.Ai = 2;
			}
			if (mode == "combat")
			{
				s.Mode = RaceSettings.ModeDemolition;
			}
			if (mode == "respawn")
			{
				s.Wreck = RaceSettings.WreckRespawn;
			}
			Room.BroadcastRoster();
		}

		private static float TrackProgress(RemoteCar rc)
		{
			return rc == null ? -1f : RaceSync.TM.lapManager.leaderboard.TryGetValue(rc.gameObject, out LapManager.LeaderboardEntry e) ? e.totalDistance : -1f;
		}

		internal static IEnumerator Host()
		{
			string mode = Mode;
			Configure();
			RigScenarios.Click("START RACE");
			yield return RigScenarios.WaitFor(() => Room.Phase == Phase.Loading || Room.Phase == Phase.Racing, 20f, "host: the race was started");
			yield return WaitRaceStarted("host");
			Rig.Log("race mode " + mode + " on " + Room.Race.Map + ", AI " + Room.AiGrid.Count);
			RigScenarios.Check(RaceSync.Humans.Count() == RigRoom.Players - 1, "host: a stand-in car exists for each other player");
			RigScenarios.Check(Room.AiGrid.Count == (mode == "ai" ? 2 : 0), "host: the AI cars are as set");
			yield return new WaitForSecondsRealtime(2f);
			Rig.Shot("host_06_race.png");

			if (mode == "hostquit")
			{
				yield return new WaitForSecondsRealtime(4f);
				Room.Leave("You left the room.");
				yield break;
			}
			if (mode == "combat" || mode == "respawn")
			{
				yield return Combat_Host(mode == "respawn");
			}
			else
			{
				RigBot.Attach(RaceSync.TM, 60f);
				if (mode != "clientquit")
				{
					yield return Watch_Host(mode == "ai");
				}
				if (mode == "watch")
				{
					Room.Leave("You left the room.");
					yield break;
				}
			}
			yield return RigScenarios.WaitFor(() => Room.Phase == Phase.Results, 120f, "host: the race ended with results");
			yield return new WaitForSecondsRealtime(1.5f);
			Rig.Shot("host_07_results.png");
			Rig.Log("results: " + string.Join(" | ", Room.Results.Select(l => l.Name + " state " + l.State + " " + l.Time.ToString("0.0")).ToArray()));
			RigScenarios.Check(HumanResults().Count == (mode == "clientquit" ? RigRoom.Players - 1 : RigRoom.Players), "host: " + HumanResults().Count + " result line(s)");
			RigScenarios.Check(Room.Results.Count(l => l.Ai) == (mode == "ai" ? 2 : 0), "host: the AI rivals are in the results (" + Room.Results.Count(l => l.Ai) + ")");
			if (mode == "clientquit")
			{
				RigScenarios.Check(Room.Peers.Count == 1, "host: the player who left is gone from the room");
			}
			if (mode == "race" || mode == "ai")
			{
				RigScenarios.Check(HumanResults().Count > 0 && HumanResults()[0].Name == "A" && HumanResults()[0].State == 0, "host: the faster bot (A) won");
				RigScenarios.Check(HumanResults().Count > 1 && HumanResults()[1].Name == "B" && HumanResults()[1].State == 0, "host: B finished second");
				if (RigRoom.Players >= 3)
				{
					RigScenarios.Check(HumanResults().Count > 2 && HumanResults()[2].Name == "C" && HumanResults()[2].State == 0, "host: C finished third");
				}
			}
			if (mode == "combat")
			{
				RigScenarios.Check(HumanResults().Count > 1 && HumanResults()[0].Name == "A" && HumanResults()[1].State == 2, "host: A is last standing, B was wrecked");
			}
			yield return RigScenarios.WaitFor(() => Room.Phase == Phase.Results && Flow.InMenu, 60f, "host: the results screen (in the menu) is up");
			yield return new WaitForSecondsRealtime(2f);
			Rig.Shot("host_07b_results_screen.png");
			if (mode == "race" || mode == "ai")
			{
				RigScenarios.Check(HumanResults().Where(l => l.State == 0).All(l => l.BestLap > 0f), "host: every finisher has a best lap in the results");
			}
			RigScenarios.Click("BACK TO LOBBY");
			yield return RigScenarios.WaitFor(() => Room.Phase == Phase.Lobby && Flow.InMenu, 60f, "host: back in the room after the results");
			yield return new WaitForSecondsRealtime(1f);
			RigScenarios.Check(!GameInput.Muted && !CarLink.godMode, "host: the controls are free and the shield is off after the race");
			yield return new WaitForSecondsRealtime(3f);
			Rig.Shot("host_08_garage_again.png");
			RigScenarios.Check(!Room.Me.Ready && !Room.Peers.Any(p => p.Ready), "host: ready flags are cleared for the next race");
			RigScenarios.Signal("hostdone");
		}

		private static IEnumerator Watch_Host(bool ai)
		{
			float until = Time.realtimeSinceStartup + 20f;
			float maxProgress = 0f;
			Vector3 aiStart = Vector3.zero;
			while (Time.realtimeSinceStartup < until)
			{
				RemoteCar b = RaceSync.Humans.FirstOrDefault();
				maxProgress = Mathf.Max(maxProgress, TrackProgress(b));
				yield return new WaitForSecondsRealtime(0.5f);
			}
			RemoteCar remote = RaceSync.Humans.FirstOrDefault();
			Rig.Log("B's stand-in has driven " + maxProgress.ToString("0") + " m on the host's leaderboard");
			RigScenarios.Check(maxProgress > 300f, "host: B's stand-in moves along the track (" + maxProgress.ToString("0") + " m)");
			Rig.Shot("host_06b_race_mid.png");
			if (ai)
			{
				EnemyController real = RaceSync.AiAt(0);
				RigScenarios.Check(real != null && real.alive && real.carController.rb.linearVelocity.magnitude > 5f, "host: the AI car drives");
			}
			if (remote == null)
			{
				RigScenarios.Check(false, "host: B's stand-in is still there");
			}
		}

		private static GunUpgrade Revolver()
		{
			return Loadout.Items().OfType<GunUpgrade>().FirstOrDefault(g => g != null && g.gunType == GunType.REVOLVER && !g.disabled);
		}

		private static IEnumerator Combat_Host(bool respawn)
		{
			TrackManager tm = RaceSync.TM;
			RemoteCar target = RaceSync.Humans.First();
			// wait for B's first state, so the stand-in is where B really is
			yield return new WaitForSecondsRealtime(2f);
			Gun gun = UnityEngine.Object.FindObjectsByType<Gun>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(g => Traverse.Create(g).Property("Fitted").GetValue() != null);
			RigScenarios.Check(gun != null, "host: a gun is fitted (the revolver from the loadout)");
			if (gun == null)
			{
				yield break;
			}
			GunUpgrade upgrade = (GunUpgrade)Traverse.Create(gun).Property("Fitted").GetValue();
			float perHit = upgrade.damage / Mathf.Max(1, upgrade.projectilesPerShot) * Room.Race.Gun;
			int shots = Mathf.CeilToInt(100f / perHit - 0.0001f);
			Rig.Log("revolver " + upgrade.damage + " per shot x" + Room.Race.Gun + " = " + perHit.ToString("0.##") + " per hit, " + shots + " hit(s) to wreck 100 HP");
			RigScenarios.Signal("combat_plan", perHit.ToString("0.####") + "," + shots);
			for (int i = 1; i <= shots; i++)
			{
				Vector3 from = tm.playerCarController.rb.position + Vector3.up * 1.2f;
				Vector3 to = target.Position + Vector3.up * 0.8f;
				Traverse.Create(gun).Method("FireRay", new object[] { from, (to - from).normalized }).GetValue();
				Rig.Log("shot " + i + " of " + shots);
				yield return new WaitForSecondsRealtime(0.6f);
			}
			if (respawn)
			{
				yield return RigScenarios.WaitFor(() => target == null || target.Controller == null || !target.Controller.alive, 15f, "host: B's stand-in was wrecked");
				yield return RigScenarios.WaitFor(() => RaceSync.Humans.Any(r => r.Controller != null && r.Controller.alive && r != target), 20f, "host: B respawned (a fresh stand-in appeared)");
				RigScenarios.Check(!Room.Find(Room.Peers.First(p => !p.Local).Id).Out, "host: B is not out after respawning");
				Rig.Shot("host_06c_respawned.png");
				RigBot.Attach(tm, 60f);
			}
			else
			{
				yield return RigScenarios.WaitFor(() => Room.Peers.First(p => !p.Local).Out, 15f, "host: B is out of the race");
			}
		}

		internal static IEnumerator Client()
		{
			string mode = Mode;
			yield return WaitRaceStarted("client");
			yield return new WaitForSecondsRealtime(2f);
			Rig.Shot("client_04_race.png");
			if (mode == "hostquit")
			{
				yield break; // the host leaves; the end of the scenario checks what happened to this client
			}
			if (mode == "clientquit")
			{
				yield return new WaitForSecondsRealtime(4f);
				Room.Leave("You left the room.");
				yield break;
			}
			if (mode == "combat" || mode == "respawn")
			{
				yield return Combat_Client(mode == "respawn");
			}
			else
			{
				RigBot.Attach(RaceSync.TM, Rig.Label == "C" ? 45f : 52f);
				yield return Watch_Client(mode == "ai");
				if (mode == "watch")
				{
					yield break;
				}
			}
			yield return RigScenarios.WaitFor(() => Room.Phase == Phase.Results, 120f, "client: the race ended with results");
			yield return new WaitForSecondsRealtime(1.5f);
			Rig.Shot("client_05_results.png");
			yield return RigScenarios.WaitFor(() => Room.Phase == Phase.Results && Flow.InMenu, 60f, "client: the results screen is up");
			yield return new WaitForSecondsRealtime(1f);
			Rig.Shot("client_05b_results_screen.png");
			yield return RigScenarios.WaitFor(() => Room.Phase == Phase.Lobby && Flow.InMenu, 60f, "client: back in the room after the results");
			yield return new WaitForSecondsRealtime(1f);
			RigScenarios.Check(!GameInput.Muted && !CarLink.godMode, "client: the controls are free and the shield is off after the race");
			yield return new WaitForSecondsRealtime(3f);
			RigScenarios.Check(Room.Me != null && !Room.Me.Ready, "client: not ready after coming back");
			yield return RigScenarios.WaitSignal("hostdone", s => { }, 60f);
		}

		private static IEnumerator Watch_Client(bool ai)
		{
			float until = Time.realtimeSinceStartup + 20f;
			float best = 0f;
			RemoteCar a = null;
			while (Time.realtimeSinceStartup < until)
			{
				a = RaceSync.Humans.FirstOrDefault();
				best = Mathf.Max(best, TrackProgress(a));
				yield return new WaitForSecondsRealtime(0.5f);
			}
			Rig.Log("A's stand-in has driven " + best.ToString("0") + " m on the client's leaderboard");
			RigScenarios.Check(best > 300f, "client: A's stand-in moves along the track (" + best.ToString("0") + " m)");
			Rig.Shot("client_04b_race_mid.png");
			if (ai)
			{
				RemoteCar proxy = UnityEngine.Object.FindObjectsByType<RemoteCar>(FindObjectsSortMode.None).FirstOrDefault(r => r.IsAi);
				RigScenarios.Check(proxy != null && TrackProgress(proxy) > 100f, "client: the host's AI car shows up and moves (" + (proxy == null ? "none" : TrackProgress(proxy).ToString("0")) + " m)");
			}
		}

		private static IEnumerator Combat_Client(bool respawn)
		{
			string plan = null;
			yield return RigScenarios.WaitSignal("combat_plan", p => plan = p, 60f);
			float perHit = float.Parse(plan.Split(',')[0], System.Globalization.CultureInfo.InvariantCulture);
			int shots = int.Parse(plan.Split(',')[1]);
			CarLink link = RaceSync.TM.playerCarlink;
			float full = link.MaxHp;
			yield return RigScenarios.WaitFor(() => link.hp < full - 0.01f, 30f, "client: took the first hit");
			yield return new WaitForSecondsRealtime(0.1f);
			float firstHp = link.hp;
			Rig.Log("hp after the first hit: " + firstHp + " of " + full);
			RigScenarios.Check(Mathf.Abs((full - firstHp) - perHit) < 0.6f, "client: the first hit took " + (full - firstHp).ToString("0.##") + " HP, expected " + perHit.ToString("0.##"));
			yield return RigScenarios.WaitFor(() => link.hp <= 0f, 30f, "client: wrecked after " + shots + " hits");
			yield return new WaitForSecondsRealtime(1.5f);
			if (respawn)
			{
				yield return RigScenarios.WaitFor(() => link.hp > 0f && !RaceSync.LocalOut, 20f, "client: respawned with HP back");
				RigScenarios.Check(Mathf.Approximately(link.hp, link.MaxHp), "client: full HP after the respawn");
				RigBot.Attach(RaceSync.TM, 52f);
			}
			else
			{
				RigScenarios.Check(RaceSync.LocalOut, "client: out of the race after the wreck");
				RigScenarios.Check(Spectator.Active, "client: the spectator camera is on");
				yield return new WaitForSecondsRealtime(2f);
				Rig.Shot("client_04c_spectating.png");
			}
		}
	}
}
