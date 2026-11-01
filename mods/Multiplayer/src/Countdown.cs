using TMPro;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// "RESPAWN IN 3": big, in the middle of the screen above the car, with a drop shadow. Shown to a wrecked player after the WRECKED text has
	/// gone and until the car is put back on the track.
	/// </summary>
	internal static class Countdown
	{
		private static GameObject root;
		private static TMP_Text label;
		private static TMP_Text shadow;

		internal static bool Visible { get { return root != null && root.activeSelf; } }

		internal static void Show(int seconds)
		{
			if (root == null)
			{
				Canvas canvas = Ui.Overlay("MultiplayerCountdown", 29500);
				Object.DontDestroyOnLoad(canvas.gameObject);
				root = canvas.gameObject;
				shadow = Ui.Label(canvas.transform, "", 96f, Color.black, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(4f, 196f), new Vector2(1500f, 130f));
				label = Ui.Label(canvas.transform, "", 96f, Ui.Amber, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), new Vector2(1500f, 130f));
				shadow.richText = false;
				label.richText = false;
			}
			string text = Tx.F("RESPAWN IN {0}", Mathf.Max(1, seconds));
			label.text = text;
			shadow.text = text;
			root.SetActive(true);
		}

		internal static void Hide()
		{
			if (root != null)
			{
				root.SetActive(false);
			}
		}

		internal static void Remove()
		{
			if (root != null)
			{
				Object.Destroy(root);
				root = null;
			}
		}
	}
}
