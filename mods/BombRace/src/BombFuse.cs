using System.Collections.Generic;
using UnityEngine;

namespace HalfGripBombRace
{
	/// <summary>
	/// The bombs of one race, on the TrackManager. In a Bomb race every car (yours, and the rivals' if <c>RivalBombs</c> is on) gets a
	/// C4 on its driver-side door, and yours also a bomb on the dash. After the grace period each car's fuse burns while it is under its
	/// limit (a share of its engine's top speed, in the dashboard's units) and burns down while it is over; a burnt-through fuse wrecks
	/// the car. Each time the leader starts a new lap, the car in last place is blown up too.
	/// </summary>
	internal class BombFuse : MonoBehaviour
	{
		private class Rival
		{
			public FuseState state = new FuseState();

			public C4Visual c4;

			public bool warned;
		}

		internal TrackManager track;

		private readonly FuseState player = new FuseState();

		private readonly Dictionary<EnemyController, Rival> rivals = new Dictionary<EnemyController, Rival>();

		private DashBomb dash;

		private C4Visual playerC4;

		private TextController callout;

		private bool built;

		private bool armedSaid;

		private int side = -1;

		private int bestLap = -1;

		private AudioSource speaker;

		private AudioClip beep;

		private void Update()
		{
			if (!RunState.Active || !Bomb.IsBomb(RunState.PendingObjective) || track == null || track.playerCarlink == null || track.lapManager == null)
			{
				return;
			}
			if (!built)
			{
				Build();
			}

			CarLink car = track.playerCarlink;
			bool over = RaceTracker.Ended || GameManager.raceOver;
			bool grace = track.TimeSinceRaceStart < Mod.GraceSeconds.Value;
			bool armedNow = track.raceStarted && !grace && !over;

			StepPlayer(car, armedNow);
			if (Mod.RivalBombs.Value)
			{
				StepRivals(armedNow);
			}
			CheckLap(car, armedNow);
		}

		private void Build()
		{
			built = true;
			CarLink car = track.playerCarlink;
			dash = DashBomb.Make(car);

			// The driver's side: the side of the car the steering wheel is on (checked against the car, since the cockpit may not sit in it).
			side = Mod.DriverSide.Value;
			if (side == 0)
			{
				side = -1;
				Transform wheel = car.sw != null && car.sw.wheel != null ? car.sw.wheel.transform : null;
				if (wheel != null && car.cc != null)
				{
					Vector3 local = car.cc.transform.InverseTransformPoint(wheel.position);
					if (Mathf.Abs(local.x) > 0.05f && Mathf.Abs(local.x) < 4f && Mathf.Abs(local.z) < 10f)
					{
						side = local.x > 0f ? 1 : -1;
					}
				}
			}

			// The cabin is not part of the car's body, so it is left out of where the C4 goes.
			var cabin = new HashSet<Transform>();
			CameraSwitch cameras = FindFirstObjectByType<CameraSwitch>();
			if (cameras != null && cameras.interiorModels != null)
			{
				foreach (Transform t in cameras.interiorModels)
				{
					if (t != null)
					{
						cabin.Add(t);
					}
				}
			}
			playerC4 = C4Visual.Attach(car.cc, cabin, side);
			if (Mod.DumpInfo.Value)
			{
				Mod.Log.LogInfo($"Bomb race set up: dash {dash != null}, C4 {playerC4 != null}, door side {side}, rivals {track.Enemies.Count}, laps {track.lapManager.maxLaps}, player top speed {car.speedMultiplier:0.0} -> limit {Bomb.Limit(car.speedMultiplier, Mod.SpeedShare.Value):0}");
			}
		}

		private void StepPlayer(CarLink car, bool armedNow)
		{
			bool alive = car.hp > 0f && !track.lapManager.HasPlayerFinished();
			bool armed = armedNow && alive;
			float speed = car.cc != null ? car.cc.forwardSpeed * SpeedDashboard.SpeedToDisplay : 0f;
			// The limit follows the engine fitted (and any top speed bonus): the same stat the dial is a share of.
			float limit = Bomb.Limit(car.speedMultiplier, Mod.SpeedShare.Value);
			float fuseTime = Mod.FuseSeconds.Value;

			if (armed && !armedSaid)
			{
				armedSaid = true;
				Say("BOMB ARMED", 1.5f);
				Say("STAY OVER " + Mathf.CeilToInt(limit), 2f);
				if (Mod.EliminateLast.Value)
				{
					Say("LAST PLACE EXPLODES EACH LAP", 2.5f);
				}
			}

			bool burnt = player.Step(Time.deltaTime, speed, limit, fuseTime, armed);
			if (!armed && (RaceTracker.Ended || GameManager.raceOver || car.hp <= 0f))
			{
				player.Off();
			}
			if (player.Ticked)
			{
				Beep();
			}
			if (dash != null)
			{
				dash.SetLights(player.Red, player.Green);
				dash.Show(Display(armed, car), player.Under ? Mathf.Max(0f, fuseTime - player.Fuse) : speed);
			}
			if (playerC4 != null)
			{
				playerC4.SetLights(player.Red, player.Green);
			}

			if (burnt)
			{
				Mod.Log.LogInfo($"Bomb went off at {speed:0} (limit {limit:0})");
				player.Reset();
				Detonate(car);
			}
		}

		// What the dash bomb's screen shows: bombs until it is armed, then ARMED, a red countdown while the fuse burns, your speed in
		// green while it burns back down, and ARMED again once it is out.
		private BombDisplay Display(bool armed, CarLink car)
		{
			if (!armed)
			{
				bool ended = RaceTracker.Ended || GameManager.raceOver || car.hp <= 0f || track.lapManager.HasPlayerFinished();
				return ended ? BombDisplay.Off : BombDisplay.Icons;
			}
			if (player.Fuse > 0f)
			{
				return player.Under ? BombDisplay.Countdown : BombDisplay.Recover;
			}
			return BombDisplay.Armed;
		}

