using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer
{
	/// <summary>
	/// A black sheet over everything while a race loads. The race is started through the garage scene (that is where the game applies the
	/// loadout), which would show for a second; this keeps it unseen so lobby, fade and track follow each other without a hiccup.
	/// </summary>
	internal static class Cover
	{
		private static Canvas canvas;
		private static CanvasGroup group;
		private static float target;
		private static float shownAt;
		private static bool forMenu;

		/// <summary>Cover for the way back to the main menu scene: held until the MULTIPLAYER screen is up, so the main menu is never seen.</summary>
		internal static void ShowForMenu()
		{
			MenuHook.ScreenReady = false;
			Show();
			forMenu = true;
		}

		internal static void Show()
		{
			forMenu = false;
			if (canvas == null)
			{
				canvas = Ui.Overlay("MpCover", 30000);
				Object.DontDestroyOnLoad(canvas.gameObject);
				RectTransform sheet = Ui.Box("Sheet", canvas.transform, Color.black);
				Ui.Stretch(sheet, 0f);
				Ui.Label(sheet, "LOADING", 66f, Color.white, TMPro.TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 90f));
				group = canvas.gameObject.AddComponent<CanvasGroup>();
				group.alpha = 0f;
			}
			target = 1f;
			shownAt = Time.unscaledTime;
		}

		internal static void Hide()
		{
			target = 0f;
		}

		internal static void Tick()
		{
			if (canvas == null)
			{
				return;
			}
			// never leave the screen black
			if (target > 0f)
			{
				if (forMenu)
				{
					if (MenuHook.ScreenReady || Time.unscaledTime - shownAt > 20f)
					{
						target = 0f;
					}
				}
				else if (Time.unscaledTime - shownAt > 60f || !Room.Active || Room.Phase == Phase.Lobby || Room.Phase == Phase.Results || Room.Phase == Phase.None)
				{
					target = 0f; // a race that did not start, a room that is gone
				}
			}
			group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / (target > 0f ? 0.3f : 0.5f));
			if (target <= 0f && group.alpha <= 0f)
			{
				Object.Destroy(canvas.gameObject);
				canvas = null;
				group = null;
			}
		}

		internal static void Remove()
		{
			if (canvas != null)
			{
				Object.Destroy(canvas.gameObject);
				canvas = null;
				group = null;
			}
			target = 0f;
		}
	}
}