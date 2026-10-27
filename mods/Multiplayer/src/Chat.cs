using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// Text chat for the room. Messages go straight to everybody in the room; the lobby shows them in its CHAT panel (<see cref="LobbyScreen"/>),
	/// and in a race they appear in the corner of the screen and fade. The chat key (T by default, rebindable in Settings > Controls) opens a
	/// line to type, Enter sends it, Escape drops it. The text is read from <see cref="Input.inputString"/> and the game's controls are muted while
	/// it is open, so no text box (and no event system) is needed in the race scene.
	/// </summary>
	internal static class Chat
	{
		internal const int MaxLength = 140;
		private const int KeepLines = 60;
		private const float ShowSeconds = 12f;
		private const int ShownInRace = 7;
		private const float MinGap = 0.35f;

		internal struct Line
		{
			internal string Name;
			internal string Text;
			internal float At;
			internal bool Mine;
		}

		internal static readonly List<Line> Lines = new List<Line>();
		/// <summary>Messages the lobby panel has not shown yet (for the number on the CHAT button).</summary>
		internal static int Unread;
		internal static bool Typing { get; private set; }

		private static readonly Dictionary<ulong, float> lastFrom = new Dictionary<ulong, float>();
		private static readonly StringBuilder buffer = new StringBuilder();
		private static GameObject root;
		private static TMP_Text log;
		private static TMP_Text logShadow;
		private static TMP_Text input;
		private static TMP_Text inputShadow;
		private static GameObject inputBack;
		private static bool skipInputThisFrame;

		/// <summary>Strips what must not reach the screen: rich-text tags and control characters.</summary>
		internal static string Clean(string text)
		{
			StringBuilder s = new StringBuilder();
			foreach (char c in text ?? "")
			{
				if (c == '<' || c == '>' || char.IsControl(c))
				{
					continue;
				}
				s.Append(c);
			}
			string clean = s.ToString().Trim();
			return clean.Length > MaxLength ? clean.Substring(0, MaxLength) : clean;
		}

		internal static void Clear()
		{
			Lines.Clear();
			lastFrom.Clear();
			Unread = 0;
			Close();
		}

		/// <summary>This player says something to the room.</summary>
		internal static void Send(string text)
		{
			string clean = Clean(text);
			Peer me = Room.Me;
			if (clean.Length == 0 || me == null || !Room.Active)
			{
				return;
			}
			Add(me.Name, clean, true);
			Room.SendAll(new W(Msg.Chat).Str(clean));
		}

		/// <summary>Somebody else said something. The name is the room's, not the sender's claim.</summary>
		internal static void Receive(Peer from, string text)
		{
			float now = Time.unscaledTime;
			float last;
			if (lastFrom.TryGetValue(from.Id, out last) && now - last < MinGap)
			{
				return; // a flood
			}
			lastFrom[from.Id] = now;
			string clean = Clean(text);
			if (clean.Length > 0)
			{
				Add(from.Name, clean, false);
			}
		}

		private static void Add(string name, string text, bool mine)
		{
			Lines.Add(new Line { Name = name, Text = text, At = Time.unscaledTime, Mine = mine });
			if (Lines.Count > KeepLines)
			{
				Lines.RemoveAt(0);
			}
			if (!mine)
			{
				Unread++;
			}
			Mod.Log.LogInfo("Chat " + name + ": " + text);
			Room.Notify();
		}

		/// <summary>The lines as one block of text, newest last, in the game's scoreboard orange for the names.</summary>
		internal static string Block(int count, bool onlyRecent)
		{
			StringBuilder s = new StringBuilder();
			int from = Math.Max(0, Lines.Count - count);
			float now = Time.unscaledTime;
			for (int i = from; i < Lines.Count; i++)
			{
				Line line = Lines[i];
				if (onlyRecent && !Typing && now - line.At > ShowSeconds)
				{
					continue;
				}
				if (s.Length > 0)
				{
					s.Append('\n');
				}
				s.Append("<color=#FF8C00>").Append(line.Name.ToUpperInvariant()).Append("</color>  ").Append(line.Text);
			}
			return s.ToString();
		}

		// ------------------------------------------------------------------ the race overlay

		private static bool InRaceScene
		{
			get { return Room.Active && Room.Phase == Phase.Racing && RaceSync.TM != null && !Room.ViewingResults; }
		}

		/// <summary>Every frame: the race overlay and the typing.</summary>
		internal static void Tick()
		{
			if (!InRaceScene)
			{
				if (Typing)
				{
					Close();
				}
				if (root != null)
				{
					root.SetActive(false);
				}
				return;
			}
			if (!Typing && !Pause.Open && Mod.ChatKey())
			{
				Open();
			}
			else if (Typing)
			{
				Type();
			}
			if (root == null)
			{
				Build();
			}
			string block = Block(ShownInRace, true);
			bool show = Typing || block.Length > 0;
			root.SetActive(show);
			if (!show)
			{
				return;
			}
			if (log.text != block)
			{
				log.text = block;
				logShadow.text = System.Text.RegularExpressions.Regex.Replace(block, "<[^>]+>", "");
			}
			inputBack.SetActive(Typing);
			input.gameObject.SetActive(Typing);
			inputShadow.gameObject.SetActive(Typing);
			if (Typing)
			{
				string typed = "> " + buffer + (((int)(Time.unscaledTime * 2f) & 1) == 0 ? "_" : " ");
				input.text = typed;
				inputShadow.text = typed;
			}
		}

		/// <summary>
		/// Top left, small and quiet: no box, just the lines in a pixel font with a drop shadow so they read over any scenery; only the
		/// line being typed gets a thin dark strip behind it.
		/// </summary>
		private static void Build()
		{
			Canvas canvas = Ui.Overlay("MultiplayerChat", 29000);
			UnityEngine.Object.DontDestroyOnLoad(canvas.gameObject);
			root = canvas.gameObject;
			RectTransform back = Ui.Box("TypingStrip", canvas.transform, new Color(0f, 0f, 0f, 0.55f));
			Ui.Anchor(back, new Vector2(0f, 1f), new Vector2(16f, -14f), new Vector2(520f, 36f));
			back.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
			inputBack = back.gameObject;
			inputShadow = Ui.Label(canvas.transform, "", 24f, Color.black, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(24f, -16f), new Vector2(504f, 32f));
			input = Ui.Label(canvas.transform, "", 24f, Ui.Amber, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(22f, -14f), new Vector2(504f, 32f));
			logShadow = Ui.Label(canvas.transform, "", 24f, Color.black, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(24f, -58f), new Vector2(520f, 230f));
			log = Ui.Label(canvas.transform, "", 24f, Color.white, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(22f, -56f), new Vector2(520f, 230f));
			foreach (TMP_Text t in new[] { input, inputShadow })
			{
				t.textWrappingMode = TextWrappingModes.NoWrap;
				t.richText = false;
			}
			logShadow.richText = false;
		}

		private static void Open()
		{
			Mod.Log.LogInfo("Chat line opened.");
			Typing = true;
			buffer.Length = 0;
			skipInputThisFrame = true; // the key that opened the line is also in inputString
			if (root == null)
			{
				Build();
			}
			GameInput.Mute(root.GetComponent<Canvas>(), true);
		}

		internal static void Close()
		{
			if (Typing && root != null)
			{
				GameInput.Mute(root.GetComponent<Canvas>(), false);
			}
			Typing = false;
			buffer.Length = 0;
		}

		private static void Type()
		{
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				Close();
				return;
			}
			if (skipInputThisFrame)
			{
				skipInputThisFrame = false;
				return;
			}
			if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.V))
			{
				buffer.Append(Clean(GUIUtility.systemCopyBuffer));
				if (buffer.Length > MaxLength)
				{
					buffer.Length = MaxLength;
				}
				return;
			}
			foreach (char c in Input.inputString)
			{
				if (c == '\b')
				{
					if (buffer.Length > 0)
					{
						buffer.Length--;
					}
				}
				else if (c == '\n' || c == '\r')
				{
					string text = buffer.ToString();
					Close();
					Send(text);
					return;
				}
				else if (!char.IsControl(c) && c != '<' && c != '>' && buffer.Length < MaxLength)
				{
					buffer.Append(c);
				}
			}
		}

		internal static void Remove()
		{
			Close();
			if (root != null)
			{
				UnityEngine.Object.Destroy(root);
				root = null;
			}
		}
	}
}
