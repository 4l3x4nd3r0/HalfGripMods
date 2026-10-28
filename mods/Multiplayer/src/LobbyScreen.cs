using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Multiplayer
{
	/// <summary>
	/// The Multiplayer screen in the main menu. It is a section like Loadout and Collection (same parent, same full-size rect, same black
	/// background and header), and its buttons are copies of the menu's own: the red NEW RUN style button for the main actions and the
	/// small CounterBadge button of the Collection screen for the rest. Four views: home (GARAGE, HOST ROOM, JOIN ROOM), the room,
	/// the friend list and, for the host, the race settings.
	/// </summary>
	internal sealed class LobbyScreen : MonoBehaviour
	{
		private sealed class SettingRow
		{
			internal RectTransform Root;
			internal TMP_Text Value;
			internal Func<string> Get;
			internal Func<bool> Visible;
			internal int Column;
			internal Button[] Arrows;
		}

		private const float ColumnLeft0 = -860f;
		private const float ColumnLeft1 = 50f;
		private const float RowTop = 230f;
		private const float RowStep = 70f;

		private MainMenu menu;
		private GameObject bigTemplate;
		private GameObject badgeTemplate;
		private GameObject backTabTemplate;

		private RectTransform home;
		private RectTransform inside;
		private RectTransform settingsView;
		private bool showSettings;
		private TMP_Text subtitle;
		private TMP_Text message;
		private TMP_Text codeText;
		private TMP_Text roomLine;
		private TMP_Text waitText;
		private TMP_Text modsLine;
		private TMP_Text revolverText;
		private TMP_InputField codeField;
		private Button hostButton;
		private Button joinButton;
		private Button soloButton;
		private Button readyButton;
		private Button settingsButton;
		private Button startButton;
		private Button lockButton;
		private Button roomGarageButton;
		private Button leaveButton;
		private RectTransform lockIcon;
		private readonly Button[] kickButtons = new Button[Room.MaxPlayers];
		private RectTransform resultsView;
		private Button backToLobbyButton;
		private TMP_Text resultsWait;
		private readonly TMP_Text[][] resultCells = new TMP_Text[Room.MaxPlayers][];
		private readonly GameObject[] resultRows = new GameObject[Room.MaxPlayers];
		private TMP_Text startTip;
		private readonly TMP_Text[] memberNames = new TMP_Text[Room.MaxPlayers];
		private readonly TMP_Text[] memberTags = new TMP_Text[Room.MaxPlayers];
		private readonly TMP_Text[] memberTotals = new TMP_Text[Room.MaxPlayers];
		private readonly TMP_Text[] memberPings = new TMP_Text[Room.MaxPlayers];
		private readonly Image[][] memberBars = new Image[Room.MaxPlayers][];
		private readonly Image[][] memberPaint = new Image[Room.MaxPlayers][];
		private RectTransform chatBox;
		private ScrollRect chatScroll;
		private TMP_Text chatLog;
		private TMP_InputField chatField;
		private TMP_Text resultsInfo;
		private TMP_Text totalTitle;
		private readonly GameObject[] memberRows = new GameObject[Room.MaxPlayers];
		private readonly List<SettingRow> settingRows = new List<SettingRow>();

		internal static GameObject Build(MainMenu owner)
		{
			Transform ui = owner.transform;
			Transform sections = ui.Find("SidePanel/Sections");
			Transform buttons = ui.Find("SidePanel/Buttons");
			Transform collection = sections == null ? null : sections.Find("Collection");
			Transform screen = collection == null ? null : collection.Find("CollectionScreen");
			Transform quit = buttons == null ? null : buttons.Find("Quit/QuitButtonContainer");
			Transform badge = screen == null ? null : screen.Find("SetsBadge");
			Transform header = screen == null ? null : screen.Find("Header");
			Transform backTab = screen == null ? null : screen.Find("BackTab");
			if (sections == null || quit == null || badge == null || header == null || backTab == null)
			{
				Mod.Log.LogError("The main menu does not have the parts the Multiplayer screen is copied from (Sections, Quit button, Collection header and badge). The game may have changed.");
				return null;
			}

			GameObject root = new GameObject("Multiplayer", typeof(RectTransform));
			root.transform.SetParent(sections, false);
			Ui.Stretch((RectTransform)root.transform, 0f);
			LobbyScreen self = root.AddComponent<LobbyScreen>();
			self.menu = owner;
			self.bigTemplate = quit.gameObject;
			Ui.StashRedButton(quit.gameObject);
			self.badgeTemplate = badge.gameObject;
			self.backTabTemplate = backTab.gameObject;
			self.Make(root.transform, header.gameObject);
			root.SetActive(false);
			Room.Changed += self.Refresh;
			return root;
		}

		private void OnDestroy()
		{
			Room.Changed -= Refresh;
		}

		// ------------------------------------------------------------------ building

		private void Make(Transform root, GameObject headerTemplate)
		{
			RectTransform background = Ui.Box("Background", root, Color.black);
			Ui.Stretch(background, 0f);
			GameObject header = Instantiate(headerTemplate, root, false);
			header.name = "Header";
			header.GetComponent<TMP_Text>().text = "MULTIPLAYER";
			subtitle = Ui.Label(root, "Race other players over Steam. Everybody needs the same mods.", 33f, Ui.Soft, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, 345f), new Vector2(1400f, 50f));

			home = (RectTransform)new GameObject("Home", typeof(RectTransform)).transform;
			home.SetParent(root, false);
			Ui.Stretch(home, 0f);
			inside = (RectTransform)new GameObject("Inside", typeof(RectTransform)).transform;
			inside.SetParent(root, false);
			Ui.Stretch(inside, 0f);

			BuildHome();
			BuildInside();

			message = Ui.Label(root, "", 30f, Ui.Amber, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, -495f), new Vector2(1400f, 40f));
			modsLine = Ui.Label(root, "", 24f, new Color(0.45f, 0.45f, 0.5f, 1f), TextAlignmentOptions.TopRight, new Vector2(1f, 1f), new Vector2(-30f, -24f), new Vector2(700f, 36f));
			GameObject tab = Instantiate(backTabTemplate, root, false);
			tab.name = "BackTab";
			Button tabButton = tab.GetComponentInChildren<Button>(true);
			tabButton.onClick = new Button.ButtonClickedEvent();
			tabButton.onClick.AddListener(() => Back());
			BuildFriends(root);
			BuildSettings(root, headerTemplate);
			BuildResults(root, headerTemplate);
			BuildChat(inside);
		}

		/// <summary>Three equal columns on one grid: GARAGE, HOST ROOM, JOIN ROOM (with its code box above it).</summary>
		private void BuildHome()
		{
			const float left = -520f, middle = 0f, right = 520f;
			soloButton = Big(home, "GARAGE", 8.5f, new Vector2(left, 20f), () => Garage.EnterSolo());
			Ui.Label(home, "Fit your car for races. Every item is yours here, free.", 28f, Ui.Soft, TextAlignmentOptions.Top, new Vector2(0.5f, 0.5f), new Vector2(left, -120f), new Vector2(440f, 110f));

			hostButton = Big(home, "HOST ROOM", 8.5f, new Vector2(middle, 20f), () => Room.Host());
			Ui.Label(home, "You get a room code to share, and can invite friends from Steam.", 28f, Ui.Soft, TextAlignmentOptions.Top, new Vector2(0.5f, 0.5f), new Vector2(middle, -120f), new Vector2(440f, 110f));

			codeField = Ui.Input(home, "ROOM CODE", new Vector2(0.5f, 0.5f), new Vector2(right, 150f), new Vector2(440f, 96f), 55f, 9);
			codeField.textComponent.alignment = TextAlignmentOptions.Center;
			((TMP_Text)codeField.placeholder).alignment = TextAlignmentOptions.Center;
			codeField.characterValidation = TMP_InputField.CharacterValidation.None;
			codeField.onValidateInput = (text, index, c) => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '\0';
			codeField.onSubmit.AddListener(s => { codeField.DeactivateInputField(); Room.JoinCode(s); });
			joinButton = Big(home, "JOIN ROOM", 8.5f, new Vector2(right, 20f), () => { codeField.DeactivateInputField(); Room.JoinCode(codeField.text); });
			Ui.Label(home, "Type or paste the code you were given.", 28f, Ui.Soft, TextAlignmentOptions.Top, new Vector2(0.5f, 0.5f), new Vector2(right, -120f), new Vector2(440f, 110f));
		}

		// One grid for the room: four buttons along the bottom (their outer edges are the list's edges), the code up top.
		private const float ButtonY = -340f;
		private static readonly float[] ButtonX = { -570f, -190f, 190f, 570f };
		private const float RowLeft = -428f; // the player bar's left edge; the chat ends at the right edge of LEAVE ROOM (-444)
		private const float GridLeft = -696f;
		private const float GridRight = 696f;

		/// <summary>The room: room code and invites on top, the players below, and the buttons along the bottom: LEAVE ROOM, GARAGE, RACE SETTINGS, START RACE.</summary>
		private void BuildInside()
		{
			Ui.Label(inside, "ROOM CODE", 33f, Ui.Soft, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, 495f), new Vector2(900f, 36f));
			codeText = Ui.Label(inside, "", 143f, Color.white, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, 375f), new Vector2(1400f, 150f));
			BuildLockIcon(inside, new Vector2(330f, 375f));
			Badge(inside, "COPY CODE", new Vector2(-220f, 285f), new Vector2(380f, 80f), () =>
			{
				GUIUtility.systemCopyBuffer = Room.Code;
				Room.Say("Room code copied. Send it to your friends.", 4f);
			});
			Badge(inside, "INVITE FRIENDS", new Vector2(220f, 285f), new Vector2(420f, 80f), () => ShowFriends(true));

			// the players: a bar of their own between the chat (left) and the host's buttons (right). Left to right in a bar: the connection,
			// the name, HOST / READY in the middle, the paint of the car, the tournament points.
			const float rowWidth = 858f; // to the right edge of INVITE FRIENDS (430)
			float rowCentre = RowLeft + rowWidth / 2f;
			roomLine = Ui.Label(inside, "", 28f, Ui.Soft, TextAlignmentOptions.MidlineLeft, new Vector2(0.5f, 0.5f), new Vector2(rowCentre, 205f), new Vector2(rowWidth, 36f));
			for (int i = 0; i < Room.MaxPlayers; i++)
			{
				int index = i;
				float y = 150f - i * 62f;
				RectTransform row = Ui.Box("Member " + i, inside, new Color(0.13f, 0.13f, 0.13f, 1f));
				Ui.Anchor(row, new Vector2(0.5f, 0.5f), new Vector2(rowCentre, y), new Vector2(rowWidth, 56f));
				memberRows[i] = row.gameObject;
				memberBars[i] = new Image[4];
				for (int k = 0; k < 4; k++)
				{
					float height = 10f + k * 8f;
					RectTransform bar = Ui.Box("Signal " + k, row, Color.gray);
					Ui.Anchor(bar, new Vector2(0f, 0f), new Vector2(14f + k * 11f, 11f), new Vector2(8f, height));
					memberBars[i][k] = bar.GetComponent<Image>();
					memberBars[i][k].raycastTarget = false;
				}
				memberPings[i] = Ui.Label(row, "", 28f, Ui.Soft, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0.5f), new Vector2(66f, 0f), new Vector2(90f, 52f));
				memberNames[i] = Ui.Label(row, "", 38f, Color.white, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0.5f), new Vector2(165f, 0f), new Vector2(170f, 52f));
				memberNames[i].enableAutoSizing = true;
				memberNames[i].fontSizeMin = 18f;
				memberNames[i].fontSizeMax = 38f;
				memberNames[i].textWrappingMode = TextWrappingModes.NoWrap;
				memberTags[i] = Ui.Label(row, "", 30f, Ui.Amber, TextAlignmentOptions.Center, new Vector2(0f, 0.5f), new Vector2(rowWidth / 2f - 110f, 0f), new Vector2(220f, 52f));
				memberPaint[i] = new Image[3];
				for (int k = 0; k < 3; k++)
				{
					RectTransform swatch = Ui.Box("Paint " + k, row, Color.white);
					Ui.Anchor(swatch, new Vector2(0f, 0.5f), new Vector2(580f + k * 36f, 0f), new Vector2(28f, 28f));
					memberPaint[i][k] = swatch.GetComponent<Image>();
					memberPaint[i][k].raycastTarget = false;
				}
				memberTotals[i] = Ui.Label(row, "", 30f, Ui.Soft, TextAlignmentOptions.MidlineRight, new Vector2(0f, 0.5f), new Vector2(690f, 0f), new Vector2(160f, 52f));
				// KICK and LOCK ROOM sit in line with the rows, after the bar, ending where START RACE ends
				kickButtons[i] = Badge(inside, "KICK", new Vector2(568f, y), new Vector2(256f, 56f), () => KickRow(index));
			}
			lockButton = Badge(inside, "LOCK ROOM", new Vector2(568f, 150f), new Vector2(256f, 56f), () => Room.SetLocked(!Room.Locked));

			// the buttons, left to right
			leaveButton = Big(inside, "LEAVE ROOM", 6.5f, new Vector2(ButtonX[0], ButtonY), () => Back());
			roomGarageButton = Big(inside, "GARAGE", 8.5f, new Vector2(ButtonX[1], ButtonY), () => Garage.EnterSolo());
			// everybody can look at the race settings (only the host can change them)
			settingsButton = Big(inside, "RACE SETTINGS", 6.5f, new Vector2(ButtonX[2], ButtonY), () => { showSettings = true; Refresh(); });
			readyButton = Big(inside, "READY", 7f, new Vector2(ButtonX[3], ButtonY), () => Garage.OnStartButton());
			startButton = Big(inside, "START RACE", 7f, new Vector2(ButtonX[3], ButtonY), () => Room.StartRace());
			startTip = Ui.Label(inside, "", 28f, Ui.Amber, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(ButtonX[3], ButtonY - 100f), new Vector2(520f, 40f));
			HoverTip.Attach(startButton, startTip, () => Room.AllReady ? "" : "Not all players are ready.");
			waitText = Ui.Label(inside, "", 28f, Ui.Soft, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(ButtonX[3], ButtonY - 100f), new Vector2(520f, 40f));
		}

		/// <summary>A small pixel padlock next to the room code, shown while the host has the room locked.</summary>
		private void BuildLockIcon(Transform parent, Vector2 at)
		{
			lockIcon = (RectTransform)new GameObject("Lock icon", typeof(RectTransform)).transform;
			lockIcon.SetParent(parent, false);
			Ui.Anchor(lockIcon, new Vector2(0.5f, 0.5f), at, new Vector2(120f, 120f));
			Color amber = Ui.Amber;
			Ui.Anchor(Ui.Box("Body", lockIcon, amber), new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(60f, 44f));
			Ui.Anchor(Ui.Box("Shackle left", lockIcon, amber), new Vector2(0.5f, 0.5f), new Vector2(-16f, 22f), new Vector2(10f, 30f));
			Ui.Anchor(Ui.Box("Shackle right", lockIcon, amber), new Vector2(0.5f, 0.5f), new Vector2(16f, 22f), new Vector2(10f, 30f));
			Ui.Anchor(Ui.Box("Shackle top", lockIcon, amber), new Vector2(0.5f, 0.5f), new Vector2(0f, 38f), new Vector2(42f, 10f));
			Ui.Anchor(Ui.Box("Keyhole", lockIcon, Color.black), new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(10f, 18f));
			lockIcon.gameObject.SetActive(false);
		}
		private void KickRow(int index)
		{
			if (!Room.IsHost || index >= Room.Peers.Count || Room.Peers[index].Local)
			{
				return;
			}
			Room.Kick(Room.Peers[index].Id, "You were removed from the room by the host.");
		}
		/// <summary>The host's race settings, one view of this screen, two columns on one grid.</summary>
		private void BuildSettings(Transform root, GameObject headerTemplate)
		{
			RectTransform view = Ui.Box("Settings", root, Color.black);
			Ui.Stretch(view, 0f);
			settingsView = view;
			GameObject header = Instantiate(headerTemplate, view, false);
			header.name = "Header";
			header.GetComponent<TMP_Text>().text = "RACE SETTINGS";
			Ui.Label(view, "RACE", 44f, Ui.Amber, TextAlignmentOptions.Left, new Vector2(0.5f, 0.5f), new Vector2(ColumnLeft0 + 355f, 300f), new Vector2(710f, 56f));
			Ui.Label(view, "DAMAGE TO PLAYERS", 44f, Ui.Amber, TextAlignmentOptions.Left, new Vector2(0.5f, 0.5f), new Vector2(ColumnLeft1 + 355f, 300f), new Vector2(710f, 56f));

			MapDefinition Map() { return Garage.FindMap(Room.Settings.Map); }
			AddRow(view, 0, "MAP", () => Room.Settings.Map == RaceSettings.RandomMap ? "RANDOM" : Garage.MapLabel(Map()), () => CycleMap(-1), () => CycleMap(1));
			AddRow(view, 0, "LAYOUT", () => VariantName(Room.Settings.Variant), () => CycleVariant(-1), () => CycleVariant(1), () => Garage.Variants(Map()).Count > 1);
			AddRow(view, 0, "MODE", () => RaceSettings.ModeName(Room.Settings.Mode), () => ChangeMode(), () => ChangeMode());
			AddRow(view, 0, "RACES", () => Room.Settings.Rounds.ToString(), () => Edit(s => s.Rounds = RaceSettings.Clamp(s.Rounds - 1, 2, 10)), () => Edit(s => s.Rounds = RaceSettings.Clamp(s.Rounds + 1, 2, 10)), () => Room.Settings.Mode == RaceSettings.ModeTournament);
			AddRow(view, 0, "LAPS", () => Room.Settings.Laps.ToString(), () => Edit(s => s.Laps = RaceSettings.Clamp(s.Laps - 1, 1, 10)), () => Edit(s => s.Laps = RaceSettings.Clamp(s.Laps + 1, 1, 10)), () => Room.Settings.RaceLike);
			AddRow(view, 0, "AI RIVALS", () => Room.Settings.Ai.ToString(), () => Edit(s => s.Ai = RaceSettings.Clamp(s.Ai - 1, 0, 6 - Room.Peers.Count)), () => Edit(s => s.Ai = RaceSettings.Clamp(s.Ai + 1, 0, 6 - Room.Peers.Count)));
			AddRow(view, 0, "WRECKED", () => Room.Settings.Mode == RaceSettings.ModeDemolition ? "OUT" : Room.Settings.Wreck == RaceSettings.WreckRespawn ? "RESPAWN" : "OUT, WATCH", () => Edit(s => s.Wreck = 1 - s.Wreck), () => Edit(s => s.Wreck = 1 - s.Wreck), () => Room.Settings.RaceLike);
			AddRow(view, 0, "RESPAWN IN", () => Room.Settings.RespawnSeconds.ToString("0") + " S", () => Edit(s => s.RespawnSeconds = RaceSettings.Clamp(s.RespawnSeconds - 1f, 2f, 30f)), () => Edit(s => s.RespawnSeconds = RaceSettings.Clamp(s.RespawnSeconds + 1f, 2f, 30f)), () => Room.Settings.RaceLike && Room.Settings.Wreck == RaceSettings.WreckRespawn);

			AddRow(view, 1, "GUNS", () => Percent(Room.Settings.Gun), () => Edit(s => s.Gun = Step(s.Gun, -0.1f)), () => Edit(s => s.Gun = Step(s.Gun, 0.1f)));
			AddRow(view, 1, "EXPLOSIONS", () => Percent(Room.Settings.Explosion), () => Edit(s => s.Explosion = Step(s.Explosion, -0.1f)), () => Edit(s => s.Explosion = Step(s.Explosion, 0.1f)));
			AddRow(view, 1, "ITEMS, MELEE", () => Percent(Room.Settings.Item), () => Edit(s => s.Item = Step(s.Item, -0.1f)), () => Edit(s => s.Item = Step(s.Item, 0.1f)));
			AddRow(view, 1, "RAMMING", () => Percent(Room.Settings.Ram), () => Edit(s => s.Ram = Step(s.Ram, -0.1f)), () => Edit(s => s.Ram = Step(s.Ram, 0.1f)));
			AddRow(view, 1, "AI GUNS", () => Percent(Room.Settings.AiGun), () => Edit(s => s.AiGun = Step(s.AiGun, -0.1f)), () => Edit(s => s.AiGun = Step(s.AiGun, 0.1f)));
			AddRow(view, 1, "TIRE SHOTS", () => Room.Settings.Tires ? "ON" : "OFF", () => Edit(s => s.Tires = !s.Tires), () => Edit(s => s.Tires = !s.Tires));

			// the example sits with the damage settings it explains
			revolverText = Ui.Label(view, "", 28f, Ui.Soft, TextAlignmentOptions.TopLeft, new Vector2(0.5f, 0.5f), new Vector2(ColumnLeft1 + 355f, -215f), new Vector2(710f, 90f));
			Big(view, "BACK", 8.5f, new Vector2(ButtonX[0], ButtonY), () => { showSettings = false; Refresh(); });
			view.gameObject.SetActive(false);
		}

		private void AddRow(Transform parent, int column, string label, Func<string> get, Action dec, Action inc, Func<bool> visible = null)
		{
			RectTransform row = (RectTransform)new GameObject("Row " + label, typeof(RectTransform)).transform;
			row.SetParent(parent, false);
			Ui.Anchor(row, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(710f, 60f));
			Ui.Label(row, label, 36f, Color.white, TextAlignmentOptions.MidlineLeft, new Vector2(0.5f, 0.5f), new Vector2(-205f, 0f), new Vector2(300f, 56f));
			Button decButton = Arrow(row, "<", new Vector2(-15f, 0f), () => dec());
			TMP_Text value = Ui.Label(row, "", 36f, Ui.Amber, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(155f, 0f), new Vector2(240f, 56f));
			Button incButton = Arrow(row, ">", new Vector2(325f, 0f), () => inc());
			settingRows.Add(new SettingRow { Root = row, Value = value, Get = get, Visible = visible, Column = column, Arrows = new[] { decButton, incButton } });
		}

		private static string Percent(float v) { return Mathf.RoundToInt(v * 100f) + "%"; }

		private static float Step(float v, float d) { return Mathf.Clamp(Mathf.Round((v + d) * 10f) / 10f, 0f, 3f); }

		private static string VariantName(int v) { return ((TrackVariant)v).ToString().ToUpperInvariant(); }

		/// <summary>Host-only: change a setting and tell everybody.</summary>
		private static void Edit(Action<RaceSettings> change)
		{
			if (!Room.IsHost)
			{
				return;
			}
			change(Room.Settings);
			Room.BroadcastRoster();
		}

		private static void CycleMap(int d)
		{
			List<MapDefinition> maps = Garage.Maps();
			if (maps.Count == 0)
			{
				return;
			}
			// RANDOM is the first stop in the list, before the real maps
			List<string> names = new List<string> { RaceSettings.RandomMap };
			names.AddRange(maps.Select(m => m.name));
			int at = Math.Max(0, names.IndexOf(Room.Settings.Map));
			string next = names[(at + d + names.Count) % names.Count];
			Edit(s =>
			{
				s.Map = next;
				s.Variant = 0;
			});
		}

		private static void CycleVariant(int d)
		{
			List<int> variants = Garage.Variants(Garage.FindMap(Room.Settings.Map));
			int at = Math.Max(0, variants.IndexOf(Room.Settings.Variant));
			int next = variants[(at + d + variants.Count) % variants.Count];
			Edit(s => s.Variant = next);
		}

		private static void ChangeMode()
		{
			// RACE -> TOURNAMENT -> DEMOLITION -> RACE, the same order either arrow
			Edit(s => s.Mode = s.Mode == RaceSettings.ModeRace ? RaceSettings.ModeTournament : s.Mode == RaceSettings.ModeTournament ? RaceSettings.ModeDemolition : RaceSettings.ModeRace);
		}

		/// <summary>The main-menu style red button: a copy of QUIT with its own text and action.</summary>
		private Button Big(Transform parent, string text, float fontSize, Vector2 position, UnityAction click)
		{
			RectTransform slot = (RectTransform)new GameObject("Slot " + text, typeof(RectTransform)).transform;
			slot.SetParent(parent, false);
			Ui.Anchor(slot, new Vector2(0.5f, 0.5f), position, new Vector2(252f, 136f));
			GameObject copy = Instantiate(bigTemplate, slot, false);
			copy.name = text + " Container";
			Button button = copy.GetComponentInChildren<Button>(true);
			button.onClick = new Button.ButtonClickedEvent();
			button.onClick.AddListener(click);
			TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
			label.text = text;
			label.enableAutoSizing = false;
			label.fontSize = fontSize;
			return button;
		}

		/// <summary>The menu's red button at a smaller size, for the arrows of the settings.</summary>
		private Button Arrow(Transform parent, string text, Vector2 position, UnityAction click)
		{
			const float scale = 1.5f; // the container is 63 x 34 units, drawn at scale 4 in the menu
			RectTransform slot = (RectTransform)new GameObject("Slot " + text, typeof(RectTransform)).transform;
			slot.SetParent(parent, false);
			Ui.Anchor(slot, new Vector2(0.5f, 0.5f), position, new Vector2(63f * scale, 34f * scale));
			GameObject copy = Instantiate(bigTemplate, slot, false);
			copy.name = text + " Arrow";
			RectTransform rect = (RectTransform)copy.transform;
			rect.localScale = new Vector3(scale, scale, 1f);
			rect.anchoredPosition = new Vector2(-31.5f * scale, -17f * scale);
			Button button = copy.GetComponentInChildren<Button>(true);
			button.onClick = new Button.ButtonClickedEvent();
			button.onClick.AddListener(click);
			TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
			label.text = text;
			label.enableAutoSizing = false;
			label.fontSize = 22f;
			return button;
		}

		/// <summary>The small CounterBadge button of the Collection screen, any size.</summary>
		private Button Badge(Transform parent, string text, Vector2 position, Vector2 size, UnityAction click)
		{
			RectTransform slot = (RectTransform)new GameObject("Slot " + text, typeof(RectTransform)).transform;
			slot.SetParent(parent, false);
			Ui.Anchor(slot, new Vector2(0.5f, 0.5f), position, size);
			GameObject copy = Instantiate(badgeTemplate, slot, false);
			copy.name = text;
			RectTransform rect = (RectTransform)copy.transform;
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.anchoredPosition = Vector2.zero;
			rect.sizeDelta = size;
			Button button = copy.GetComponent<Button>();
			button.onClick = new Button.ButtonClickedEvent();
			button.onClick.AddListener(click);
			ButtonDepth depth = copy.GetComponent<ButtonDepth>();
			if (depth != null)
			{
				Traverse.Create(depth).Field("baseY").SetValue(0f);
			}
			TMP_Text label = copy.GetComponentInChildren<TMP_Text>(true);
			label.text = text;
			label.fontSize = 33f;
			RectTransform labelRect = (RectTransform)label.transform;
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
			return button;
		}

		private static readonly Color Orange = new Color(1f, 0.55f, 0f, 1f);
		private static readonly float[] ResultX = { -700f, -470f, -80f, 170f, 380f, 510f, 660f, 830f };

		private static float CellWidth(int column)
		{
			return column == 0 ? 80f : column == 1 ? 380f : column == 2 ? 200f : column == 3 ? 220f : column == 6 ? 170f : column == 7 ? 150f : 100f;
		}

		/// <summary>After a race: one row per player with best lap, total time, kills (KOs) and wrecks (WOs).</summary>
		private void BuildResults(Transform root, GameObject headerTemplate)
		{
			RectTransform view = Ui.Box("Results", root, Color.black);
			Ui.Stretch(view, 0f);
			resultsView = view;
			GameObject header = Instantiate(headerTemplate, view, false);
			header.name = "Header";
			header.GetComponent<TMP_Text>().text = "RACE RESULTS";
			string[] titles = { "", "PLAYER", "BEST LAP", "TOTAL TIME", "KOS", "WOS", "SCORE", "TOURNAMENT" };
			for (int c = 1; c < titles.Length; c++)
			{
				TextAlignmentOptions align = c == 1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
				TMP_Text title = Ui.Label(view, titles[c], 33f, Ui.Amber, align, new Vector2(0.5f, 0.5f), new Vector2(ResultX[c], 285f), new Vector2(CellWidth(c), 44f));
				if (c == 7)
				{
					totalTitle = title;
				}
			}
			resultsInfo = Ui.Label(view, "", 33f, Ui.Soft, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, 340f), new Vector2(1400f, 44f));
			for (int i = 0; i < Room.MaxPlayers; i++)
			{
				float y = 215f - i * 66f;
				RectTransform row = Ui.Box("Result " + i, view, new Color(0.13f, 0.13f, 0.13f, 1f));
				Ui.Anchor(row, new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(1500f, 58f));
				resultRows[i] = row.gameObject;
				resultCells[i] = new TMP_Text[ResultX.Length];
				for (int c = 0; c < ResultX.Length; c++)
				{
					TextAlignmentOptions align = c == 1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
					resultCells[i][c] = Ui.Label(row, "", 38f, Orange, align, new Vector2(0.5f, 0.5f), new Vector2(ResultX[c], 0f), new Vector2(CellWidth(c), 54f));
				}
			}
			Big(view, "LEAVE ROOM", 6.5f, new Vector2(ButtonX[0], ButtonY), () => Back());
			backToLobbyButton = Big(view, "BACK TO LOBBY", 6.5f, new Vector2(ButtonX[3], ButtonY), () => Room.BackToLobby());
			resultsWait = Ui.Label(view, "The host takes everybody back to the lobby.", 28f, Ui.Soft, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(ButtonX[3] - 80f, ButtonY - 100f), new Vector2(700f, 40f));
			view.gameObject.SetActive(false);
		}

		private static string Clock(float seconds)
		{
			if (seconds <= 0f)
			{
				return "-";
			}
			int minutes = (int)(seconds / 60f);
			return minutes + ":" + (seconds - minutes * 60f).ToString("00.000");
		}

		private void RefreshResults()
		{
			bool tournament = Room.Race != null && Room.Race.Mode == RaceSettings.ModeTournament;
			totalTitle.gameObject.SetActive(tournament);
			Peer leader = Room.SeriesLeader();
			resultsInfo.text = !tournament ? "" : Room.SeriesOver && leader != null ? Tx.F("TOURNAMENT OVER: {0} WINS WITH {1}", leader.Name.ToUpperInvariant(), Mathf.RoundToInt(leader.Total)) : Tx.F("RACE {0} OF {1}", Math.Min(Room.Round + (Room.Phase == Phase.Results ? 0 : 1), Room.SeriesRounds), Room.SeriesRounds);
			for (int i = 0; i < Room.MaxPlayers; i++)
			{
				bool has = i < Room.Results.Count && i < Room.MaxPlayers;
				resultRows[i].SetActive(has);
				if (!has)
				{
					continue;
				}
				ResultLine line = Room.Results[i];
				resultCells[i][0].text = (i + 1) + ".";
				resultCells[i][1].text = line.Name.ToUpperInvariant();
				resultCells[i][2].text = Clock(line.BestLap);
				resultCells[i][3].text = line.State == 0 ? Clock(line.Time) : (line.State == 2 ? "WRECKED" : (Room.Phase == Phase.Racing ? "RACING" : "DNF"));
				resultCells[i][4].text = line.Kos.ToString();
				resultCells[i][5].text = line.Wos.ToString();
				// the score follows the single-player rule (place payout, survival, kills, pick-ups); humans are orange, AI white
				resultCells[i][6].text = Mathf.RoundToInt(line.Score).ToString();
				// the tournament column: what the player had before this race plus its score (the race is already added once it is over)
				Peer owner = line.Ai ? null : Room.Find(line.Id);
				resultCells[i][7].text = tournament && owner != null ? Mathf.RoundToInt(owner.Total + (Room.Phase == Phase.Results ? 0f : line.Score)).ToString() : "";
				resultRows[i].GetComponent<RectTransform>().sizeDelta = new Vector2(tournament ? 1800f : 1500f, 58f);
				for (int c = 0; c < resultCells[i].Length; c++)
				{
					resultCells[i][c].color = line.Ai ? Color.white : Orange;
				}
			}
			bool over = Room.Phase == Phase.Results;
			Holder(backToLobbyButton).SetActive(Room.IsHost && over);
			resultsWait.gameObject.SetActive(!(Room.IsHost && over));
			resultsWait.text = !over ? "The race is still going. The results fill in as cars finish." : "The host takes everybody back to the lobby.";
		}
		// ------------------------------------------------------------------ friends

		private const int FriendRows = 8;

		private GameObject friendPanel;
		private TMP_Text friendEmpty;
		private float friendsAt;
		private List<FriendInfo> friendList = new List<FriendInfo>();
		private readonly HashSet<ulong> invited = new HashSet<ulong>();
		private readonly TMP_Text[] friendNames = new TMP_Text[FriendRows];
		private readonly Button[] friendButtons = new Button[FriendRows];

		/// <summary>The in-game list of online Steam friends, one INVITE button each.</summary>
		private void BuildFriends(Transform root)
		{
			RectTransform bg = Ui.Box("Friends", root, Color.black);
			Ui.Stretch(bg, 0f);
			friendPanel = bg.gameObject;
			Ui.Label(bg, "INVITE FRIENDS", 66f, Color.white, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, 345f), new Vector2(1200f, 80f));
			friendEmpty = Ui.Label(bg, "No friends are online on Steam.", 38f, Ui.Soft, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1200f, 60f));
			for (int i = 0; i < FriendRows; i++)
			{
				int index = i;
				float y = 240f - i * 66f;
				friendNames[i] = Ui.Label(bg, "", 38f, Color.white, TextAlignmentOptions.MidlineLeft, new Vector2(0.5f, 0.5f), new Vector2(-250f, y), new Vector2(620f, 54f));
				friendButtons[i] = Badge(bg, "INVITE", new Vector2(330f, y), new Vector2(300f, 56f), () => InviteRow(index));
			}
			Badge(bg, "BACK", new Vector2(0f, -330f), new Vector2(380f, 80f), () => ShowFriends(false));
			friendPanel.SetActive(false);
		}

		private void ShowFriends(bool show)
		{
			friendPanel.SetActive(show);
			friendsAt = 0f;
			if (show)
			{
				invited.Clear();
			}
		}

		private void InviteRow(int index)
		{
			if (index >= friendList.Count || Room.B == null)
			{
				return;
			}
			FriendInfo friend = friendList[index];
			Room.B.InviteFriend(friend.Id);
			invited.Add(friend.Id);
			Room.Say("Invited " + friend.Name + ".", 4f);
		}

		private void RefreshFriends()
		{
			if (Room.B != null && Time.unscaledTime >= friendsAt)
			{
				friendsAt = Time.unscaledTime + 3f;
				friendList = Room.B.Friends();
			}
			friendEmpty.gameObject.SetActive(friendList.Count == 0);
			for (int i = 0; i < FriendRows; i++)
			{
				bool has = i < friendList.Count;
				friendNames[i].gameObject.SetActive(has);
				Holder(friendButtons[i]).SetActive(has);
				if (has)
				{
					FriendInfo f = friendList[i];
					friendNames[i].text = f.Name + (f.Playing ? "  <color=#F2B833>IN GAME</color>" : "");
					Ui.SetText(friendButtons[i], invited.Contains(f.Id) ? "INVITED" : "INVITE");
				}
			}
		}

		// ------------------------------------------------------------------ chat

		private int chatEscapeFrame = -1;
		private int chatOpenedFrame = -100;

		/// <summary>
		/// The lobby's chat: always open, the column under MULTIPLAYER down to the LEAVE ROOM button. The history above a line to type; the chat
		/// key puts the cursor in the line, Enter sends, Escape lets go.
		/// </summary>
		private void BuildChat(Transform parent)
		{
			// no box of its own: the text sits on the menu's black like the rest, from the left edge to the right edge of LEAVE ROOM
			chatBox = Ui.Box("ChatBox", parent, new Color(0f, 0f, 0f, 0f));
			Ui.Anchor(chatBox, new Vector2(0.5f, 0.5f), new Vector2(-687f, 65f), new Vector2(486f, 640f));
			Ui.Label(chatBox, "CHAT", 28f, Ui.Soft, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(6f, -4f), new Vector2(300f, 34f));

			RectTransform view = Ui.Box("Log", chatBox, new Color(0f, 0f, 0f, 0.004f)); // nearly clear, but it takes the mouse wheel
			view.gameObject.AddComponent<RectMask2D>();
			Ui.Anchor(view, new Vector2(0f, 0f), new Vector2(6f, 84f), new Vector2(452f, 510f));
			chatLog = Ui.Label(view, "", 28f, Color.white, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), Vector2.zero, new Vector2(452f, 510f));
			chatLog.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			RectTransform track = Ui.Box("Scrollbar", chatBox, new Color(1f, 1f, 1f, 0.06f));
			Ui.Anchor(track, new Vector2(0f, 0f), new Vector2(470f, 84f), new Vector2(8f, 510f));
			RectTransform handle = Ui.Box("Handle", track, new Color(1f, 1f, 1f, 0.35f));
			Ui.Stretch(handle, 0f);
			Scrollbar bar = track.gameObject.AddComponent<Scrollbar>();
			bar.handleRect = handle;
			bar.targetGraphic = handle.GetComponent<Image>();
			bar.direction = Scrollbar.Direction.BottomToTop;
			chatScroll = view.gameObject.AddComponent<ScrollRect>();
			chatScroll.content = chatLog.rectTransform;
			chatScroll.viewport = view;
			chatScroll.horizontal = false;
			chatScroll.vertical = true;
			chatScroll.movementType = ScrollRect.MovementType.Clamped;
			chatScroll.scrollSensitivity = 30f;
			chatScroll.verticalScrollbar = bar;
			chatScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

			chatField = Ui.Input(chatBox, "", new Vector2(0f, 0f), new Vector2(6f, 14f), new Vector2(472f, 58f), 30f, Chat.MaxLength);
			// the key that opened the line must not type itself into it
			chatField.onValidateInput = (text, index, c) => c == '<' || c == '>' || Time.frameCount <= chatOpenedFrame + 1 ? '\0' : c;
			chatField.onSubmit.AddListener(s => SendChat());
		}

		private void SendChat()
		{
			string text = chatField.text;
			chatField.text = "";
			Chat.Send(text);
			chatField.ActivateInputField();
		}

		/// <summary>Every frame in the room: the chat key, Escape, the history.</summary>
		private void TickChat()
		{
			if (!inside.gameObject.activeInHierarchy || !Room.Active || Room.Phase != Phase.Lobby)
			{
				if (chatField.isFocused)
				{
					chatField.DeactivateInputField(); // the room view is going away (race starting, leaving): let go of the line and the controls
				}
				return;
			}
			TMP_Text hint = (TMP_Text)chatField.placeholder;
			string want = Tx.F("Press {0} to chat", Mod.ChatKeyName());
			if (hint.text != want)
			{
				hint.text = want;
			}
			bool focused = chatField.isFocused;
			if (focused && Input.GetKeyDown(KeyCode.Escape))
			{
				chatEscapeFrame = Time.frameCount;
				chatField.DeactivateInputField();
			}
			else if (!focused && Mod.ChatKey())
			{
				chatOpenedFrame = Time.frameCount;
				chatField.text = "";
				chatField.ActivateInputField();
			}
			string block = Chat.Block(60, false);
			if (chatLog.text != block)
			{
				// follow the newest line unless the player has scrolled up to read
				bool atBottom = chatScroll.verticalNormalizedPosition < 0.05f || chatLog.text.Length == 0;
				chatLog.text = block;
				if (atBottom)
				{
					Canvas.ForceUpdateCanvases();
					chatScroll.verticalNormalizedPosition = 0f;
				}
			}
			Chat.Unread = 0;
		}

		private static GameObject Holder(Button b)
		{
			Transform t = b.transform;
			while (t.parent != null && !t.name.StartsWith("Slot "))
			{
				t = t.parent;
			}
			return t.gameObject;
		}

		private void Back()
		{
			if (Time.frameCount == chatEscapeFrame)
			{
				return; // Escape on the chat line closes the chat, not the room
			}
			if (friendPanel.activeSelf)
			{
				ShowFriends(false);
				return;
			}
			if (showSettings)
			{
				showSettings = false;
				Refresh();
				return;
			}
			if (Room.Active)
			{
				// back to the MULTIPLAYER screen (GARAGE / HOST ROOM / JOIN ROOM), not all the way to the main menu
				Room.Leave("You left the room.");
				return;
			}
			menu.CloseMultiplayer();
		}

		// ------------------------------------------------------------------ refresh

		private void OnEnable()
		{
			showSettings = false;
			Refresh();
		}

		private void Update()
		{
			if (chatBox != null)
			{
				TickChat();
			}
			Refresh();
		}

		private void Refresh()
		{
			if (home == null)
			{
				return;
			}
			bool inRoom = Room.Active;
			if (!inRoom || Room.Phase != Phase.Lobby)
			{
				showSettings = false;
			}
			home.gameObject.SetActive(!inRoom);
			bool results = inRoom && (Room.Phase == Phase.Results || Room.ViewingResults);
			inside.gameObject.SetActive(inRoom && !showSettings && !results);
			settingsView.gameObject.SetActive(showSettings);
			resultsView.gameObject.SetActive(results);
			bool idle = Room.Busy.Length == 0;
			hostButton.interactable = idle;
			joinButton.interactable = idle;
			soloButton.interactable = idle;
			message.text = Room.Busy.Length > 0 ? Room.Busy : Room.Notice;
			message.color = Room.Busy.Length > 0 ? Ui.Soft : Ui.Amber;
			subtitle.gameObject.SetActive(!inRoom);
			if (Checksums.Mods.Count > 0)
			{
				modsLine.text = "checksum " + Checksums.Combined.Substring(0, 8);
			}
			if (!inRoom)
			{
				friendPanel.SetActive(false);
				return;
			}
			if (friendPanel.activeSelf)
			{
				RefreshFriends();
			}
			if (results)
			{
				RefreshResults();
				return;
			}
			if (showSettings)
			{
				RefreshSettings();
				return;
			}
			string code = Room.Code ?? "";
			codeText.text = code.Length == 6 ? code.Substring(0, 3) + " " + code.Substring(3) : code;
			for (int i = 0; i < Room.MaxPlayers; i++)
			{
				bool has = i < Room.Peers.Count;
				memberRows[i].SetActive(has);
				Holder(kickButtons[i]).SetActive(has && Room.IsHost && !Room.Peers[i].Local);
				if (has)
				{
					Peer p = Room.Peers[i];
					memberNames[i].text = p.Name.ToUpperInvariant() + (p.Local ? Tx.T(" (YOU)") : "");
					Color[] paint = Skins.Colours(p.Skin);
					for (int k = 0; k < 3; k++)
					{
						memberPaint[i][k].gameObject.SetActive(paint != null);
						if (paint != null)
						{
							memberPaint[i][k].color = paint[k];
						}
					}
					bool series = Room.Settings.Mode == RaceSettings.ModeTournament && Room.SeriesRounds > 0 && Room.SeriesRounds == Room.Settings.Rounds;
					memberTotals[i].text = series ? Tx.F("{0} PTS", Mathf.RoundToInt(p.Total)) : "";
					// the ping each player has to the host; the host is its own end of the line
					int ms = p.Id == Room.HostId ? 0 : p.PingMs;
					int lit = ms < 0 ? 0 : ms < 50 ? 4 : ms < 100 ? 3 : ms < 170 ? 2 : 1;
					Color tone = lit >= 3 ? Ui.Green : lit == 2 ? Ui.Amber : Ui.Red;
					for (int k = 0; k < 4; k++)
					{
						memberBars[i][k].color = k < lit ? tone : new Color(0.28f, 0.28f, 0.3f, 1f);
					}
					memberPings[i].text = ms < 0 ? "-" : ms + " ms";
					memberPings[i].color = ms < 0 ? Ui.Soft : tone;
					if (p.Id == Room.HostId)
					{
						memberTags[i].text = "HOST";
						memberTags[i].color = Ui.Amber;
					}
					else
					{
						memberTags[i].text = p.Ready ? "READY" : "NOT READY";
						memberTags[i].color = p.Ready ? Ui.Green : Ui.Red;
					}
				}
			}
			string players = Tx.F("PLAYERS {0} / {1}", Room.Peers.Count, Room.MaxPlayers);
			string seriesLine = Room.SeriesText();
			roomLine.text = seriesLine.Length > 0 ? players + "     " + seriesLine : players;
			lockIcon.gameObject.SetActive(Room.Locked);
			Holder(lockButton).SetActive(Room.IsHost);
			Ui.SetText(lockButton, Room.Locked ? "UNLOCK ROOM" : "LOCK ROOM");
			Peer me = Room.Me;
			bool lobby = Room.Phase == Phase.Lobby;
			Holder(readyButton).SetActive(!Room.IsHost);
			readyButton.interactable = lobby && me != null;
			Ui.SetText(readyButton, me != null && me.Ready ? "NOT READY" : "READY");
			Holder(startButton).SetActive(Room.IsHost);
			bool canStart = lobby && Room.AllReady && (Room.Settings.Map == RaceSettings.RandomMap || Garage.FindMap(Room.Settings.Map) != null);
			startButton.interactable = canStart;
			startButton.targetGraphic.color = canStart ? Color.white : new Color(0.45f, 0.45f, 0.45f, 1f);
			settingsButton.interactable = lobby;
			roomGarageButton.interactable = lobby;
			waitText.text = Room.IsHost ? "" : (me != null && me.Ready ? "Waiting for the host to start." : "Press READY when your car is set.");
		}

		private void RefreshSettings()
		{
			int[] used = new int[2];
			foreach (SettingRow row in settingRows)
			{
				bool show = row.Visible == null || row.Visible();
				row.Root.gameObject.SetActive(show);
				row.Value.text = row.Get();
				foreach (Button arrow in row.Arrows)
				{
					arrow.interactable = Room.IsHost; // players may look, only the host changes
				}
				if (show)
				{
					float left = row.Column == 0 ? ColumnLeft0 : ColumnLeft1;
					row.Root.anchoredPosition = new Vector2(left + 355f, RowTop - used[row.Column] * RowStep);
					used[row.Column]++;
				}
			}
			revolverText.text = "EXAMPLE\n" + Pvp.RevolverInfo(Room.Settings);
		}
	}
}
