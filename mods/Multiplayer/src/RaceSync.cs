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
			Countdown.Remove();
			Cues.Remove();
			humans.Clear();
			aiRespawnAt.Clear();
			aiLastSpot.Clear();
			aiProxies.Clear();
			aiReal.Clear();
			aiReported.Clear();
			aiStats.Clear();
			Bots.Reset();
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

		private static readonly Dictionary<int, float> aiRespawnAt = new Dictionary<int, float>();
		private static readonly Dictionary<int, Vector3> aiLastSpot = new Dictionary<int, Vector3>();

		/// <summary>
		/// Host: with the RESPAWN rule on, a wrecked AI car comes back like a player does (after the same time), on the racing line where it was
		/// wrecked. It is a new car (the game destroys a wrecked one), so it starts its laps again; clients are told with <see cref="Msg.AiRespawned"/>.
		/// </summary>
		private static void RespawnAi()
		{
			if (Room.Race == null || Room.Race.Wreck != RaceSettings.WreckRespawn || Room.Race.Mode == RaceSettings.ModeDemolition || Room.Phase != Phase.Racing)
			{
				return;
			}
			float now = Time.unscaledTime;
			for (int i = 0; i < aiReal.Count; i++)
			{
				EnemyController ec = aiReal[i];
				AiStat stat;
				aiStats.TryGetValue(i, out stat);
				if (ec != null && ec.alive)
				{
					Rigidbody rb = ec.carController != null ? ec.carController.rb : null;
					aiLastSpot[i] = rb != null ? rb.position : ec.transform.position;
					aiRespawnAt.Remove(i);
					continue;
				}
				if (stat != null && stat.Finish > 0f)
				{
					continue; // it finished the race: it stays finished
				}
				float at;
				if (!aiRespawnAt.TryGetValue(i, out at))
				{
					aiRespawnAt[i] = now + RespawnTotal(Room.Race.RespawnSeconds);
					continue;
				}
				if (now >= at && aiLastSpot.ContainsKey(i))
				{
					aiRespawnAt.Remove(i);
					RespawnAiCar(i);
				}
			}
		}

		private static void RespawnAiCar(int index)
		{
			PathManager line = tm.lapManager != null ? tm.lapManager.trackLine : null;
			if (line == null || line.sampledPoints.Count < 4)
			{
				return;
			}
			List<Vector3> pts = line.sampledPoints;
			int at = line.GetClosestPointIndex(aiLastSpot[index]);
			Vector3 ahead = pts[(at + 1) % pts.Count] - pts[at];
			ahead.y = 0f;
			Quaternion rotation = ahead.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(ahead.normalized, Vector3.up) : Quaternion.identity;
			Vector3 position = pts[at] + Vector3.up * 0.8f;
			List<EnemyController> enemies = Traverse.Create(tm).Field("enemies").GetValue<List<EnemyController>>();
			EnemyController fresh = SpawnAi(tm, enemies, position, rotation, index);
			aiReal[index] = fresh;
			aiReported.Remove(index);
			AiStat stat;
			if (aiStats.TryGetValue(index, out stat))
			{
				stat.Dead = false;
				stat.Counted = false;
				stat.Lap = 0;
				stat.LastTotal = 0f;
			}
			Mod.Log.LogInfo("Bot " + index + " respawned on the racing line.");
			Room.SendAll(new W(Msg.AiRespawned).U8(index).Vec(position).Rot(rotation));
		}

		/// <summary>Client: the host's AI car came back; its old stand-in was wrecked and is on its way out.</summary>
		internal static void OnAiRespawned(R r)
		{
			int index = r.U8();
			Vector3 p = r.Vec();
			Quaternion q = r.Rot();
			if (tm == null || index < 0 || index >= aiProxies.Count)
			{
				return;
			}
			RemoteCar old = aiProxies[index];
			if (old != null)
			{
				UnityEngine.Object.Destroy(old.gameObject);
			}
			List<EnemyController> enemies = Traverse.Create(tm).Field("enemies").GetValue<List<EnemyController>>();
			RemoteCar fresh = SpawnProxy(tm, enemies, p, q, Bots.AiName(index), 0, true, index);
			fresh.OnState(p, q, Vector3.zero, 0f, 1f);
			aiProxies[index] = fresh;
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
			Ui.ReleaseInputs(); // a lobby text box that was still focused when the menu went must not keep the car muted
			tm = manager;
			Traverse t = Traverse.Create(manager);
			List<Transform> starts = t.Field("startPositions").GetValue<List<Transform>>();
			Transform playerStart = t.Field("playerStart").GetValue<Transform>();
			float gridHeight = t.Field("normalGridHeight").GetValue<float>();
			List<Transform> slots = new List<Transform> { playerStart };
			slots.AddRange(starts.Take(5));
			// a track with fewer grid spots than the room has cars gets extra spots behind the start, along the racing line
			int needed = 6;
			foreach (Peer p in Room.Peers)
			{
				needed = Math.Max(needed, p.Grid == Room.SpectatorGrid ? 0 : p.Grid + 1);
			}
			foreach (int g in Room.AiGrid)
			{
				needed = Math.Max(needed, g + 1);
			}
			Transform firstSpot = slots[0];
			Quaternion heading = Heading();
			while (slots.Count < needed)
			{
				slots.Add(ExtraSpot(manager, firstSpot, slots.Count - 6, heading));
			}
			List<EnemyController> enemies = t.Field("enemies").GetValue<List<EnemyController>>();
			RaceSettings race = Room.Race;
			Mod.Log.LogInfo("Race scene: " + slots.Count + " grid slots, " + Room.Peers.Count + " player(s), " + Room.AiGrid.Count + " AI");

			// a mode from another mod may place the cars itself (the same answer on every machine)
			HalfGripTools.NetMode def = Room.Race != null ? Room.Race.ModDef : null;
			bool customSpots = false;
			if (def != null && def.SpawnPoint != null)
			{
				for (int g = 0; g < needed; g++)
				{
					try
					{
						Pose? pose = def.SpawnPoint(g);
						if (pose.HasValue)
						{
							GameObject spot = new GameObject("MpModeSpot" + g);
							spot.transform.SetPositionAndRotation(pose.Value.position, pose.Value.rotation);
							while (slots.Count <= g)
							{
								slots.Add(spot.transform);
							}
							slots[g] = spot.transform;
							customSpots = true;
						}
					}
					catch (Exception e)
					{
						Mod.Log.LogWarning("The mode's SpawnPoint(" + g + ") failed: " + e.Message);
					}
				}
			}
			// slots past the track's own six face along the racing line; the stock six use the game's heading
			Func<int, Quaternion> facing = g => customSpots || g >= 6 ? slots[g].rotation : heading;
			bool localSpectator = false;
			foreach (Peer peer in Room.Peers)
			{
				if (peer.Grid == Room.SpectatorGrid)
				{
					localSpectator |= peer.Local; // more players than the mode takes: this one watches
					continue;
				}
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
						MovePlayer(manager, at, facing(peer.Grid));
					}
				}
				else
				{
					humans[peer.Id] = SpawnProxy(manager, enemies, at, facing(peer.Grid), peer.Name, peer.Id, false, -1);
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
					aiReal.Add(SpawnAi(manager, enemies, at, facing(g), i));
				}
				else
				{
					aiProxies.Add(SpawnProxy(manager, enemies, at, facing(g), Bots.AiName(i), 0, true, i));
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
			if (localSpectator)
			{
				// the room has more players than this mode takes: no car for this one, the drone follows the others from the start
				Mod.Log.LogInfo("This player is a spectator (the mode takes " + race.Cap + " players).");
				HideLocalCar();
				CarLink.godMode = true;
				Spectator.Begin();
			}
		}

		/// <summary>
		/// Extra grid spot number <paramref name="k"/> (0 = the first beyond the track's own six): two cars to a row, rows 9 m apart, walking back
		/// from the start along the racing line, so the queue follows the road instead of a straight line that may leave it.
		/// </summary>
		private static Transform ExtraSpot(TrackManager manager, Transform start, int k, Quaternion heading)
		{
			GameObject spot = new GameObject("MpExtraGridSpot" + (k + 6));
			PathManager line = manager.lapManager != null ? manager.lapManager.trackLine : null;
			List<Vector3> pts = line != null ? line.sampledPoints : null;
			Vector3 position;
			Quaternion rotation = heading;
			if (pts == null || pts.Count < 8)
			{
				// no racing line to follow: stand behind the first spot (the stock cars face the way of the heading)
				Vector3 forward = heading * Vector3.forward;
				Vector3 side = Vector3.Cross(Vector3.up, forward);
				position = start.position - forward * (9f * (1 + k / 2)) + side * ((k % 2 == 0 ? -1f : 1f) * 2.6f);
			}
			else
			{
				int at = line.GetClosestPointIndex(start.position);
				float behind = 9f * (2 + k / 2);
				float walked = 0f;
				int index = at;
				while (walked < behind && walked < 5000f)
				{
					int before = (index - 1 + pts.Count) % pts.Count;
					walked += Vector3.Distance(pts[index], pts[before]);
					index = before;
					if (index == at)
					{
						break;
					}
				}
				Vector3 point = pts[index];
				Vector3 ahead = pts[(index + 1) % pts.Count] - point;
				ahead.y = 0f;
				if (ahead.sqrMagnitude < 0.0001f)
				{
					ahead = heading * Vector3.forward;
				}
				ahead.Normalize();
				rotation = Quaternion.LookRotation(ahead, Vector3.up);
				Vector3 side = Vector3.Cross(Vector3.up, ahead);
				position = point + side * ((k % 2 == 0 ? -1f : 1f) * 2.6f);
				position.y = point.y + (start.position.y - pts[at].y);
			}
			spot.transform.SetPositionAndRotation(position, rotation);
			return spot.transform;
		}

		private static Vector3 GridPoint(Transform slot, float gridHeight)
		{
			float y = TrackVariants.Active ? slot.position.y + (1f - gridHeight) : 1f;
			return new Vector3(slot.position.x, y, slot.position.z);
		}

		private static void MovePlayer(TrackManager manager, Vector3 at, Quaternion heading, bool raiseOnly = true)
		{
			CarControllerNew player = manager.playerCarController;
			if (player == null)
			{
				return;
			}
			if (raiseOnly)
			{
				at.y = Mathf.Max(at.y, player.transform.position.y);
			}
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
			Bots.Plan plan = Room.Race != null ? Bots.Roll(Room.Race, index) : null;
			EnemyGun prefabGun = manager.enemyCarPrefab != null ? manager.enemyCarPrefab.GetComponentInChildren<EnemyGun>(true) : null;
			float stockNoGun = prefabGun != null ? prefabGun.noGunChance : 0f;
			if (plan != null && prefabGun != null && plan.NoGun >= 0f)
			{
				prefabGun.noGunChance = plan.NoGun; // the gun rolls this in its Awake, which runs as the car is made
			}
			EnemyController ai;
			try
			{
				ai = MakeCar(manager, enemies, at, heading, Bots.AiName(index), "Enemy_ai" + index);
			}
			finally
			{
				if (prefabGun != null)
				{
					prefabGun.noGunChance = stockNoGun;
				}
			}
			if (plan != null)
			{
				Bots.Apply(ai, plan, index);
			}
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
					RespawnAi();
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
			for (int i = 0; i < count && i < Room.Ceiling; i++)
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
				NoteWreckSpot();
				Peer me = Room.Me;
				ulong killer = me != null && me.LastAttacker != 0 && Time.time - me.LastAttackTime < 8f ? me.LastAttacker : 0UL;
				bool out_ = Room.Race.Mode == RaceSettings.ModeDemolition || Room.Race.Wreck == RaceSettings.WreckSpectate;
				if (me != null)
				{
					me.Time = tm.TimeSinceRaceStart;
				}
				// the player sees the wreck first: still in the burning car with the red text on screen, and only then the drone or the countdown
				ShowWrecked();
				int aiKiller = Combat.LastAiShooter >= 0 && Time.time - Combat.LastAiShotAt < 8f && killer == 0UL ? Combat.LastAiShooter : 255;
				Room.LocalWrecked(killer, aiKiller);
				if (out_)
				{
					localOut = true;
					tm.StartCoroutine(SpectateAfterWreck());
				}
				else
				{
					respawnAt = Time.unscaledTime + RespawnTotal(Room.Race.RespawnSeconds);
					Spectator.Stop();
					tm.StartCoroutine(RespawnLater());
				}
			}
		}

		/// <summary>Seconds a wrecked player stays in the car, burning and with the WRECKED text, before the drone (or the respawn) takes over.</summary>
		internal const float WreckHold = 3.5f;

		private static IEnumerator SpectateAfterWreck()
		{
			float until = Time.unscaledTime + WreckHold;
			while (Time.unscaledTime < until)
			{
				yield return null;
			}
			// no drone if the race ended while the player was still looking at the wreck (the results come next)
			if (localOut && Room.Active && Room.Race != null && Room.Phase == Phase.Racing && tm != null && !Spectator.Active)
			{
				Spectator.Begin();
			}
		}

		/// <summary>
		/// From the wreck to the respawn: the WRECKED text and the burning car for <see cref="WreckHold"/>, and after that the host's RESPAWN IN
		/// setting as a countdown of whole seconds (at least 1).
		/// </summary>
		internal static float RespawnTotal(float setting)
		{
			return WreckHold + Mathf.Max(1, Mathf.CeilToInt(setting));
		}

		/// <summary>The game's own failure red (the colour of FAILED at the end of a single-player wreck) on the end-of-race combo line; the plain TextController line if that widget is not in the scene.</summary>
		private static void ShowWrecked()
		{
			if (UnityEngine.Object.FindFirstObjectByType<EndRaceScreenCombo>() != null)
			{
				EndRaceScreenCombo.UpdateComboInstant("<color=#" + RaceOutcomeText.Hex(false) + ">" + Loc.T("WRECKED") + "</color>", ComboType.NoCount, 1f);
			}
			else
			{
				TextController.EnqueueRandomText(WreckHold - 0.5f, "WRECKED", "TOTALLED", "SCRAPPED");
			}
		}

		private static Vector3 wreckSpot;
		private static float wreckYaw;
		private static bool hasWreckSpot;
		private static bool allowReset;

		/// <summary>The local player is wrecked (from the wreck until the respawn, or for the rest of the race when out).</summary>
		internal static bool Wrecked { get { return Room.Active && Room.Race != null && wreckReported; } }

		/// <summary>No reset of the car while wrecked: not by R, not by the pause menu, not by the game's own fell-off-the-map reset. The mod's own respawn is the one exception.</summary>
		internal static bool ResetBlocked { get { return Wrecked && !allowReset; } }

		/// <summary>Where the car was when its HP ran out: the respawn puts it back there, not on the road where it has rolled to.</summary>
		private static void NoteWreckSpot()
		{
			CarControllerNew car = tm != null ? tm.playerCarController : null;
			Rigidbody body = car == null ? null : (car.rb != null ? car.rb : car.GetComponent<Rigidbody>());
			hasWreckSpot = body != null;
			if (hasWreckSpot)
			{
				wreckSpot = body.position;
				wreckYaw = body.rotation.eulerAngles.y;
			}
		}

		/// <summary>
		/// The respawn is on the track at the place where the damage happened: the car is first moved to the recorded spot and then the game's own
		/// reset onto the track runs from there. It takes the nearest clear road point (so a crash into a wall does not put the car in the wall),
		/// faces along the track, stands still, and steps back along the road if another car is in the way.
		/// </summary>
		private static void PlaceAtWreck()
		{
			CarControllerNew cc = tm.playerCarController;
			if (hasWreckSpot && cc != null)
			{
				MovePlayer(tm, wreckSpot, Quaternion.Euler(0f, wreckYaw, 0f), false);
			}
			allowReset = true;
			try
			{
				tm.ResetPlayerOnTrack();
			}
			finally
			{
				allowReset = false;
			}
		}

		private static IEnumerator RespawnLater()
		{
			// the WRECKED text and the car burning first
			float countdownFrom = Time.unscaledTime + WreckHold;
			while (Time.unscaledTime < countdownFrom)
			{
				yield return null;
			}
			while (Time.unscaledTime < respawnAt)
			{
				Countdown.Show(Mathf.CeilToInt(respawnAt - Time.unscaledTime));
				yield return null;
			}
			Countdown.Hide();
			CarLink link = tm.playerCarlink;
			if (link == null)
			{
				yield break;
			}
			// items can lower the maximum to nothing: a respawned car always has at least 1 HP
			link.hp = link.MaxHp >= 1f ? link.MaxHp : 1f;
			PlaceAtWreck();
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

	// A wrecked player cannot reset the car: holding R (and its "Hold R to reset" hint, and the automatic reset after falling off the map) is the game's PlayerResetLogic.Tick.
	[HarmonyPatch(typeof(PlayerResetLogic), nameof(PlayerResetLogic.Tick))]
	internal static class NoResetKeyWhenWrecked
	{
		private static bool Prefix() => !RaceSync.ResetBlocked;
	}

	[HarmonyPatch(typeof(PlayerResetLogic), nameof(PlayerResetLogic.ResetOnTrack))]
	internal static class NoResetWhenWrecked
	{
		private static bool Prefix() => !RaceSync.ResetBlocked;
	}

	// ... nor from the pause menu's button (which is also hidden while wrecked, see Pause.ShowResetButton)
	[HarmonyPatch(typeof(PauseController), nameof(PauseController.ResetCar))]
	internal static class NoPauseResetWhenWrecked
	{
		private static bool Prefix(PauseController __instance)
		{
			if (!RaceSync.Wrecked)
			{
				return true;
			}
			__instance.Resume();
			return false;
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