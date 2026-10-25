using System;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// How a player's car is painted, so the others see it. The paint is the game's own (the Paint button in the garage, three colours kept in
	/// the save as <c>playerCarSkin_colours</c>), plus which of the car's bodies is fitted once the car exists in the race. It travels as one
	/// short line, <c>model:RRGGBB,RRGGBB,RRGGBB</c> (model -1 = the colours only, keep the stand-in's own body), clients tell the host and
	/// the host puts everybody's line in the roster, so a late joiner gets them too.
	/// </summary>
	internal static class Skins
	{
		private static string lastSent = "";
		private static float nextCheck;
		private static readonly Dictionary<ulong, string> applied = new Dictionary<ulong, string>();

		private static string known = "";

		/// <summary>
		/// The look of this player's car right now: the live car in a race; in the garage the preview car (it always shows what the Paint
		/// menu has chosen, the stock colours included); otherwise the colours the game has them wearing. When none of these says anything
		/// (the menu, and the default paint) the last look seen is kept, so the swatches never go blank.
		/// </summary>
		internal static string Local()
		{
			try
			{
				TrackManager tm = RaceSync.TM;
				if (tm != null && tm.playerCarController != null)
				{
					CarSkinController live = tm.playerCarController.GetComponentInChildren<CarSkinController>(true);
					if (live == null)
					{
						live = tm.playerCarController.GetComponentInParent<CarSkinController>();
					}
					if (live != null && live.carSkin != null)
					{
						known = "-1:" + PaintSchemes.Encode(new[] { live.carSkin.color1, live.carSkin.color2, live.carSkin.color3 });
						return live.carSkin.skinIndex + ":" + PaintSchemes.Encode(new[] { live.carSkin.color1, live.carSkin.color2, live.carSkin.color3 });
					}
				}
				if (!Flow.InMenu)
				{
					LoadoutPreview preview = UnityEngine.Object.FindFirstObjectByType<LoadoutPreview>();
					CarSkin shown = preview != null && preview.skinController != null ? preview.skinController.carSkin : null;
					if (shown != null)
					{
						known = "-1:" + PaintSchemes.Encode(new[] { shown.color1, shown.color2, shown.color3 });
						return known;
					}
				}
				Color[] worn = PaintSchemes.Decode(PlayerCarSkin.Line) ?? PaintSchemes.ChosenColours;
				if (worn != null)
				{
					known = "-1:" + PaintSchemes.Encode(worn);
				}
				return known;
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not read the car's paint: " + e.Message);
				return "";
			}
		}

		/// <summary>Parses a skin line. A line that is not exactly what <see cref="Local"/> writes is refused.</summary>
		internal static bool TryParse(string line, out int model, out Color[] colours)
		{
			model = -1;
			colours = null;
			if (string.IsNullOrEmpty(line) || line.Length > 48)
			{
				return false;
			}
			int colon = line.IndexOf(':');
			if (colon < 1 || !int.TryParse(line.Substring(0, colon), out model) || model < -1 || model > 20)
			{
				return false;
			}
			string[] parts = line.Substring(colon + 1).Split(',');
			if (parts.Length != 3)
			{
				return false;
			}
			colours = new Color[3];
			for (int i = 0; i < 3; i++)
			{
				if (parts[i].Length != 6 || !ColorUtility.TryParseHtmlString("#" + parts[i], out colours[i]))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Valid or empty; anything else from the network is dropped.</summary>
		internal static string Sanitize(string line)
		{
			int model;
			Color[] colours;
			return TryParse(line, out model, out colours) ? line : "";
		}

		/// <summary>Every second: tell the room when the paint changed (the garage's Paint button, or the car appearing in the race).</summary>
		internal static void Tick()
		{
			if (!Room.Active || Time.unscaledTime < nextCheck)
			{
				return;
			}
			nextCheck = Time.unscaledTime + 1f;
			Peer me = Room.Me;
			if (me == null)
			{
				return;
			}
			Enforce();
			string line = Local();
			if (line == lastSent && me.Skin == line)
			{
				return;
			}
			Mod.Log.LogInfo("Paint of this car: " + (line.Length == 0 ? "stock (nothing chosen)" : line));
			lastSent = line;
			me.Skin = line;
			if (Room.IsHost)
			{
				Room.BroadcastRoster();
			}
			else
			{
				Room.SendHost(new W(Msg.Skin).Str(line));
			}
		}

		/// <summary>
		/// Once a second in a race: every other player's stand-in must still wear what that player sent. Anything in the game that
		/// re-equips a car's skin (it does so for several reasons) would otherwise leave it with a random enemy look.
		/// </summary>
		private static void Enforce()
		{
			if (RaceSync.TM == null)
			{
				return;
			}
			foreach (Peer p in Room.Peers)
			{
				int model;
				Color[] colours;
				if (p.Local || !TryParse(p.Skin, out model, out colours))
				{
					continue;
				}
				RemoteCar rc = RaceSync.RemoteOf(p.Id);
				CarSkinController controller = rc == null ? null : rc.GetComponent<CarSkinController>();
				if (controller == null)
				{
					continue;
				}
				CarSkin now = controller.carSkin;
				bool same = now != null && Same(now.color1, colours[0]) && Same(now.color2, colours[1]) && Same(now.color3, colours[2]);
				if (!same)
				{
					Apply(rc, p.Skin);
				}
			}
		}

		private static bool Same(Color a, Color b)
		{
			return Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;
		}

		/// <summary>Forget what was sent when a room ends, so the next room hears it again.</summary>
		internal static void Reset()
		{
			lastSent = "";
			known = "";
			applied.Clear();
		}

		/// <summary>Dresses a stand-in in what its player sent (nothing happens when they sent nothing).</summary>
		internal static void Apply(RemoteCar rc, string line)
		{
			int model;
			Color[] colours;
			if (rc == null || rc.IsAi || !TryParse(line, out model, out colours))
			{
				return;
			}
			CarSkinController controller = rc.GetComponent<CarSkinController>();
			if (controller == null || controller.carModels == null || controller.carModels.Length == 0)
			{
				return;
			}
			CarSkin skin = ScriptableObject.CreateInstance<CarSkin>();
			// a body this car does not have would hide the whole car: keep its own then
			skin.skinIndex = model >= 0 && model < controller.carModels.Length ? model : (controller.carSkin != null ? controller.carSkin.skinIndex : 0);
			skin.color1 = colours[0];
			skin.color2 = colours[1];
			skin.color3 = colours[2];
			controller.ApplySkin(skin);
			if (!applied.ContainsKey(rc.PeerId) || applied[rc.PeerId] != line)
			{
				Mod.Log.LogInfo("Paint applied to " + rc.DisplayName() + "'s car: " + line);
			}
			applied[rc.PeerId] = line;
		}

		/// <summary>After a roster: any stand-in whose player's paint is not what it wears yet gets dressed.</summary>
		internal static void Refresh()
		{
			foreach (Peer p in Room.Peers)
			{
				string worn;
				if (p.Local || string.IsNullOrEmpty(p.Skin) || (applied.TryGetValue(p.Id, out worn) && worn == p.Skin))
				{
					continue;
				}
				RemoteCar rc = RaceSync.RemoteOf(p.Id);
				if (rc != null)
				{
					Apply(rc, p.Skin);
				}
			}
		}

		/// <summary>The three colours of a player's line for the lobby's swatches, or null.</summary>
		internal static Color[] Colours(string line)
		{
			int model;
			Color[] colours;
			return TryParse(line, out model, out colours) ? colours : null;
		}
	}
}
