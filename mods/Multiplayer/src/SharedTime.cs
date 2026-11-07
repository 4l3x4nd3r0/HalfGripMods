using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// Slow motion and the Inverter in a room race act on every player.
	/// <list type="bullet">
	/// <item>Slow motion: the lever's own time scale is switched off in a room. A pull is sent to the host, which tells everybody else; every machine (the one
	/// that pulled too) runs one shared slow motion at <see cref="Strength"/> of normal speed. Pulls overlap, they do not add: the window ends at the latest
	/// end of any pull (a 4 s pull and another 2 s later end 6 s after the first).</item>
	/// <item>Inverter: every machine keeps its own player car's history in a <c>TimeRewinder</c> of ours. A flip is sent to the host, which tells everybody else,
	/// and each machine rewinds its own car (hit points come back with it, the game's rule). The other cars are not rewound by the machine that
	/// does not own them: their owners rewind them, and the stand-ins follow by the network.</item>
	/// </list>
	/// </summary>
	internal static class SharedTime
	{
		/// <summary>How fast time runs during the shared slow motion (single player's lever: a quarter).</summary>
		internal const float Strength = 0.5f;

		private static float endsAt;
		private static float power;
		private static bool applied;
		private static TimeRewinder rewinder;
		private static TrackManager owner;

		internal static void Reset()
		{
			if (applied && owner != null)
			{
				TimeScale.Clear(owner);
			}
			applied = false;
			owner = null;
			endsAt = 0f;
			power = 0f;
			rewinder = null;
		}

		private static bool Live { get { return Room.Active && Room.Phase == Phase.Racing; } }

		internal static void Tick(TrackManager tm)
		{
			if (tm == null)
			{
				return;
			}
			owner = tm;
			// slow motion: ease in and out like the lever does, whole seconds counted in game time
			bool on = Time.time < endsAt;
			power = Mathf.MoveTowards(power, on ? 1f : 0f, Time.unscaledDeltaTime);
			if (power > 0f)
			{
				applied = true;
				TimeScale.Set(tm, Mathf.Lerp(1f, Strength, power));
			}
			else if (applied)
			{
				applied = false;
				TimeScale.Clear(tm);
			}
			// the car's history for the Inverter
			if (rewinder == null && tm.playerCarController != null)
			{
				rewinder = tm.playerCarController.gameObject.GetComponent<TimeRewinder>() ?? tm.playerCarController.gameObject.AddComponent<TimeRewinder>();
			}
		}

		// ------------------------------------------------------------------ slow motion

		/// <summary>A lever was pulled here: <paramref name="seconds"/> of slow motion for everybody.</summary>
		internal static void Pulled(float seconds)
		{
			if (!Live || seconds <= 0f)
			{
				return;
			}
			Start(seconds);
			W w = new W(Msg.SlowMo).F32(seconds);
			if (Room.IsHost)
			{
				Room.SendAll(w);
			}
			else
			{
				Room.SendHost(w);
			}
		}

		internal static void OnSlowMo(ulong from, R r)
		{
			float seconds = Mathf.Clamp(r.F32(), 0f, 30f);
			if (!Live || seconds <= 0f)
			{
				return;
			}
			if (Room.IsHost)
			{
				// tell everybody but the one who pulled (it already runs it)
				byte[] data = new W(Msg.SlowMo).F32(seconds).Bytes();
				foreach (Peer p in Room.Peers.Where(p => !p.Local && p.Id != from).ToList())
				{
					Room.B.Send(p.Id, data, true);
				}
			}
			Start(seconds);
		}

		private static void Start(float seconds)
		{
			float end = Time.time + seconds;
			if (end > endsAt)
			{
				endsAt = end;
			}
			Mod.Log.LogInfo("Slow motion for everybody: " + seconds.ToString("0.#") + " s, ends in " + (endsAt - Time.time).ToString("0.#") + " s.");
		}

		// ------------------------------------------------------------------ the Inverter

		internal static void Flipped()
		{
			if (!Live)
			{
				return;
			}
			W w = new W(Msg.Rewind);
			if (Room.IsHost)
			{
				Room.SendAll(w);
			}
			else
			{
				Room.SendHost(w);
			}
		}

		internal static void OnRewind(ulong from)
		{
			if (!Live)
			{
				return;
			}
			if (Room.IsHost)
			{
				byte[] data = new W(Msg.Rewind).Bytes();
				foreach (Peer p in Room.Peers.Where(p => !p.Local && p.Id != from).ToList())
				{
					Room.B.Send(p.Id, data, true);
				}
			}
			if (rewinder == null || RaceSync.LocalOut || RaceSync.Wrecked)
			{
				return;
			}
			if (Object.FindObjectsByType<TimeRewinder>(FindObjectsSortMode.None).Any(t => t.Rewinding))
			{
				return; // an Inverter here is already running one
			}
			if (rewinder.Begin())
			{
				Mod.Log.LogInfo("Time rewinds for everybody.");
			}
		}
	}

	/// <summary>The lever's own time scale is off in a room (<see cref="SharedTime"/> applies one for everybody); a pull is passed on.</summary>
	[HarmonyPatch(typeof(NSlowMoLever), "Update")]
	internal static class SharedSlowMoLever
	{
		private static void Prefix(NSlowMoLever __instance, out bool __state)
		{
			__state = __instance.active;
			if (Room.Active && Room.InRace)
			{
				__instance.affectTimeScale = false;
			}
		}

		private static void Postfix(NSlowMoLever __instance, bool __state)
		{
			if (!__state && __instance.active && Room.Active && Room.InRace)
			{
				SharedTime.Pulled(__instance.slowMoActiveTimeLeft);
			}
		}
	}

	/// <summary>A flip of the hourglass that started a rewind is passed on to the other players.</summary>
	[HarmonyPatch(typeof(NInverter), "Flip")]
	internal static class SharedInverterFlip
	{
		private static void Prefix(NInverter __instance, out bool __state)
		{
			__state = __instance.active;
		}

		private static void Postfix(NInverter __instance, bool __state)
		{
			if (!__state && __instance.active && Room.Active && Room.InRace)
			{
				SharedTime.Flipped();
			}
		}
	}

	/// <summary>
	/// A rewind moves the bodies it recorded: in a room only this machine's own car (and on the host the real AI cars). The stand-ins of other players and, on
	/// a client, of the host's AI cars are driven by the network and must stay as they are.
	/// </summary>
	[HarmonyPatch(typeof(TimeRewinder), "Bodies")]
	internal static class RewindOwnBodies
	{
		private static void Postfix(List<Rigidbody> into)
		{
			if (Room.Active && into != null)
			{
				into.RemoveAll(rb => rb != null && rb.GetComponentInParent<RemoteCar>() != null);
			}
		}
	}
}