		private void StepRivals(bool armedNow)
		{
			var leaderboard = track.lapManager.leaderboard;
			foreach (EnemyController enemy in track.Enemies)
			{
				if (enemy == null || enemy.carController == null)
				{
					continue;
				}
				if (!rivals.TryGetValue(enemy, out Rival rival))
				{
					rival = new Rival { c4 = C4Visual.Attach(enemy.carController, null, side) };
					rivals[enemy] = rival;
				}

				bool finished = leaderboard != null && leaderboard.TryGetValue(enemy.gameObject, out LapManager.LeaderboardEntry entry) && entry.isFinished;
				bool armed = armedNow && enemy.alive && !finished;
				float speed = enemy.carController.forwardSpeed * SpeedDashboard.SpeedToDisplay;
				// Each rival's own engine, a lower share than yours, and a longer fuse: they slow for corners.
				float top = enemy.carStats != null ? enemy.carStats.speedMultiplier : 100f;
				float limit = Bomb.Limit(top, Mod.RivalSpeedShare.Value);
				float fuseTime = Mod.RivalFuseSeconds.Value;

				bool burnt = rival.state.Step(Time.deltaTime, speed, limit, fuseTime, armed);
				if (!enemy.alive)
				{
					rival.state.Off();
				}
				if (rival.c4 != null)
				{
					rival.c4.SetLights(rival.state.Red, rival.state.Green);
				}
				if (Mod.DumpInfo.Value && !rival.warned && rival.state.Fuse > fuseTime * 0.7f)
				{
					rival.warned = true;
					Mod.Log.LogInfo($"{Name(enemy)} is {rival.state.Fuse:0.0}s into its fuse at {speed:0} (limit {limit:0}, top speed {top:0})");
				}
				if (burnt)
				{
					Mod.Log.LogInfo($"{Name(enemy)}'s bomb went off at {speed:0} (limit {limit:0})");
					rival.state.Reset();
					Detonate(enemy);
				}
			}
		}

		// Each time the leader starts another lap (not the last: that is the finish), the car in last place is blown up.
		private void CheckLap(CarLink car, bool armedNow)
		{
			var board = track.lapManager.leaderboard;
			GameObject playerCar = track.lapManager.PlayerCar;
			if (board == null)
			{
				return;
			}
			int best = -1;
			int cars = 0;
			int lastPosition = -1;
			EnemyController lastRival = null;
			bool playerLast = false;

			void Consider(LapManager.LeaderboardEntry entry, EnemyController rival)
			{
				cars++;
				best = Mathf.Max(best, entry.currentLap);
				if (entry.position > lastPosition)
				{
					lastPosition = entry.position;
					lastRival = rival;
					playerLast = rival == null;
				}
			}

			if (playerCar != null && car.hp > 0f && board.TryGetValue(playerCar, out LapManager.LeaderboardEntry mine) && !mine.isFinished)
			{
				Consider(mine, null);
			}
			foreach (EnemyController enemy in track.Enemies)
			{
				if (enemy != null && enemy.alive && board.TryGetValue(enemy.gameObject, out LapManager.LeaderboardEntry theirs) && !theirs.isFinished)
				{
					Consider(theirs, enemy);
				}
			}

			if (best <= bestLap)
			{
				return;
			}
			bestLap = best;
			if (!armedNow || !Mod.EliminateLast.Value || best < 1 || best >= track.lapManager.maxLaps || cars < 2)
			{
				return;
			}
			if (playerLast)
			{
				Mod.Log.LogInfo($"Last place at the start of lap {best + 1}: you");
				Detonate(car);
			}
			else if (lastRival != null)
			{
				Mod.Log.LogInfo($"Last place at the start of lap {best + 1}: {Name(lastRival)}");
				Say(Name(lastRival).ToUpperInvariant() + " BLOWN UP", 2f);
				Detonate(lastRival);
			}
		}

		// More damage than the car has; items that save a car from a wreck still get their say in TakeCrashDamage.
		private static void Detonate(CarLink car)
		{
			car.TakeCrashDamage(car.hp + car.MaxHp);
		}

		// Not by the player: it is not one of your kills.
		private static void Detonate(EnemyController enemy)
		{
			enemy.TakeDamage(1000000f, false);
		}

		private static string Name(EnemyController enemy)
		{
			return enemy.skinController != null && !string.IsNullOrEmpty(enemy.skinController.carName) ? enemy.skinController.carName : enemy.name;
		}

		private void Say(string line, float seconds)
		{
			if (callout == null)
			{
				callout = FindAnyObjectByType<TextController>();
			}
			if (callout != null)
			{
				callout.EnqueueText(line, seconds);
			}
		}

		private void Beep()
		{
			if (beep == null)
			{
				const int rate = 22050;
				int count = rate / 14;
				var data = new float[count];
				for (int i = 0; i < count; i++)
				{
					float t = i / (float)rate;
					data[i] = Mathf.Sin(2f * Mathf.PI * 1760f * t) * (1f - i / (float)count) * 0.35f;
				}
				beep = AudioClip.Create("BombBeep", count, 1, rate, false);
				beep.SetData(data, 0);
				beep.hideFlags = HideFlags.HideAndDontSave;
				speaker = gameObject.AddComponent<AudioSource>();
				speaker.spatialBlend = 0f;
			}
			speaker.PlayOneShot(beep);
		}
	}
}
