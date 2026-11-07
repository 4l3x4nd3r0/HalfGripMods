using HarmonyLib;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// Keeps every machine's race clock (<c>TrackManager.TimeSinceRaceStart</c> = <c>Time.time - raceStartTime</c>) on the host's.
	/// Each machine started its own clock when its countdown ended, up to one network trip apart, and <c>Time.time</c> only moves by
	/// <c>Time.maximumDeltaTime</c> in a frame, so a long frame (a teleport or a far respawn that builds terrain, a load stall) loses time on that machine only.
	/// Open World's weather, time of day and event timers read this clock, so they drifted apart between players.
	/// The host sends its clock once a second; a client corrects its <c>raceStartTime</c>: a snap when it is more than half a second off, small steps otherwise.
	/// </summary>
	internal static class ClockSync
	{
		private static float nextSend;
		private static bool logged;

		internal static void Reset()
		{
			nextSend = 0f;
			logged = false;
		}

		/// <summary>Host: say what the race clock reads.</summary>
		internal static void TickHost(TrackManager tm, float now)
		{
			if (now < nextSend || tm == null)
			{
				return;
			}
			nextSend = now + 1f;
			float clock = tm.TimeSinceRaceStart;
			if (clock > 0f)
			{
				Room.SendAll(new W(Msg.Clock).F32(clock).F32(Time.realtimeSinceStartup), false);
			}
		}

		/// <summary>Client: the host's clock arrived.</summary>
		internal static void OnClock(TrackManager tm, R r)
		{
			float hostClock = r.F32();
			r.F32(); // the host's real time of sending: not needed, the round trip comes from the pings
			if (tm == null || hostClock <= 0f || Room.IsHost)
			{
				return;
			}
			Traverse t = Traverse.Create(tm);
			if (!t.Field("raceStarted").GetValue<bool>())
			{
				return;
			}
			Peer me = Room.Me;
			float oneWay = me != null && me.PingMs > 0 ? Mathf.Min(me.PingMs, 1000) / 2000f : 0f;
			float error = hostClock + oneWay - tm.TimeSinceRaceStart; // > 0: this clock is behind the host's
			float abs = Mathf.Abs(error);
			if (abs < 0.02f)
			{
				return;
			}
			float step = abs > 0.5f ? error : Mathf.Clamp(error, -0.05f, 0.05f);
			float start = t.Field("raceStartTime").GetValue<float>();
			t.Field("raceStartTime").SetValue(start - step);
			if (!logged || abs > 1f)
			{
				logged = true;
				Mod.Log.LogInfo("Clock: this race clock was " + error.ToString("0.00") + " s " + (error > 0f ? "behind" : "ahead of") + " the host's (" + (abs > 0.5f ? "set to it" : "eased") + ").");
			}
		}
	}
}
