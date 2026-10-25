using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// Player-versus-player damage. The shooter's game finds the hit with the game's own code (the other car is a stand-in enemy),
	/// multiplies the damage by what the host set, and tells the owner of that car, whose game applies it to its real car through
	/// <see cref="CarLink.TakeCrashDamage"/> (so Max HP, regeneration and wreck-saving items work). Everything else about the hit
	/// (lifesteal, hit marks, HP per shot, kills) is the game's own, on the stand-in.
	/// </summary>
	internal static class Combat
	{
		internal enum Source
		{
			None,
			Gun,
			Explosion,
			Crash
		}

		internal const int KindGun = 0;
		internal const int KindExplosion = 1;
		internal const int KindItem = 2;
		internal const int KindTire = 3;

		/// <summary>What the game is doing right now that may hurt an enemy car.</summary>
		internal static Source Current;
		/// <summary>An AI gun is hitting the player's car.</summary>
		internal static bool AiShot;
		/// <summary>The local car is crashing into this stand-in.</summary>
		internal static RemoteCar CrashWith;
		/// <summary>A hit that arrived from another player is being applied to the local car; it is already scaled.</summary>
		internal static bool Applying;
		/// <summary>The mod itself is killing a stand-in; the patch must not forward it.</summary>
		internal static bool Internal;
		internal static readonly Dictionary<int, ulong> AiHitter = new Dictionary<int, ulong>();
		/// <summary>The AI car (by index) whose gun is firing right now, and the last one that hit the local player.</summary>
		internal static int AiShooterNow = -1;
		internal static int LastAiShooter = -1;
		internal static float LastAiShotAt;

		internal static float Multiplier(Source source)
		{
			RaceSettings s = Room.Race;
			if (s == null)
			{
				return 1f;
			}
			switch (source)
			{
				case Source.Gun:
					return s.Gun;
				case Source.Explosion:
					return s.Explosion;
				default:
					return s.Item;
			}
		}

		internal static int KindOf(Source source)
		{
			return source == Source.Gun ? KindGun : source == Source.Explosion ? KindExplosion : KindItem;
		}

		/// <summary>Wrecks a stand-in through the game's own death path, so kill rewards and effects happen when the local player did it.</summary>
		internal static void Kill(RemoteCar rc, bool byLocalPlayer)
		{
			EnemyController ec = rc == null ? null : rc.Controller;
			if (ec == null || !ec.alive)
			{
				return;
			}
			Internal = true;
			try
			{
				RaceSync.HealthField.SetValue(ec, 1f);
				ec.TakeDamage(1f, byLocalPlayer);
			}
			finally
			{
				Internal = false;
			}
		}

		/// <summary>A hit from another player on the local car.</summary>
		internal static void OnHit(ulong from, R r)
		{
			int kind = r.U8();
			float amount = r.F32();
			int wheel = r.U8();
			TrackManager tm = RaceSync.TM;
			if (tm == null || Room.Race == null || Room.Phase != Phase.Racing || RaceSync.LocalOut || tm.playerCarlink == null)
			{
				return;
			}
			Peer shooter = Room.Find(from);
			Peer me = Room.Me;
			if (shooter == null || me == null)
			{
				return;
			}
			if (kind == KindTire)
			{
				CarControllerNew cc = tm.playerCarController;
				if (Room.Race.Tires && cc != null && cc.wheels != null && wheel >= 0 && wheel < cc.wheels.Length && cc.wheels[wheel] != null)
				{
					cc.wheels[wheel].deadTime = 6f;
					TextController.EnqueueRandomText(1f, "TIRE SHOT");
				}
				return;
			}
			if (float.IsNaN(amount) || amount <= 0f)
			{
				return;
			}
			amount = Math.Min(amount, 150f);
			me.LastAttacker = from;
			me.LastAttackTime = Time.time;
			Applying = true;
			try
			{
				tm.playerCarlink.TakeCrashDamage(amount, true, DamageKind.Other);
			}
			finally
			{
				Applying = false;
			}
			Mod.Log.LogInfo("Hit by " + shooter.Name + ": " + amount.ToString("0.#") + " damage, hp now " + tm.playerCarlink.hp.ToString("0.#"));
		}

		/// <summary>Host: a client hit one of the host's AI cars.</summary>
		internal static void OnAiHit(ulong from, R r)
		{
			int index = r.U8();
			float amount = r.F32();
			EnemyController ec = RaceSync.AiAt(index);
			if (ec == null || !ec.alive || float.IsNaN(amount) || amount <= 0f)
			{
				return;
			}
			AiHitter[index] = from;
			Internal = true;
			try
			{
				ec.TakeDamage(Math.Min(amount, 300f), false);
			}
			finally
			{
				Internal = false;
			}
		}
	}

	// ---------------------------------------------------------------------- tagging what the game is doing

	[HarmonyPatch(typeof(Gun), "FireRay")]
	internal static class TagGunShot
	{
		private static void Prefix() { Combat.Current = Combat.Source.Gun; }
		private static void Finalizer() { Combat.Current = Combat.Source.None; }
	}

	[HarmonyPatch(typeof(AreaBlast), nameof(AreaBlast.Apply))]
	internal static class TagBlast
	{
		private static void Prefix() { Combat.Current = Combat.Source.Explosion; }
		private static void Finalizer() { Combat.Current = Combat.Source.None; }
	}

	[HarmonyPatch(typeof(CarControllerNew), "OnCollisionEnter")]
	internal static class TagCrash
	{
		private static void Prefix(Collision collision)
		{
			if (!Room.Active)
			{
				return;
			}
			Combat.Current = Combat.Source.Crash;
			Combat.CrashWith = collision != null && collision.gameObject != null ? collision.gameObject.GetComponentInParent<RemoteCar>() : null;
		}

		private static void Finalizer()
		{
			Combat.Current = Combat.Source.None;
			Combat.CrashWith = null;
		}
	}

	[HarmonyPatch(typeof(EnemyGun), "HandleHit")]
	internal static class TagAiShot
	{
		private static void Prefix(EnemyGun __instance)
		{
			Combat.AiShot = true;
			Combat.AiShooterNow = RaceSync.AiIndexOfGun(__instance);
		}

		private static void Finalizer()
		{
			Combat.AiShot = false;
			Combat.AiShooterNow = -1;
		}
	}

	// ---------------------------------------------------------------------- the routing

	[HarmonyPatch(typeof(EnemyController), nameof(EnemyController.TakeDamage), new[] { typeof(float), typeof(bool) })]
	internal static class RouteDamage
	{
		private static bool Prefix(EnemyController __instance, ref float amount, bool byPlayer)
		{
			if (!Room.Active || Room.Race == null || Combat.Internal)
			{
				return true;
			}
			RemoteCar rc = __instance.GetComponent<RemoteCar>();
			if (rc == null)
			{
				if (Room.IsHost && !byPlayer && Combat.AiShooterNow >= 0)
				{
					int victim = RaceSync.AiIndexOf(__instance);
					if (victim >= 0)
					{
						RaceSync.NoteAiHit(victim, Combat.AiShooterNow); // an AI car shot an AI car: a KO for the shooter if it dies of it
					}
				}
				if (Room.IsHost && byPlayer && Combat.Current != Combat.Source.Crash)
				{
					int index = RaceSync.AiIndexOf(__instance);
					if (index >= 0)
					{
						Combat.AiHitter[index] = Room.LocalId;
					}
				}
				return true;
			}
			RaceSync.HealthField.SetValue(__instance, RemoteCar.Health); // a stand-in is never wrecked by damage, only by its owner's word
			if (!byPlayer || Combat.Current == Combat.Source.Crash || amount <= 0f)
			{
				return true;
			}
			if (rc.IsAi)
			{
				if (!Room.IsHost)
				{
					Room.SendHost(new W(Msg.AiHit).U8(rc.AiIndex).F32(amount));
				}
				return true;
			}
			float scaled = amount * Combat.Multiplier(Combat.Current);
			amount = scaled;
			Room.SendTo(rc.PeerId, new W(Msg.Hit).U8(Combat.KindOf(Combat.Current)).F32(scaled).U8(0));
			return true;
		}

		private static void Postfix(EnemyController __instance)
		{
			if (Room.Active && Room.Race != null && __instance.alive && __instance.GetComponent<RemoteCar>() != null)
			{
				RaceSync.HealthField.SetValue(__instance, RemoteCar.Health);
			}
		}
	}

	// Shooting a tire of another player's car: the shooter sees it burst, the owner's wheel goes dead.
	[HarmonyPatch(typeof(CarTireDamage), "TakeShot")]
	internal static class RouteTireShot
	{
		private static bool Prefix(CarTireDamage __instance, int wheel)
		{
			if (!Room.Active || Room.Race == null)
			{
				return true;
			}
			EnemyController ec = __instance.Enemy;
			RemoteCar rc = ec == null ? null : ec.GetComponent<RemoteCar>();
			if (rc == null || rc.IsAi)
			{
				return true;
			}
			if (!Room.Race.Tires)
			{
				return false;
			}
			Room.SendTo(rc.PeerId, new W(Msg.Hit).U8(Combat.KindTire).F32(0f).U8(wheel));
			return true;
		}
	}

	// A shot at a player's car is not shielded by how far ahead of the shooter it is.
	[HarmonyPatch(typeof(LapShield), nameof(LapShield.Scale))]
	internal static class NoLapShieldOnPlayers
	{
		private static bool Prefix(EnemyController enemy, float damage, ref float __result)
		{
			if (!Room.Active || Room.Race == null || enemy == null)
			{
				return true;
			}
			RemoteCar rc = enemy.GetComponent<RemoteCar>();
			if (rc != null && rc.Human)
			{
				__result = damage;
				return false;
			}
			return true;
		}
	}
}
