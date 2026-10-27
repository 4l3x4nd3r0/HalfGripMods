using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// The race scene in a room: puts everybody on the grid, builds the stand-ins for the other cars, holds the countdown until the host
	/// says go, streams this car to the others, and ends the race for this player when they finish or are wrecked.
	/// </summary>
	internal static class RaceSync
	{
		internal static readonly FieldInfo HealthField = AccessTools.Field(typeof(EnemyController), "health");

		private static TrackManager tm;
		private static readonly Dictionary<ulong, RemoteCar> humans = new Dictionary<ulong, RemoteCar>();
		private static readonly List<RemoteCar> aiProxies = new List<RemoteCar>();
		private static readonly List<EnemyController> aiReal = new List<EnemyController>();
		private static readonly HashSet<int> aiReported = new HashSet<int>();
		private static float nextSend;
		private static float nextAi;
		private static float nextProgress;
		private static bool wreckReported;
		private static bool localOut;
		private static bool finishedLocal;
		private static float respawnAt;
		internal static bool WatchAiDone;
		private sealed class AiStat
		{
			internal string Name = "AI";
			internal float Best;
			internal int Lap;
			internal float PrevTotal;
			internal float LastTotal;
			internal float Finish;
			internal float Progress;
			internal bool Dead;
			internal int Kos;
			internal bool Counted;
			internal int HitBy = -1;
			internal float HitAt;
		}

		private static readonly Dictionary<int, AiStat> aiStats = new Dictionary<int, AiStat>();
		private static float localBest;
		private static int koBase;
		private static float nextStats;
		private static float nextNames;
		private static bool finishMuted;
		private static CarControllerNew finishOwner;

		internal static TrackManager TM { get { return tm; } }
		/// <summary>True while another player's stand-in car is being created.</summary>
		internal static bool MakingHuman;
		internal static bool LocalOut { get { return localOut; } }
		internal static bool InRace { get { return tm != null && Room.Active && Room.Race != null; } }
		internal static IEnumerable<RemoteCar> Humans { get { return humans.Values.Where(r => r != null); } }

		internal static RemoteCar RemoteOf(ulong id)
		{
			RemoteCar rc;
			return humans.TryGetValue(id, out rc) && rc != null ? rc : null;
		}

		internal static EnemyController AiAt(int index)
		{
			return index >= 0 && index < aiReal.Count ? aiReal[index] : null;
		}

		internal static int AiIndexOf(EnemyController controller)
		{
			return aiReal.IndexOf(controller);
		}

		internal static void Remove()
		{
			Spectator.Stop();
			Cues.Remove();
			humans.Clear();
			aiProxies.Clear();
			aiReal.Clear();
			aiReported.Clear();
			aiStats.Clear();
			tm = null;
			localOut = false;
			wreckReported = false;
			finishedLocal = false;
			ReleaseFinish();
		}

		/// <summary>The controls and the shield a finished player has are taken away again (new race, or the scene is left).</summary>
		private static void ReleaseFinish()
		{
			CarLink.godMode = false;
			if (finishMuted && finishOwner != null)
			{
				GameInput.Mute(finishOwner, false);
			}
			else if (finishMuted)
			{
				GameInput.Mute(finishOwner, false);
			}
			finishMuted = false;
			finishOwner = null;
		}

		/// <summary>Host: watches the AI cars every frame (lap times, finish, how far they got) so they can be listed in the results.</summary>
		private static void WatchAi()
		{
			LapManager laps = tm != null ? tm.lapManager : null;
			if (laps == null || laps.leaderboard == null)
			{
				return;
			}
			for (int i = 0; i < aiReal.Count; i++)
			{
				EnemyController ec = aiReal[i];
				AiStat stat;
				if (!aiStats.TryGetValue(i, out stat))
				{
					stat = new AiStat();
					aiStats[i] = stat;
				}
				if (ec == null)
				{
					stat.Dead = true;
					continue;
				}
				CarSkinController skin = ec.GetComponent<CarSkinController>();
				if (skin != null && !string.IsNullOrEmpty(skin.carName))
				{
					stat.Name = skin.carName;
				}
				bool wasDead = stat.Dead;
				stat.Dead = !ec.alive;
				if (stat.Dead && !wasDead && !stat.Counted)
				{
					// an AI car that was shot by another AI car a moment ago: that is a KO for the shooter
					stat.Counted = true;
					AiStat shooter;
					ulong human;
					if (stat.HitBy >= 0 && Time.time - stat.HitAt < 5f && stat.HitBy != i && aiStats.TryGetValue(stat.HitBy, out shooter) && !Combat.AiHitter.TryGetValue(i, out human))
					{
						shooter.Kos++;
					}
				}
				LapManager.LeaderboardEntry entry;
				if (!stat.Dead && laps.leaderboard.TryGetValue(ec.gameObject, out entry))
				{
					// a lap is the difference between the total race time at two crossings of the line (the lap timer itself runs before the start)
					if (entry.currentLap > stat.Lap)
					{
						NoteAiLap(stat, stat.LastTotal);
						stat.Lap = entry.currentLap;
					}
					stat.LastTotal = entry.totalTime;
					stat.Progress = entry.totalDistance;
					if (entry.isFinished && stat.Finish <= 0f)
					{
						stat.Finish = entry.totalTime;
						NoteAiLap(stat, entry.totalTime);
					}
				}
			}
		}

		private static void NoteAiLap(AiStat stat, float total)
		{
			float duration = total - stat.PrevTotal;
			if (duration > 1f)
			{
				stat.PrevTotal = total;
				if (stat.Best <= 0f || duration < stat.Best)
				{
					stat.Best = duration;
				}
			}
		}

		internal static int AiCount() { return aiReal.Count; }

		internal static int AiAlive()
		{
			int n = 0;
			foreach (AiStat stat in aiStats.Values)
			{
				if (!stat.Dead && stat.Finish <= 0f)
				{
					n++;
				}
			}
			return n;
		}

		/// <summary>Every AI car has crossed the line or been wrecked (also true when there are none).</summary>
		internal static bool AiAllDone()
		{
			for (int i = 0; i < aiReal.Count; i++)
			{
				AiStat stat;
				if (!aiStats.TryGetValue(i, out stat) || (!stat.Dead && stat.Finish <= 0f))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>A KO for an AI car (it wrecked a player).</summary>
		internal static void AddAiKo(int index)
		{
			AiStat stat;
			if (aiStats.TryGetValue(index, out stat))
			{
				stat.Kos++;
			}
		}

		/// <summary>An AI car's gun hit another AI car.</summary>
		internal static void NoteAiHit(int victim, int shooter)
		{
			AiStat stat;
			if (aiStats.TryGetValue(victim, out stat))
			{
				stat.HitBy = shooter;
				stat.HitAt = Time.time;
			}
		}

		/// <summary>The index of the AI car (host: real, client: stand-in) that owns this gun, or -1.</summary>
		internal static int AiIndexOfGun(EnemyGun gun)
		{
			EnemyController ec = gun == null ? null : gun.GetComponentInParent<EnemyController>();
			if (ec == null)
			{
				return -1;
			}
			RemoteCar rc = ec.GetComponent<RemoteCar>();
			return rc != null ? (rc.IsAi ? rc.AiIndex : -1) : AiIndexOf(ec);
		}

		/// <summary>The AI cars as result lines (they earn no money, so no score).</summary>
		internal static List<ResultLine> AiResults()
		{
			List<ResultLine> lines = new List<ResultLine>();
			for (int i = 0; i < aiReal.Count; i++)
			{
				AiStat stat;
				if (!aiStats.TryGetValue(i, out stat))
				{
					continue;
				}
				bool finished = stat.Finish > 0f;
				lines.Add(new ResultLine { Id = 0UL, Ai = true, Name = Room.CleanName(stat.Name), Time = finished ? stat.Finish : 0f, BestLap = stat.Best, Kos = stat.Kos, Wos = stat.Dead ? 1 : 0, Score = 0f, Progress = stat.Progress, State = finished ? 0 : (stat.Dead ? 2 : 1) });
			}
			return lines;
		}

		/// <summary>True once this player has crossed the line: the car runs free and nothing can hurt it.</summary>
		internal static bool FinishedLocal { get { return finishMuted; } }

		/// <summary>The game reports each lap time of the local car; the best one goes in the results.</summary>
		internal static void NoteLap(float seconds)
		{
			if (seconds > 0f && (localBest <= 0f || seconds < localBest))
			{
				localBest = seconds;
			}
		}

		/// <summary>Best lap and kills of this player to the host, who writes the results.</summary>
		private static void PushStats()
		{
			int kos = Mathf.Max(0, RaceStats.nbEnemiesDestroyed - koBase);
			Peer me = Room.Me;
			if (me != null)
			{
				me.BestLap = localBest;
				me.Kos = kos;
				me.Score = RaceEarnings.Total;
			}
			Room.SendHost(new W(Msg.Stats).F32(localBest).U8(Math.Min(kos, 255)).F32(RaceEarnings.Total));
		}

		/// <summary>The game's scoreboard names the cars; the stand-ins of human players carry the players' names.</summary>
		private static void SyncNames()
		{
			LapManager laps = tm != null ? tm.lapManager : null;
			if (laps == null || laps.leaderboard == null)
			{
				return;
			}
			foreach (RemoteCar rc in humans.Values)
			{
				Peer peer = rc == null ? null : Room.Find(rc.PeerId);
				LapManager.LeaderboardEntry entry;
				if (peer != null && laps.leaderboard.TryGetValue(rc.gameObject, out entry) && entry.carName != peer.Name)
				{
					entry.carName = peer.Name;
					laps.leaderboard[rc.gameObject] = entry;
				}
			}
		}

		// ------------------------------------------------------------------ the grid

		private static Quaternion Heading()
		{
			MethodInfo layout = AccessTools.Method(typeof(TrackManager), "LayoutHeading");
			return layout == null ? Quaternion.Euler(0f, 90f, 0f) : (Quaternion)layout.Invoke(null, new object[] { Quaternion.Euler(0f, 90f, 0f) });
		}

		/// <summary>Replaces the game's own enemy spawning in a room race.</summary>
		internal static void Spawn(TrackManager manager)
		{
			Remove();
			tm = manager;
			Traverse t = Traverse.Create(manager);
			List<Transform> starts = t.Field("startPositions").GetValue<List<Transform>>();
			Transform playerStart = t.Field("playerStart").GetValue<Transform>();
			float gridHeight = t.Field("normalGridHeight").GetValue<float>();
			List<Transform> slots = new List<Transform> { playerStart };
			slots.AddRange(starts.Take(5));
			// a track with fewer grid spots than the room has cars gets extra spots queued up behind the last one
			while (slots.Count < 6)
			{
				Transform last = slots[slots.Count - 1];
				GameObject extra = new GameObject("MpExtraGridSpot" + slots.Count);
				extra.transform.SetPositionAndRotation(last.position - Vector3.right * 9f, last.rotation);
				slots.Add(extra.transform);
			}
			Quaternion heading = Heading();
			List<EnemyController> enemies = t.Field("enemies").GetValue<List<EnemyController>>();
			RaceSettings race = Room.Race;
			Mod.Log.LogInfo("Race scene: " + slots.Count + " grid slots, " + Room.Peers.Count + " player(s), " + Room.AiGrid.Count + " AI");

			foreach (Peer peer in Room.Peers)
			{
				if (peer.Grid < 0 || peer.Grid >= slots.Count)
				{
					Mod.Log.LogWarning(peer.Name + " has no grid slot (" + peer.Grid + ")");
					continue;
				}
				Transform slot = slots[peer.Grid];
				Vector3 at = GridPoint(slot, gridHeight);
				if (peer.Local)
				{
					if (peer.Grid != 0)
					{
						MovePlayer(manager, at, heading);
					}
				}
				else
				{
					humans[peer.Id] = SpawnProxy(manager, enemies, at, heading, peer.Name, peer.Id, false, -1);
				}
			}
			for (int i = 0; i < Room.AiGrid.Count; i++)
			{
				int g = Room.AiGrid[i];
				if (g < 0 || g >= slots.Count)
				{
					continue;
				}
				Vector3 at = GridPoint(slots[g], gridHeight);
				if (Room.IsHost)
				{
					aiReal.Add(SpawnAi(manager, enemies, at, heading, i));
				}
				else
				{
					aiProxies.Add(SpawnProxy(manager, enemies, at, heading, null, 0, true, i));
				}
			}
			TrackManager.endRaceInstead = LocalRaceEnded;
			ReleaseFinish();
			localBest = 0f;
			koBase = RaceStats.nbEnemiesDestroyed;
			nextStats = 0f;
			nextNames = 0f;
			wreckReported = false;
			localOut = false;
			finishedLocal = false;
			aiReported.Clear();
			Cover.Hide();
			Room.LocalLoaded();
		}

		private static Vector3 GridPoint(Transform slot, float gridHeight)
		{
			float y = TrackVariants.Active ? slot.position.y + (1f - gridHeight) : 1f;
			return new Vector3(slot.position.x, y, slot.position.z);
		}

		private static void MovePlayer(TrackManager manager, Vector3 at, Quaternion heading)
		{
			CarControllerNew player = manager.playerCarController;
			if (player == null)
			{
				return;
			}
			at.y = Mathf.Max(at.y, player.transform.position.y);
			AudioListener listener = player.GetComponentInChildren<AudioListener>(true);
			bool on = listener != null && listener.enabled;
			if (on)
			{
				listener.enabled = false;
			}
			player.transform.SetPositionAndRotation(at, heading);
			Rigidbody rb = player.rb != null ? player.rb : player.GetComponent<Rigidbody>();
			if (rb != null)
			{
				rb.position = at;
				rb.rotation = heading;
				rb.linearVelocity = Vector3.zero;
				rb.angularVelocity = Vector3.zero;
			}
			if (on)
			{
				listener.enabled = true;
			}
		}

		/// <summary>A copy of what the game's SpawnEnemies does for one car.</summary>
		private static EnemyController MakeCar(TrackManager manager, List<EnemyController> enemies, Vector3 at, Quaternion heading, string carName, string objectName)
		{
			GameObject car = UnityEngine.Object.Instantiate(manager.enemyCarPrefab, at, heading, manager.enemiesContainer.transform);
			car.name = objectName;
			CarControllerNew cc = car.GetComponent<CarControllerNew>();
			cc.desiredSpeed = UnityEngine.Random.Range(30f, 80f);
			cc.tm = manager;
			car.layer = LayerMask.NameToLayer("Enemy");
			EnemyController ec = car.GetComponent<EnemyController>();
			ec.pathManager = manager.lapManager.GetRandomPath();
			ec.trackManager = manager;
			ec.invincible = false;
			enemies.Add(ec);
			CarSkinController skin = car.GetComponent<CarSkinController>();
			if (carName != null && skin != null)
			{
				skin.carName = carName;
			}
			return ec;
		}

		/// <summary>One of the host's real AI cars: it drives itself.</summary>
		private static EnemyController SpawnAi(TrackManager manager, List<EnemyController> enemies, Vector3 at, Quaternion heading, int index)
		{
			EnemyController ai = MakeCar(manager, enemies, at, heading, null, "Enemy_ai" + index);
			NameTag.Attach(ai.gameObject, () =>
			{
				CarSkinController skin = ai != null ? ai.GetComponent<CarSkinController>() : null;
				return skin != null && !string.IsNullOrEmpty(skin.carName) ? skin.carName : "AI";
			}, false);
			return ai;
		}

		/// <summary>Somebody else's car, or on a client one of the host's AI cars: driven by the network.</summary>
		private static RemoteCar SpawnProxy(TrackManager manager, List<EnemyController> enemies, Vector3 at, Quaternion heading, string carName, ulong peerId, bool ai, int aiIndex)
		{
			EnemyController ec;
			MakingHuman = !ai; // read by the EnemyGun.Awake patch: another player's car must keep its gun parts and never shoot by itself
			try
			{
				ec = MakeCar(manager, enemies, at, heading, carName, ai ? "Enemy_aiproxy" + aiIndex : "Remote_" + carName);
			}
			finally
			{
				MakingHuman = false;
			}
			RemoteCar made = RemoteCar.Make(ec.gameObject, peerId, ai, aiIndex);
			if (!ai)
			{
				Peer owner = Room.Find(peerId);
				if (owner != null)
				{
					Skins.Apply(made, owner.Skin); // paint chosen in the garage, as sent
				}
			}
			return made;
		}

		// ------------------------------------------------------------------ per frame

		internal static void Tick()
		{
			if ((object)tm != null && tm == null)
			{
				Remove(); // the race scene is gone (the player is looking at the results): give back the controls
			}
			if (!Room.Active || Room.Race == null)
			{
				if (tm != null)
				{
					Remove();
				}
				return;
			}
			if (tm == null)
			{
				return;
			}
			if (Room.Phase != Phase.Racing && Room.Phase != Phase.Results)
			{
				return;
			}
			float now = Time.unscaledTime;
			if (Room.Phase == Phase.Racing)
			{
				if (now >= nextSend)
				{
					nextSend = now + 0.05f;
					SendState();
					Cues.SendRockets();
				}
				Cues.Tick();
				WatchWreck();
				if (now >= nextStats)
				{
					nextStats = now + 2f;
					PushStats();
				}
				if (now >= nextNames)
				{
					nextNames = now + 1f;
					SyncNames();
				}
				if (Room.IsHost)
				{
					WatchAi();
					if (now >= nextAi)
					{
						nextAi = now + 0.066f;
						SendAi();
					}
					if (now >= nextProgress)
					{
						nextProgress = now + 0.5f;
						UpdateProgress();
					}
				}
			}
			Hud.Tick();
		}

		private static void SendState()
		{
			CarControllerNew cc = tm.playerCarController;
			if (cc == null || cc.rb == null || localOut)
			{
				return;
			}
			CarLink link = tm.playerCarlink;
			float hp = link != null && link.MaxHp > 0f ? Mathf.Clamp01(link.hp / link.MaxHp) : 1f;
			W w = new W(Msg.Car).Vec(cc.rb.position).Rot(cc.rb.rotation).Vec(cc.rb.linearVelocity).F32(cc.steeringAngleDeg).F32(hp);
			Cues.WriteAim(w, cc);
			Room.SendAll(w, false);
		}

		internal static void OnCar(ulong from, R r)
		{
			RemoteCar rc = RemoteOf(from);
			if (rc == null)
			{
				return;
			}
			Vector3 p = r.Vec();
			Quaternion q = r.Rot();
			Vector3 v = r.Vec();
			float steer = r.F32();
			float hp = r.F32();
			rc.OnState(p, q, v, steer, hp);
			Cues.ReadAim(rc, r);
		}

		private static void SendAi()
		{
			if (aiReal.Count == 0)
			{
				return;
			}
			W w = new W(Msg.AiState).U8(aiReal.Count);
			for (int i = 0; i < aiReal.Count; i++)
			{
				EnemyController ec = aiReal[i];
				if (ec == null)
				{
					w.U8(0).Vec(Vector3.zero).Rot(Quaternion.identity).Vec(Vector3.zero).F32(0f);
					continue;
				}
				Rigidbody rb = ec.carController != null ? ec.carController.rb : null;
				Vector3 p = rb != null ? rb.position : ec.transform.position;
				Quaternion q = rb != null ? rb.rotation : ec.transform.rotation;
				Vector3 v = rb != null ? rb.linearVelocity : Vector3.zero;
				w.U8(ec.alive ? 1 : 0).Vec(p).Rot(q).Vec(v).F32(ec.carController != null ? ec.carController.steeringAngleDeg : 0f);
				if (!ec.alive && aiReported.Add(i))
				{
					ulong killer;
					Combat.AiHitter.TryGetValue(i, out killer);
					Room.SendAll(new W(Msg.AiDead).U8(i).U64(killer));
				}
			}
			Room.SendAll(w, false);
		}

		internal static void OnAiState(R r)
		{
			int count = r.U8();
			for (int i = 0; i < count && i < 8; i++)
			{
				bool alive = r.U8() == 1;
				Vector3 p = r.Vec();
				Quaternion q = r.Rot();
				Vector3 v = r.Vec();
				float steer = r.F32();
				if (alive && i < aiProxies.Count && aiProxies[i] != null)
				{
					aiProxies[i].OnState(p, q, v, steer, 1f);
				}
			}
		}

		internal static void OnAiDead(R r)
		{
			int index = r.U8();
			ulong killer = r.U64();
			if (index >= 0 && index < aiProxies.Count && aiProxies[index] != null)
			{
				Combat.Kill(aiProxies[index], killer == Room.LocalId);
			}
		}

		private static void UpdateProgress()
		{
			LapManager laps = tm.lapManager;
			if (laps == null || laps.leaderboard == null)
			{
				return;
			}
			foreach (Peer p in Room.Peers)
			{
				GameObject car = p.Local ? (tm.playerCarController != null ? tm.playerCarController.gameObject : null) : (RemoteOf(p.Id) != null ? RemoteOf(p.Id).gameObject : null);
				LapManager.LeaderboardEntry entry;
				if (car != null && laps.leaderboard.TryGetValue(car, out entry))
				{
					p.Progress = entry.totalDistance;
				}
			}
		}

		// ------------------------------------------------------------------ this player's race

		private static void WatchWreck()
		{
			CarLink link = tm.playerCarlink;
			if (link == null || localOut && Room.Race.Wreck == RaceSettings.WreckSpectate)
			{
				return;
			}
			if (link.hp <= 0f && !wreckReported && !RaceTracker.Ended)
			{
				wreckReported = true;
				Peer me = Room.Me;
				ulong killer = me != null && me.LastAttacker != 0 && Time.time - me.LastAttackTime < 8f ? me.LastAttacker : 0UL;
				bool out_ = Room.Race.Mode == RaceSettings.ModeDemolition || Room.Race.Wreck == RaceSettings.WreckSpectate;
				if (me != null)
				{
					me.Time = tm.TimeSinceRaceStart;
				}
				TextController.EnqueueRandomText(1.5f, "WRECKED", "TOTALLED", "SCRAPPED");
				int aiKiller = Combat.LastAiShooter >= 0 && Time.time - Combat.LastAiShotAt < 8f && killer == 0UL ? Combat.LastAiShooter : 255;
				Room.LocalWrecked(killer, aiKiller);
				if (out_)
				{
					localOut = true;
					Spectator.Begin();
				}
				else
				{
					respawnAt = Time.unscaledTime + Room.Race.RespawnSeconds;
					Spectator.Stop();
					tm.StartCoroutine(RespawnLater());
				}
			}
		}

		private static IEnumerator RespawnLater()
		{
			while (Time.unscaledTime < respawnAt)
			{
				yield return null;
			}
			CarLink link = tm.playerCarlink;
			if (link == null)
			{
				yield break;
			}
			link.hp = link.MaxHp;
			tm.ResetPlayerOnTrack();
			wreckReported = false;
			TextController.EnqueueRandomText(1f, "BACK IN THE RACE");
			CarControllerNew cc = tm.playerCarController;
			Room.SendAll(new W(Msg.Respawned).Vec(cc.rb.position).Rot(cc.rb.rotation));
		}

		internal static void OnRespawned(ulong from, R r)
		{
			Vector3 p = r.Vec();
			Quaternion q = r.Rot();
			Peer peer = Room.Find(from);
			if (peer == null || tm == null)
			{
				return;
			}
			RemoteCar old = RemoteOf(from);
			if (old != null && old.Controller != null && old.Controller.alive)
			{
				old.OnState(p, q, Vector3.zero, 0f, 1f);
				return;
			}
			// the old stand-in was wrecked and is on its way out: build a fresh one
			List<EnemyController> enemies = Traverse.Create(tm).Field("enemies").GetValue<List<EnemyController>>();
			RemoteCar fresh = SpawnProxy(tm, enemies, p, q, peer.Name, from, false, -1);
			fresh.OnState(p, q, Vector3.zero, 0f, 1f);
			humans[from] = fresh;
		}

		internal static void OnWreckedPeer(Peer peer, ulong killer)
		{
			if (peer == null)
			{
				return;
			}
			string killerName = killer == 0UL ? null : (Room.Find(killer) != null ? Room.Find(killer).Name : null);
			string text = killerName != null ? killerName.ToUpperInvariant() + " WRECKED " + peer.Name.ToUpperInvariant() : peer.Name.ToUpperInvariant() + " WRECKED";
			if (peer.Local)
			{
				// the local player's own call-out was shown already
				text = killerName != null ? "WRECKED BY " + killerName.ToUpperInvariant() : null;
			}
			if (text != null)
			{
				TextController.EnqueueRandomText(2f, text);
			}
			if (!peer.Local)
			{
				RemoteCar rc = RemoteOf(peer.Id);
				if (rc != null && rc.Controller != null && rc.Controller.alive)
				{
					Combat.Kill(rc, killer == Room.LocalId);
				}
			}
		}

		/// <summary>Somebody went to the results screen: their car leaves the track the way a wreck does.</summary>
		internal static void OnDespawn(ulong from)
		{
			RemoteCar rc = RemoteOf(from);
			if (rc != null && rc.Controller != null && rc.Controller.alive)
			{
				Combat.Kill(rc, false);
			}
		}

		/// <summary>The RACE RESULTS button of the spectator: only the host with AI cars still racing has to wait (the AI cars live in their game).</summary>
		internal static bool CanEnterResults()
		{
			// locked until every player has passed the finish (or is out); then only AI cars can still be racing and the player may end it
			return Room.Phase != Phase.Racing || Room.AllPlayersDone;
		}

		internal static void EnterResults()
		{
			if (!CanEnterResults())
			{
				return;
			}
			PushStats();
			if (Room.Phase == Phase.Racing)
			{
				// only AI cars are left: the race ends for everybody now
				if (Room.IsHost)
				{
					Room.ForceEnd();
				}
				else
				{
					Room.SendHost(new W(Msg.EndRace));
				}
			}
			Room.LocalEnterResults();
		}

		/// <summary>The finished car leaves the track: it cannot be seen or hit any more, and the other players see it wrecked away.</summary>
		private static void HideLocalCar()
		{
			CarControllerNew car = tm != null ? tm.playerCarController : null;
			if (car == null)
			{
				return;
			}
			foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true))
			{
				r.enabled = false;
			}
			foreach (Collider c in car.GetComponentsInChildren<Collider>(true))
			{
				c.enabled = false;
			}
			Rigidbody rb = car.rb != null ? car.rb : car.GetComponent<Rigidbody>();
			if (rb != null)
			{
				rb.linearVelocity = Vector3.zero;
				rb.angularVelocity = Vector3.zero;
				rb.isKinematic = true;
			}
			Room.SendAll(new W(Msg.Despawn));
		}

		/// <summary>Everything a spectator can watch: the other players and the AI cars, while they are on the track.</summary>
		internal static List<KeyValuePair<Transform, string>> WatchTargets()
		{
			List<KeyValuePair<Transform, string>> list = new List<KeyValuePair<Transform, string>>();
			foreach (KeyValuePair<ulong, RemoteCar> pair in humans)
			{
				RemoteCar rc = pair.Value;
				Peer peer = Room.Find(pair.Key);
				if (rc != null && rc.Controller != null && rc.Controller.alive && peer != null)
				{
					list.Add(new KeyValuePair<Transform, string>(rc.transform, peer.Name));
				}
			}
			for (int i = 0; i < aiReal.Count; i++)
			{
				EnemyController ec = aiReal[i];
				if (ec != null && ec.alive)
				{
					AiStat stat;
					list.Add(new KeyValuePair<Transform, string>(ec.transform, aiStats.TryGetValue(i, out stat) ? stat.Name : "AI"));
				}
			}
			for (int i = 0; i < aiProxies.Count; i++)
			{
				RemoteCar rc = aiProxies[i];
				if (rc != null && rc.Controller != null && rc.Controller.alive)
				{
					CarSkinController skin = rc.GetComponent<CarSkinController>();
					list.Add(new KeyValuePair<Transform, string>(rc.transform, skin != null && !string.IsNullOrEmpty(skin.carName) ? skin.carName : "AI"));
				}
			}
			return list;
		}

		internal static void PeerLeft(Peer peer)
		{
			RemoteCar rc = RemoteOf(peer.Id);
			if (rc != null)
			{
				UnityEngine.Object.Destroy(rc.gameObject);
			}
			humans.Remove(peer.Id);
		}

		/// <summary>The game's end of race for this player (replaces going back to the run).</summary>
		/// <summary>The local car crossed the finish line (the game''s own SUCCESS text is dropping if this player won): from here on
		/// the controls are let go of, nothing can hurt the car, and after a moment a drone takes over the view.</summary>
		internal static void OnCrossedLine()
		{
			if (finishedLocal || tm == null)
			{
				return;
			}
			finishedLocal = true;
			LapManager laps = tm.lapManager;
			float time = laps != null ? laps.GetPlayerTotalTime() : 0f;
			Mod.Log.LogInfo("Crossed the line in " + time.ToString("0.00") + "s");
			PushStats();
			Room.LocalFinished(time, true);
			if (tm.playerCarController != null)
			{
				CarLink.godMode = true;
				finishOwner = tm.playerCarController;
				GameInput.Mute(finishOwner, true);
				finishMuted = true;
			}
			if (!IsLocalWinner())
			{
				TextController.EnqueueRandomText(2f, "FINISHED");
			}
			tm.StartCoroutine(SpectateSoon());
		}

		private static IEnumerator SpectateSoon()
		{
			// long enough to see the text drop onto the road
			float until = Time.unscaledTime + 4f;
			while (Time.unscaledTime < until)
			{
				yield return null;
			}
			if (Room.Active && Room.Phase == Phase.Racing)
			{
				HideLocalCar();
				Spectator.Begin();
			}
		}

		internal static bool IsLocalWinner()
		{
			LapManager laps = tm != null ? tm.lapManager : null;
			return laps != null && laps.GetPlayerLeaderboardPosition() == 1;
		}

		/// <summary>The game''s end of race for this player (replaces going back to the run). A player who crossed the line was dealt with then.</summary>
		private static void LocalRaceEnded()
		{
			if (!finishedLocal)
			{
				LapManager laps = tm != null ? tm.lapManager : null;
				bool crossed = laps != null && laps.HasPlayerFinished();
				if (crossed)
				{
					OnCrossedLine();
				}
				else
				{
					finishedLocal = true;
					Mod.Log.LogInfo("Local race ended: did not finish");
					PushStats();
					Room.LocalFinished(0f, false);
					localOut = true;
				}
			}
			// keep driving until the host ends the race
			GameManager.raceOver = false;
		}
		internal static void OnResults()
		{
		}
	}

	// ---------------------------------------------------------------------- patches

	[HarmonyPatch(typeof(TrackManager), "SpawnEnemies")]
	internal static class RoomSpawn
	{
		private static bool Prefix(TrackManager __instance)
		{
			if (!Room.Active || Room.Race == null)
			{
				return true;
			}
			try
			{
				RaceSync.Spawn(__instance);
			}
			catch (Exception e)
			{
				Mod.Log.LogError("Building the race grid failed: " + e);
				Room.Leave("The race could not be set up (see the log).");
			}
			return false;
		}
	}

	// The countdown waits for the host's go, so everybody's lights start together.
	[HarmonyPatch(typeof(TrackManager), "RaceCountdown")]
	internal static class RoomCountdown
	{
		private static void Postfix(ref IEnumerator __result)
		{
			if (Room.Active && Room.Race != null)
			{
				__result = Wait(__result);
			}
		}

		private static IEnumerator Wait(IEnumerator original)
		{
			float until = Time.realtimeSinceStartup + 60f;
			while (!Room.GoReceived && Room.Active && Time.realtimeSinceStartup < until)
			{
				yield return null;
			}
			while (original.MoveNext())
			{
				yield return original.Current;
			}
		}
	}

	// A wrecked player is handled by the room (spectate or respawn), not by ending the race.
	[HarmonyPatch(typeof(TrackManager), "OnPlayerDestroyed")]
	internal static class RoomWreck
	{
		private static bool Prefix()
		{
			return !(Room.Active && Room.Race != null);
		}
	}

	// No damage reaches a player who is out of the race.
	[HarmonyPatch(typeof(CarLink), nameof(CarLink.TakeCrashDamage))]
	internal static class RoomDamage
	{
		private static bool Prefix(ref float damage, DamageKind kind)
		{
			if (!Room.Active || Room.Race == null)
			{
				return true;
			}
			if (RaceSync.LocalOut || RaceSync.FinishedLocal)
			{
				return false;
			}
			if (Combat.Applying)
			{
				return true;
			}
			RaceSettings s = Room.Race;
			if (kind == DamageKind.Crash && Combat.CrashWith != null && Combat.CrashWith.Human)
			{
				damage *= s.Ram;
			}
			else if (Combat.AiShot)
			{
				Combat.LastAiShooter = Combat.AiShooterNow;
				Combat.LastAiShotAt = Time.time;
				damage *= s.AiGun;
			}
			return damage > 0f;
		}
	}

	// Slow motion would slow every player's game; it does nothing in a room.
	[HarmonyPatch(typeof(TimeScale), nameof(TimeScale.Set))]
	internal static class NoSlowMotion
	{
		private static float lastWarn;

		private static bool Prefix(float factor)
		{
			if (!Room.Active || factor >= 1f)
			{
				return true;
			}
			if (Time.realtimeSinceStartup - lastWarn > 6f)
			{
				lastWarn = Time.realtimeSinceStartup;
				TextController.EnqueueRandomText(1.5f, "NO SLOW MOTION IN A ROOM");
			}
			return false;
		}
	}

	// The game reports every lap time of the local car; the best of them is part of the results.
	[HarmonyPatch(typeof(RaceStats), nameof(RaceStats.ReportLap))]
	internal static class RoomLapTime
	{
		private static void Prefix(float seconds)
		{
			if (Room.Active && Room.Race != null)
			{
				RaceSync.NoteLap(seconds);
			}
		}
	}

	// The game's own scoreboard: AI names stay white, human players are orange (the local player too, instead of the game's red).
	[HarmonyPatch(typeof(StatusInfo), "RowColour")]
	internal static class RoomRowColour
	{
		private static void Postfix(GameObject car, ref string __result)
		{
			if (Room.Active && car != null)
			{
				RemoteCar rc = car.GetComponent<RemoteCar>();
				if (rc != null && rc.Human)
				{
					__result = "FF8C00";
				}
			}
		}
	}

	[HarmonyPatch(typeof(StatusInfo), "BuildLeaderboardPanel")]
	internal static class RoomOwnRow
	{
		private static void Postfix(ref string __result)
		{
			if (Room.Active && Room.Race != null && __result != null)
			{
				__result = __result.Replace("<color=#FF0000>", "<color=#FF8C00>");
			}
		}
	}

	// The game calls this the moment the player's car completes the race.
	[HarmonyPatch(typeof(TrackManager), "OnPlayerHasFinished")]
	internal static class RoomCrossedLine
	{
		private static void Postfix(TrackManager __instance)
		{
			if (Room.Active && Room.Race != null && __instance.lapManager != null && __instance.lapManager.HasPlayerFinished())
			{
				RaceSync.OnCrossedLine();
			}
		}
	}
}