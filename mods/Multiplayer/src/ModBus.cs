using System;
using System.Collections.Generic;
using System.Linq;
using HalfGripTools;

namespace Multiplayer
{
	/// <summary>
	/// The part of HalfGripTools.Net that moves bytes: other mods call <c>ctx.Net.Send</c>, this puts the message on the room's connection under the
	/// sending mod's id, and hands what arrives to the same mod on this machine. It also tells HalfGripTools who is in the room and what phase it is in.
	/// Everybody in a room has the same mods (the room checks), so a message only ever reaches the copy of the mod that sent it.
	/// </summary>
	internal sealed class ModBus : INetTransport
	{
		private static float nextPush;

		internal static void Install()
		{
			Net.SetTransport(new ModBus());
			Room.Changed += Push;
		}

		internal static void Remove()
		{
			Room.Changed -= Push;
			Net.SetTransport(null);
		}

		/// <summary>Twice a second, in case something changed without a notice (a player's name, say).</summary>
		internal static void Tick()
		{
			if (UnityEngine.Time.realtimeSinceStartup >= nextPush)
			{
				nextPush = UnityEngine.Time.realtimeSinceStartup + 0.5f;
				Push();
			}
		}

		/// <summary>Tells HalfGripTools the room's players, phase and mode.</summary>
		internal static void Push()
		{
			try
			{
				if (!Room.Active)
				{
					Net.SetRoom(false, false, 0UL, 0UL, null, NetPhase.None, null);
					return;
				}
				List<NetPlayer> players = Room.Peers.Where(p => !p.Gone).Select(p => new NetPlayer { Id = p.Id, Name = p.Name, IsHost = p.Id == Room.HostId, IsLocal = p.Local }).ToList();
				RaceSettings mode = Room.Race ?? Room.Settings;
				Net.SetRoom(true, Room.IsHost, Room.LocalId, Room.HostId, players, Map(Room.Phase), mode == null ? "race" : mode.PublicModeId());
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Telling mods about the room failed: " + e.Message);
			}
		}

		private static NetPhase Map(Phase phase)
		{
			switch (phase)
			{
				case Phase.Lobby: return NetPhase.Lobby;
				case Phase.Loading: return NetPhase.Loading;
				case Phase.Racing: return NetPhase.Racing;
				case Phase.Results: return NetPhase.Results;
				default: return NetPhase.None;
			}
		}

		public void Send(string channel, ulong to, byte[] data, bool reliable)
		{
			if (string.IsNullOrEmpty(channel) || channel.Length > 60)
			{
				return;
			}
			W w = new W(Msg.ModData).Str(channel).Raw(data);
			if (to == Net.Everyone)
			{
				Room.SendAll(w, reliable);
			}
			else
			{
				Room.SendTo(to, w, reliable);
			}
		}

		public void EndRace()
		{
			if (Room.IsHost && Room.Phase == Phase.Racing)
			{
				Room.ForceEnd();
			}
		}

		/// <summary>A message from another player's copy of a mod.</summary>
		internal static void OnData(ulong from, R r)
		{
			string channel = r.Str();
			byte[] data = r.Raw();
			Net.Deliver(channel, from, data);
		}
	}
}
