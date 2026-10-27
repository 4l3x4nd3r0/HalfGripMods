using HarmonyLib;
using TMPro;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// The pause screen inside a room. Time never stops (the other players keep racing), the car is let go of while the menu is open,
	/// there are no hearts, and leaving is a disconnect from the room, not a lost life.
	/// </summary>
	internal static class Pause
	{
		private static PauseController held;

		// Closing the menu with Escape un-mutes the controls in the same frame, and the game's own pause check runs after this mod's
		// Update: it would see the same Escape press, find the game unpaused, and open the menu again. So the mute is kept for the rest
		// of that frame and let go of on the next one.
		private static PauseController closing;

		private static int closingFrame;

		internal static bool Open { get { return (object)held != null; } }

		/// <summary>Gives the controls back. Safe to call at any time, also after the pause menu's scene is gone.</summary>
		internal static void Release()
		{
			if ((object)held != null)
			{
				GameInput.Mute(held, false); // works on a destroyed object too: the set compares references
			}
			held = null;
			EndClosing();
		}

		private static void EndClosing()
		{
			if ((object)closing != null)
			{
				GameInput.Mute(closing, false);
			}
			closing = null;
		}

		/// <summary>Muted input also blocks the game's own Escape check, so Escape closes the menu from here.</summary>
		internal static void Tick()
		{
			if ((object)closing != null && Time.frameCount != closingFrame)
			{
				EndClosing();
			}
			if (Chat.Typing)
			{
				return; // Escape belongs to the chat line while it is open
			}
			if ((object)held == null)
			{
				// a finished player's controls are let go of, which also blocks the game's own Escape: open the pause menu from here
				if ((RaceSync.FinishedLocal || Spectator.Active) && Input.GetKeyDown(KeyCode.Escape))
				{
					PauseController.PauseRace();
				}
				return;
			}
			if (!held)
			{
				Release(); // the race scene was left while the menu was open
				return;
			}
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				held.Resume();
			}
		}

		internal static void Hold(PauseController pause, bool paused)
		{
			if (paused)
			{
				EndClosing(); // a menu opened again in the frame after it was closed
				held = pause;
				GameInput.Mute(pause, true);
			}
			else if ((object)held != null)
			{
				// closed: the controls stay muted until the next frame (see above)
				EndClosing();
				closing = held;
				closingFrame = Time.frameCount;
				held = null;
			}
			else
			{
				Release();
			}
		}

		internal static void Strip(PauseController pause)
		{
			Transform panel = Traverse.Create(pause).Field("pausePanel").GetValue<GameObject>().transform;
			LivesDisplay lives = pause.GetComponentInChildren<LivesDisplay>(true);
			if (lives == null)
			{
				lives = Object.FindFirstObjectByType<LivesDisplay>(FindObjectsInactive.Include);
			}
			if (lives == null)
			{
				Mod.Log.LogWarning("The pause screen has no LivesDisplay; the hearts stay.");
				return;
			}
			Transform parent = lives.transform.parent;
			lives.gameObject.SetActive(false);
			Hide(parent, "ObjectiveText");
			Hide(parent, "QuitRaceButton/QuitRaceHeartIcon");
			Hide(parent, "QuitRaceFreeButton/QuitRaceFreeHeartIcon");
			Transform label = parent.Find("QuitRaceButton/QuitRaceButtonLabel");
			if (label != null)
			{
				TMP_Text text = label.GetComponent<TMP_Text>();
				text.text = "Leave room";
				text.alignment = TextAlignmentOptions.Center;
				text.textWrappingMode = TextWrappingModes.NoWrap;
				RectTransform rect = (RectTransform)label;
				rect.anchorMin = Vector2.zero;
				rect.anchorMax = Vector2.one;
				rect.offsetMin = rect.offsetMax = Vector2.zero;
			}
		}

		private static void Hide(Transform parent, string path)
		{
			Transform t = parent.Find(path);
			if (t != null)
			{
				t.gameObject.SetActive(false);
			}
		}
	}

	// Pausing in a room: the panel, the cursor and the hand as the game does it, but no time stop, no audio stop.
	[HarmonyPatch(typeof(PauseController), "SetPaused")]
	internal static class RoomSetPaused
	{
		private static bool Prefix(PauseController __instance, bool paused)
		{
			if (!Room.Active || !Room.InRace)
			{
				return true;
			}
			Traverse t = Traverse.Create(__instance);
			t.Field("isPaused").SetValue(paused);
			t.Field("pausePanel").GetValue<GameObject>().SetActive(paused);
			ScreenTabController settings = t.Field("settingsPanel").GetValue<ScreenTabController>();
			if (!paused && settings != null)
			{
				settings.Close(true);
			}
			Grabber grabber = t.Field("grabber").GetValue<Grabber>();
			if (grabber != null)
			{
				grabber.SetHandVisible(!paused);
			}
			if (paused)
			{
				Cursor.lockState = CursorLockMode.Confined;
				Cursor.visible = true;
				t.Method("UpdateQuitButtons").GetValue();
			}
			else if (grabber != null && grabber.lockHardwareCursor)
			{
				Cursor.lockState = CursorLockMode.Locked;
				Cursor.visible = false;
			}
			Pause.Hold(__instance, paused);
			return false;
		}
	}

	[HarmonyPatch(typeof(PauseController), "Start")]
	internal static class RoomPauseStart
	{
		private static void Postfix(PauseController __instance)
		{
			if (Room.Active)
			{
				Pause.Strip(__instance);
			}
		}
	}

	// Leave from the pause screen = disconnect from the room: no life lost, no run bookkeeping.
	[HarmonyPatch]
	internal static class RoomQuit
	{
		private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(PauseController), "QuitRace");
			yield return AccessTools.Method(typeof(PauseController), "QuitRaceFree");
			yield return AccessTools.Method(typeof(PauseController), "BackToMenu");
		}

		private static bool Prefix()
		{
			if (!Room.Active && !Room.Leaving)
			{
				return true;
			}
			Pause.Release();
			Room.Leave("You left the room.");
			return false;
		}
	}

	// "-1 life" is the game's line for leaving a race it counts as lost; a room has no lives, so neither the fade nor the race-end text says it.
	[HarmonyPatch(typeof(RaceEndFadeEffect), nameof(RaceEndFadeEffect.FadeOut))]
	internal static class RoomNoLifeFade
	{
		private static System.Collections.IEnumerator Nothing()
		{
			yield break;
		}

		private static bool Prefix(ref System.Collections.IEnumerator __result)
		{
			if (!Room.Active && !Room.Leaving)
			{
				return true;
			}
			__result = Nothing();
			return false;
		}
	}

	internal static class RoomOutcome
	{
		internal static bool Allow;
	}

	[HarmonyPatch(typeof(RaceOutcomeText), nameof(RaceOutcomeText.Show))]
	internal static class RoomNoLifeText
	{
		private static bool Prefix()
		{
			if (RoomOutcome.Allow || (!Room.Active && !Room.Leaving))
			{
				return true;
			}
			// the game shows SUCCESS or -1 LIFE at the finish line for everybody; in a room only the winner gets it, on their own screen
			if (Room.Race != null && RaceSync.IsLocalWinner())
			{
				RoomOutcome.Allow = true;
				try
				{
					RaceOutcomeText.Show(true);
				}
				finally
				{
					RoomOutcome.Allow = false;
				}
			}
			return false;
		}
	}
}