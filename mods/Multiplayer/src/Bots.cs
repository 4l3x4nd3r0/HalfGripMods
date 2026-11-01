using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// What the host chose for the AI rivals: how aggressively they drive and shoot, how they are kitted, and whether they are all alike.
	/// The game has no per-race bot difficulty (its Hardcore flag is a saved player choice, and the bots' strength is upgrade points a single-player run
	/// hands out), so every bot gets its own <see cref="EnemyStatGenerator"/> and <see cref="DriverBehaviour"/> here, handed to the game when the bot
	/// asks for them (<see cref="BotStatsPatch"/>, <see cref="BotBehaviourPatch"/>). Nothing is written to the game's per-name tables or to the save, so
	/// the bots no longer carry over what the last single-player run gave them.
	/// </summary>
	internal static class Bots
	{
		internal const int AggressionSteps = 5;
		internal const int KitCount = 7;
		internal const int KitStarter = 0, KitHalf = 1, KitFull = 2, KitSpeed = 3, KitCombat = 4, KitLikeARun = 5, KitRandom = 6;
		internal const int SpreadSame = 0, SpreadMixed = 1, SpreadRandom = 2;

		private static readonly string[] AggressionNames = { "NORMAL", "+25%", "HARDCORE", "+75%", "ULTRA" };
		private static readonly string[] KitNames = { "STARTER", "HALF KITTED", "FULLY KITTED", "SPEED DEMONS", "COMBAT", "LIKE A RUN", "RANDOM" };
		private static readonly string[] StageNames = { "EARLY", "MIDDLE", "LATE" };
		private static readonly string[] SpreadNames = { "SAME", "MIXED", "RANDOM" };

		/// <summary>The upgrade points a bot has at an early, middle and late race of a single-player run (a rough reading of what the game hands out per column and race).</summary>
		private static readonly int[] StagePoints = { 8, 22, 36 };

		/// <summary>Half of the 44 points a bot can hold: grip, speed, acceleration and brakes 6 each, drag and downforce 5, health 4, weather 2, nitro 4.</summary>
		private const int MaxPoints = 44;

		private struct Step
		{
			internal float Share;   // how many bots drive aggressively
			internal float Pace;    // corner speed multiplier
			internal float Pause;   // seconds between their shooting bursts
			internal int Burst;     // shots in a burst
		}

		private static readonly Step[] Steps =
		{
			new Step { Share = 0.25f, Pace = 1.00f, Pause = 10f, Burst = 2 },
			new Step { Share = 0.45f, Pace = 1.02f, Pause = 8.5f, Burst = 2 },
			new Step { Share = 0.60f, Pace = 1.05f, Pause = 7f, Burst = 3 },
			new Step { Share = 0.80f, Pace = 1.07f, Pause = 6f, Burst = 3 },
			new Step { Share = 1.00f, Pace = 1.10f, Pause = 5f, Burst = 4 }
		};

		internal static string AggressionName(int i) { return AggressionNames[RaceSettings.Clamp(i, 0, AggressionSteps - 1)]; }
		internal static string KitName(int i) { return KitNames[RaceSettings.Clamp(i, 0, KitCount - 1)]; }
		internal static string StageName(int i) { return StageNames[RaceSettings.Clamp(i, 0, 2)]; }
		internal static string SpreadName(int i) { return SpreadNames[RaceSettings.Clamp(i, 0, 2)]; }

		/// <summary>What one bot gets.</summary>
		internal sealed class Plan
		{
			internal EnemyStatGenerator Stats;
			internal DriverBehaviour Behaviour;
			internal float Pace;
			internal float Pause;
			internal int Burst;
			/// <summary>The chance a bot has no gun, or -1 to leave the game's own.</summary>
			internal float NoGun = -1f;
			/// <summary>Rounds a bot's gun holds, or 0 to leave the game's own.</summary>
			internal int Ammo;
			internal int Aggression;
			internal int Kit;
			internal int Points;
		}

		private static readonly Dictionary<int, Plan> plans = new Dictionary<int, Plan>();

		/// <summary>Every bot's plan (the test rig reads them).</summary>
		internal static IEnumerable<Plan> All { get { return plans.Values; } }

		internal static void Reset()
		{
			plans.Clear();
			names = null;
		}

		private static List<string> names;
		private static int namesSeed;

		/// <summary>
		/// The name of AI car number <paramref name="index"/>. The game draws bot names from a roster that a run limits, and once it is used up
		/// every further bot is called "John": 14 bots on a CUSTOM grid were all John. Here the names come from the game's whole list
		/// (<c>CarSkinController.enemyNames</c>), shuffled by the room's seed, skipping the players' own names, so the same index gets the same
		/// name on every machine; past the end of the list the names repeat with a number.
		/// </summary>
		internal static string AiName(int index)
		{
			if (names == null || namesSeed != Room.Seed)
			{
				namesSeed = Room.Seed;
				HashSet<string> taken = new HashSet<string>(Room.Peers.Select(p => (p.Name ?? "").ToLowerInvariant()));
				List<string> pool = CarSkinController.enemyNames.Where(n => !string.IsNullOrWhiteSpace(n) && !taken.Contains(n.ToLowerInvariant())).Distinct().ToList();
				System.Random rng = new System.Random(unchecked(Room.Seed * 2654435 + 12345));
				for (int i = pool.Count - 1; i > 0; i--)
				{
					int j = rng.Next(i + 1);
					string swap = pool[i];
					pool[i] = pool[j];
					pool[j] = swap;
				}
				names = pool;
			}
			if (names.Count == 0)
			{
				return "Bot " + (index + 1);
			}
			string name = names[index % names.Count];
			int round = index / names.Count;
			return round == 0 ? name : name + " " + (round + 1);
		}

		internal static void Attach(GameObject car, Plan plan)
		{
			plans[car.GetInstanceID()] = plan;
		}

		internal static Plan For(Component part)
		{
			Plan plan;
			return plans.Count > 0 && part != null && plans.TryGetValue(part.gameObject.GetInstanceID(), out plan) ? plan : null;
		}

		/// <summary>Rolls the plan for bot number <paramref name="index"/> from the host's settings.</summary>
		internal static Plan Roll(RaceSettings s, int index)
		{
			System.Random rng = new System.Random(unchecked(Room.Seed * 31 + index * 7919 + 17));
			int aggression = s.Aggression;
			int kit = s.Kit;
			if (s.Spread == SpreadRandom)
			{
				aggression = rng.Next(0, AggressionSteps);
				kit = rng.Next(0, KitRandom);
			}
			else if (s.Spread == SpreadMixed)
			{
				aggression = RaceSettings.Clamp(aggression + rng.Next(-s.SpreadSteps, s.SpreadSteps + 1), 0, AggressionSteps - 1);
				if (kit <= KitFull)
				{
					kit = RaceSettings.Clamp(kit + rng.Next(-s.SpreadSteps, s.SpreadSteps + 1), KitStarter, KitFull); // starter, half and full kitted are a ladder; the others stay what they are
				}
			}
			if (kit == KitRandom)
			{
				kit = rng.Next(0, KitRandom);
			}
			Step step = Steps[aggression];
			Plan plan = new Plan { Aggression = aggression, Kit = kit, Pace = step.Pace, Pause = step.Pause, Burst = step.Burst };
			plan.Behaviour = DriverBehaviour.CreateRandom();
			plan.Behaviour.isAggressive = UnityEngine.Random.value < step.Share;
			plan.Behaviour.eTurbat = RollTurbat(aggression);
			plan.Stats = Kit(plan, kit, StagePoints[RaceSettings.Clamp(s.Stage, 0, 2)]);
			return plan;
		}

		/// <summary>How tight a bot takes its line (1 is the fastest racing line): the game's normal roll at NORMAL, its hardcore roll at HARDCORE, and always tight at ULTRA.</summary>
		private static float RollTurbat(int aggression)
		{
			float v = UnityEngine.Random.value;
			switch (aggression)
			{
				case 0: return UnityEngine.Random.value < 0.4f ? (UnityEngine.Random.value < 0.5f ? 0f : 1f) : v;
				case 1: return UnityEngine.Random.value < 0.2f ? 1f : Mathf.Lerp(0.25f, 1f, v);
				case 2: return UnityEngine.Random.value < 0.3f ? 1f : 1f - v * v;
				case 3: return UnityEngine.Random.value < 0.5f ? 1f : 1f - v * v * v;
				default: return 1f;
			}
		}

		private static EnemyStatGenerator Kit(Plan plan, int kit, int runPoints)
		{
			EnemyStatGenerator g = new EnemyStatGenerator();
			switch (kit)
			{
				case KitHalf:
					g.UpgradeRandomLevels(MaxPoints / 2);
					plan.Points = MaxPoints / 2;
					break;
				case KitFull:
					g.UpgradeRandomLevels(MaxPoints + 10);
					plan.Points = MaxPoints;
					break;
				case KitLikeARun:
					g.UpgradeRandomLevels(runPoints);
					plan.Points = runPoints;
					break;
				case KitSpeed:
					// fast and light: speed, acceleration, grip and downforce full, little drag; ordinary brakes and health; fewer guns
					g.speedMultiplier.level = 6;
					g.engineAccel.level = 6;
					g.tireGrip.level = 6;
					g.downForce.level = 5;
					g.airDrag.level = 5;
					g.brakeDeccel.level = 3;
					g.UpgradeModule(EnemyModuleKind.Nitro, 3);
					plan.Points = 6 + 6 + 6 + 5 + 5 + 3 + 3;
					plan.NoGun = 0.6f;
					break;
				case KitCombat:
					// tough and armed: full health and nitro, middling speed, every bot has a gun with more rounds
					g.health.level = 4;
					g.UpgradeModule(EnemyModuleKind.Nitro, 4);
					g.tireGrip.level = 3;
					g.speedMultiplier.level = 3;
					g.engineAccel.level = 3;
					g.brakeDeccel.level = 3;
					g.airDrag.level = 2;
					g.downForce.level = 2;
					plan.Points = 4 + 4 + 3 + 3 + 3 + 3 + 2 + 2;
					plan.NoGun = 0f;
					plan.Ammo = 12;
					break;
				default:
					plan.Points = 0;
					break;
			}
			return g;
		}

		/// <summary>Called when the bot's car is made, before the game starts it.</summary>
		internal static void Apply(EnemyController ec, Plan plan, int index)
		{
			Attach(ec.gameObject, plan);
			ec.paceMultiplier = plan.Pace;
			EnemyGun gun = ec.GetComponentInChildren<EnemyGun>(true);
			if (gun != null)
			{
				gun.timeBetweenShootingSessions = plan.Pause;
				gun.timeBetweenShootingSessionsRandomizer = plan.Pause;
				gun.bulletsShotPerSession = plan.Burst;
				if (plan.Ammo > 0)
				{
					gun.ammo = plan.Ammo;
				}
			}
			Mod.Log.LogInfo("Bot " + index + ": " + AggressionName(plan.Aggression) + ", " + KitName(plan.Kit) + " (" + plan.Points + " points), " + (plan.Behaviour.isAggressive ? "aggressive" : "calm") + ", line " + plan.Behaviour.eTurbat.ToString("0.00") + ", pace x" + plan.Pace.ToString("0.00"));
		}

		/// <summary>The text beside the settings: what the chosen steps do.</summary>
		internal static string Describe(RaceSettings s)
		{
			Step step = Steps[RaceSettings.Clamp(s.Aggression, 0, AggressionSteps - 1)];
			StringBuilder text = new StringBuilder();
			if (s.Ai == 0)
			{
				text.AppendLine("AI RIVALS is 0, so no bots race.");
				text.AppendLine();
			}
			text.AppendLine(AggressionName(s.Aggression) + ": " + Mathf.RoundToInt(step.Share * 100f) + "% of the bots drive aggressively,");
			text.AppendLine("pace " + (step.Pace >= 1f ? "+" : "") + Mathf.RoundToInt((step.Pace - 1f) * 100f) + "%, bursts of " + step.Burst + " every " + step.Pause.ToString("0.#") + " s.");
			text.AppendLine();
			switch (s.Kit)
			{
				case KitStarter: text.AppendLine("STARTER: stock cars, no upgrades."); break;
				case KitHalf: text.AppendLine("HALF KITTED: about half the upgrades, spread at random."); break;
				case KitFull: text.AppendLine("FULLY KITTED: every upgrade maxed."); break;
				case KitSpeed: text.AppendLine("SPEED DEMONS: speed, acceleration, grip and downforce maxed; fewer guns."); break;
				case KitCombat: text.AppendLine("COMBAT: full health and nitro, every bot armed with more rounds."); break;
				case KitLikeARun: text.AppendLine("LIKE A RUN: the upgrades a single-player run gives at the " + StageName(s.Stage).ToLowerInvariant() + " stage."); break;
				default: text.AppendLine("RANDOM: every bot gets a kit of its own."); break;
			}
			text.AppendLine();
			text.AppendLine(s.Spread == SpreadSame ? "SAME: every bot gets these." : s.Spread == SpreadMixed ? "MIXED: each bot rolls up to " + s.SpreadSteps + (s.SpreadSteps == 1 ? " step" : " steps") + " either side." : "RANDOM: every bot rolls its own aggression and kit.");
			return text.ToString();
		}
	}

	// A bot with a plan takes its stats and driving style from the plan, not from the game's per-name tables.
	[HarmonyPatch(typeof(CarSkinController), nameof(CarSkinController.GetStatsForThisEnemyName))]
	internal static class BotStatsPatch
	{
		private static bool Prefix(CarSkinController __instance, ref CarStats __result)
		{
			Bots.Plan plan = Bots.For(__instance);
			if (plan == null)
			{
				return true;
			}
			__result = plan.Stats.GetCarStats();
			return false;
		}
	}

	[HarmonyPatch(typeof(CarSkinController), nameof(CarSkinController.GetBehaviourForThisEnemyName))]
	internal static class BotBehaviourPatch
	{
		private static bool Prefix(CarSkinController __instance, ref DriverBehaviour __result)
		{
			Bots.Plan plan = Bots.For(__instance);
			if (plan == null)
			{
				return true;
			}
			__result = plan.Behaviour;
			return false;
		}
	}
}
