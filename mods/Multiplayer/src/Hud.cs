using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer
{
	/// <summary>
	/// What the room adds on top of the race: only the spectator camera now. The scoreboard is the game's own (names in orange for humans,
	/// see <see cref="RoomScoreboard"/>) and the results are a screen of the MULTIPLAYER menu.
	/// </summary>
	internal static class Hud
	{
		internal static void Remove()
		{
			Spectator.Stop();
		}

		internal static void Tick()
		{
			if (Room.Phase != Phase.Racing && Room.Phase != Phase.Results)
			{
				Spectator.Stop();
				return;
			}
			Spectator.Tick();
		}
	}
}
