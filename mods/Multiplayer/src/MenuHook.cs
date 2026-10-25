using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Multiplayer
{
	/// <summary>
	/// Turns on the main menu's greyed-out MULTIPLAYER button and gives it a screen. The game already has the rest:
	/// <c>MainMenu.ShowMultiplayer()</c> slides in whatever object is assigned to <c>MainMenu.MultiplayerSection</c>.
	/// The button is locked by a <c>DemoLockedButton</c> (it shows "Not in demo" on hover), which is switched off here.
	/// </summary>
	internal static class MenuHook
	{
		private const string ButtonPath = "MAIN_MENU/MenuUI/SidePanel/Buttons/Multiplayer/MultiplayerButtonContainer/ButtonMultiplayer";

		private struct GroupState
		{
			internal CanvasGroup Group;
			internal float Alpha;
			internal bool Interactable;
			internal bool BlocksRaycasts;
		}

		private static MainMenu menu;
		private static Button button;
		private static bool wasInteractable;
		private static UnityAction clicked;
		private static GameObject section;
		private static DemoLockedButton demoLock;
		private static bool demoLockWasEnabled;
		private static bool fromCommandLineDone;
		private static readonly List<GroupState> groups = new List<GroupState>();

		/// <summary>Called every frame: puts the screen into a main menu that has just appeared (it may have been there before the mod loaded).</summary>
		/// <summary>False from the moment a scene change to the main menu starts until the MULTIPLAYER screen is up (or no screen is wanted).</summary>
		internal static bool ScreenReady = true;

		private static int settleUntil;

		/// <summary>The menu's own start-up can undo the first frame's work, so the screen is held in place for a few frames before the cover goes.</summary>
		private static void Settle()
		{
			if (settleUntil == 0 || menu == null)
			{
				return;
			}
			if (Time.frameCount <= settleUntil)
			{
				ShowAtOnce(menu);
			}
			else
			{
				settleUntil = 0;
				ScreenReady = true;
			}
		}

		internal static void Tick()
		{
			MainMenu found = Object.FindFirstObjectByType<MainMenu>();
			if (found == menu)
			{
				Settle();
				return;
			}
			Remove();
			if (found != null)
			{
				Install(found);
			}
		}

		private static void Install(MainMenu found)
		{
			menu = found;
			button = FindButton();
			if (button == null)
			{
				Mod.Log.LogError("The MULTIPLAYER button was not found at " + ButtonPath + " and nowhere else by name. The game may have changed.");
				return;
			}
			Describe(button);
			section = LobbyScreen.Build(menu);
			if (section == null)
			{
				return;
			}
			menu.MultiplayerSection = section;
			Enable(button);
			Mod.Log.LogInfo("MULTIPLAYER button is on.");
			if (Room.Active || Garage.ReturnToScreen)
			{
				Garage.ReturnToScreen = false;
				ShowAtOnce(menu);
				settleUntil = Time.frameCount + 8;
			}
			else
			{
				ScreenReady = true; // no screen wanted: the cover, if any, can go
			}
			if (!fromCommandLineDone)
			{
				fromCommandLineDone = true;
				Room.JoinFromCommandLine();
			}
		}

		/// <summary>Opens the MULTIPLAYER screen without the sliding animation, so coming back from the garage does not show the main menu first.</summary>
		private static void ShowAtOnce(MainMenu target)
		{
			if (Traverse.Create(target).Field("view").GetValue<MainMenu.View>() != MainMenu.View.Multiplayer)
			{
				target.ShowMultiplayer();
			}
			Animator animator = Traverse.Create(target).Field("animator").GetValue<Animator>();
			if (animator != null)
			{
				animator.Play("Pull", 1, 1f);
				animator.Update(0f);
			}
		}

		/// <summary>Puts the menu back as the game made it. Runs when mods switch off and before each rebuild.</summary>
		internal static void Remove()
		{
			if (button != null)
			{
				if (clicked != null)
				{
					button.onClick.RemoveListener(clicked);
				}
				button.interactable = wasInteractable;
			}
			if (demoLock != null)
			{
				Traverse.Create(demoLock).Field("locked").SetValue(true);
				demoLock.enabled = demoLockWasEnabled;
			}
			demoLock = null;
			foreach (GroupState state in groups)
			{
				if (state.Group != null)
				{
					state.Group.alpha = state.Alpha;
					state.Group.interactable = state.Interactable;
					state.Group.blocksRaycasts = state.BlocksRaycasts;
				}
			}
			groups.Clear();
			if (menu != null && menu.MultiplayerSection == section)
			{
				menu.MultiplayerSection = null;
			}
			if (section != null)
			{
				Object.Destroy(section);
			}
			menu = null;
			button = null;
			clicked = null;
			section = null;
		}

		private static Button FindButton()
		{
			GameObject go = GameObject.Find(ButtonPath);
			if (go != null && go.GetComponent<Button>() != null)
			{
				return go.GetComponent<Button>();
			}
			return Resources.FindObjectsOfTypeAll<Button>().FirstOrDefault(b => b != null && b.gameObject.scene.IsValid() && b.name == "ButtonMultiplayer");
		}

		private static void Enable(Button target)
		{
			wasInteractable = target.interactable;
			// "Not in demo": DemoLockedButton locks the button in Awake and swaps its label on hover. Switch it off and put the label back.
			demoLock = target.GetComponentInParent<DemoLockedButton>(true);
			if (demoLock == null && target.transform.parent != null)
			{
				demoLock = target.transform.parent.GetComponentInChildren<DemoLockedButton>(true);
			}
			if (demoLock != null)
			{
				demoLockWasEnabled = demoLock.enabled;
				Traverse lockTraverse = Traverse.Create(demoLock);
				string original = lockTraverse.Field("unlockedText").GetValue<string>();
				if (demoLock.label != null && !string.IsNullOrEmpty(original))
				{
					demoLock.label.text = original;
				}
				lockTraverse.Field("locked").SetValue(false);
				demoLock.enabled = false;
			}
			target.interactable = true;
			// Something above the button may be what greys it; undo that too, remembering how it was.
			foreach (CanvasGroup group in target.GetComponentsInParent<CanvasGroup>(true))
			{
				groups.Add(new GroupState { Group = group, Alpha = group.alpha, Interactable = group.interactable, BlocksRaycasts = group.blocksRaycasts });
				group.alpha = 1f;
				group.interactable = true;
				group.blocksRaycasts = true;
			}
			clicked = menu.ShowMultiplayer;
			target.onClick.AddListener(clicked);
		}

		/// <summary>Logs what the button is made of, so a change in the game shows up in the log.</summary>
		private static void Describe(Button target)
		{
			StringBuilder text = new StringBuilder("MULTIPLAYER button ").Append(Rig.PathOf(target.transform)).Append(": interactable=").Append(target.interactable).Append(", components=");
			text.Append(string.Join(",", target.GetComponents<Component>().Select(c => c == null ? "(missing)" : c.GetType().Name).ToArray()));
			int count = target.onClick.GetPersistentEventCount();
			for (int i = 0; i < count; i++)
			{
				text.Append("; onClick[").Append(i).Append("]=").Append(target.onClick.GetPersistentTarget(i)).Append('.').Append(target.onClick.GetPersistentMethodName(i));
			}
			Mod.Log.LogInfo(text.ToString());
		}
	}
}
