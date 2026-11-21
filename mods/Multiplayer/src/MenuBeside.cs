using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer
{
#if HALFGRIP_OFFICIAL_NET
	/// <summary>
	/// For game builds that have their own MULTIPLAYER (a co-op run lobby): that button and screen are left exactly as the game made them, and this
	/// mod's racing rooms get a button of their own, RACE ROOMS, under it. Both screens use the menu's one MultiplayerSection slot, so this
	/// mod's screen takes the slot only while it is up and gives it back as soon as the menu leaves the screen. On older builds (no such
	/// lobby) <see cref="MenuHook"/> turns the original MULTIPLAYER button into this mod's screen instead and none of this is compiled.
	/// </summary>
	internal static class MenuBeside
	{
		private const string ButtonsPath = "MAIN_MENU/MenuUI/SidePanel/Buttons";

		private static MainMenu menu;
		private static GameObject entry;
		private static GameObject section;
		private static GameObject theirs;
		private static Transform buttons;
		private static Vector3 buttonsScale;
		private static float hideAt;

		internal static bool Installed { get { return entry != null; } }

		internal static void Install(MainMenu found)
		{
			menu = found;
			GameObject buttonsObject = GameObject.Find(ButtonsPath);
			Transform original = buttonsObject == null ? null : buttonsObject.transform.Find("Multiplayer");
			Transform container = original == null ? null : original.Find("MultiplayerButtonContainer");
			Transform face = container == null ? null : container.Find("ButtonMultiplayer");
			Transform text = face == null ? null : face.Find("TextMultiplayer");
			if (text == null || face.GetComponent<Button>() == null)
			{
				Mod.Log.LogError("The main menu's MULTIPLAYER button is not where the RACE ROOMS button is copied from (" + ButtonsPath + "/Multiplayer). The game may have changed.");
				return;
			}
			buttons = buttonsObject.transform;
			buttonsScale = buttons.localScale;
			// A copy of the game's own MULTIPLAYER button, right under it. The menu's VerticalLayoutGroup places it.
			entry = Object.Instantiate(original.gameObject, buttons, false);
			entry.name = "RaceRooms";
			entry.transform.SetSiblingIndex(original.GetSiblingIndex() + 1);
			Transform copyContainer = entry.transform.Find("MultiplayerButtonContainer");
			copyContainer.name = "RaceRoomsButtonContainer";
			Transform copyFace = copyContainer.Find("ButtonMultiplayer");
			copyFace.name = "ButtonRaceRooms";
			Transform copyText = copyFace.Find("TextMultiplayer");
			copyText.name = "TextRaceRooms";
			// The copy must not share the original's lock or its click handlers.
			foreach (DemoLockedButton locked in entry.GetComponentsInChildren<DemoLockedButton>(true))
			{
				Object.DestroyImmediate(locked);
			}
			foreach (CanvasGroup group in entry.GetComponentsInChildren<CanvasGroup>(true))
			{
				group.alpha = 1f;
				group.interactable = true;
				group.blocksRaycasts = true;
			}
			Button button = copyFace.GetComponent<Button>();
			button.interactable = true;
			button.onClick = new Button.ButtonClickedEvent();
			button.onClick.AddListener(Open);
			TMP_Text label = copyText.GetComponent<TMP_Text>();
			if (label != null)
			{
				label.text = "RACE ROOMS";
			}
			// Six buttons are taller than the panel the game made for five.
			buttons.localScale = buttonsScale * (5f / 6f);
			Mod.Log.LogInfo("RACE ROOMS button added under the game's MULTIPLAYER button.");
		}

		private static void Build()
		{
			if (section == null && menu != null)
			{
				section = LobbyScreen.Build(menu, "RaceRooms");
			}
		}

		/// <summary>Puts this mod's screen in the menu's slot (remembering the game's own screen there), ready for ShowMultiplayer.</summary>
		internal static void Use(MainMenu target)
		{
			if (menu == null)
			{
				menu = target;
			}
			Build();
			hideAt = 0f;
			if (section != null && target.MultiplayerSection != section)
			{
				// The menu only switches sections on the way in (MainMenu.ShowSection); on the way back to the main view it leaves the last one active.
				// So the game's own screen, left active from an earlier visit, is switched off here or it would show through ours.
				theirs = target.MultiplayerSection;
				if (theirs != null)
				{
					theirs.SetActive(false);
				}
				target.MultiplayerSection = section;
			}
		}

		private static void Open()
		{
			if (menu != null)
			{
				Use(menu);
				menu.ShowMultiplayer();
			}
		}

		/// <summary>True while this mod's screen is the one the menu shows in its Multiplayer view.</summary>
		internal static bool IsUp(MainMenu target)
		{
			return section != null && target.MultiplayerSection == section && Traverse.Create(target).Field("view").GetValue<MainMenu.View>() == MainMenu.View.Multiplayer;
		}

		/// <summary>Every frame: once the menu has left the Multiplayer view, the slot goes back to the game's own screen.</summary>
		internal static void Tick()
		{
			if (menu == null || section == null)
			{
				return;
			}
			if (menu.MultiplayerSection == section && Traverse.Create(menu).Field("view").GetValue<MainMenu.View>() != MainMenu.View.Multiplayer)
			{
				menu.MultiplayerSection = theirs;
				hideAt = Time.unscaledTime + 1.5f; // after the menu's slide-out, so the screen does not vanish while it moves
			}
			if (hideAt > 0f && Time.unscaledTime >= hideAt)
			{
				hideAt = 0f;
				if (menu.MultiplayerSection != section)
				{
					section.SetActive(false);
				}
			}
		}

		internal static void Remove()
		{
			if (menu != null && section != null && menu.MultiplayerSection == section)
			{
				menu.MultiplayerSection = theirs;
			}
			if (buttons != null)
			{
				buttons.localScale = buttonsScale;
			}
			if (entry != null)
			{
				Object.Destroy(entry);
			}
			if (section != null)
			{
				Object.Destroy(section);
			}
			menu = null;
			entry = null;
			section = null;
			theirs = null;
			buttons = null;
		}
	}
#else
	/// <summary>Not used on this game build (it has no lobby of its own): <see cref="MenuHook"/> takes over the MULTIPLAYER button.</summary>
	internal static class MenuBeside
	{
		internal static bool Installed { get { return false; } }
		internal static bool IsUp(MainMenu target) { return false; }
		internal static void Install(MainMenu found) { }
		internal static void Use(MainMenu target) { }
		internal static void Tick() { }
		internal static void Remove() { }
	}
#endif
}
