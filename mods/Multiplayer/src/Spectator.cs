using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer
{
	/// <summary>
	/// What a player who has finished (or is out) sees: a drone that hovers over one of the cars still on the track, players and AI
	/// alike (Tab or Space for the next one), a copy of the car's own world camera so sky, fog and post-processing match, and a
	/// RACE RESULTS button in the game's own style. Going to the results takes the player's car off the track.
	/// </summary>
	internal static class Spectator
	{
		private static GameObject go;
		private static UnityEngine.Object muteOwner;
		private static Camera cam;
		private static GameObject canvasObject;
		private static TMP_Text label;
		private static TMP_Text tip;
		private static Button resultsButton;
		private static Transform target;
		private static string targetName = "";
		private static bool active;
		private static float refreshAt;
		private static List<KeyValuePair<Transform, string>> targets = new List<KeyValuePair<Transform, string>>();

		internal static bool Active { get { return active; } }

		/// <summary>The drone's camera while it is up (the name tags face it).</summary>
		internal static Camera Cam { get { return active ? cam : null; } }

		internal static void Begin()
		{
			if (active || RaceSync.TM == null)
			{
				return;
			}
			CarControllerNew cc = RaceSync.TM.playerCarController;
			if (cc == null || cc.fpvCamera == null)
			{
				return;
			}
			try
			{
				go = UnityEngine.Object.Instantiate(cc.fpvCamera.gameObject);
				go.name = "MpSpectatorCamera";
				go.transform.SetParent(null, true);
				foreach (MonoBehaviour behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))
				{
					if (behaviour != null && behaviour.GetType().Name != "UniversalAdditionalCameraData")
					{
						UnityEngine.Object.Destroy(behaviour);
					}
				}
				foreach (AudioListener listener in go.GetComponentsInChildren<AudioListener>(true))
				{
					UnityEngine.Object.Destroy(listener);
				}
				cam = go.GetComponent<Camera>();
				cam.depth = cc.fpvCamera.depth + 10f;
				cam.enabled = true;
				cc.fpvCamera.enabled = false;
				if (cc.interiorCamera != null)
				{
					cc.interiorCamera.enabled = false;
				}
				active = true;
				BuildUi();
				muteOwner = go;
				GameInput.Mute(muteOwner, true); // the car is let go of; the mouse is for the button
				Mod.Log.LogInfo("Spectating (drone)");
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The spectator camera could not be made: " + e.Message);
				Stop();
			}
		}

		private static void BuildUi()
		{
			Canvas canvas = Ui.Overlay("MpSpectate", 45);
			canvasObject = canvas.gameObject;
			label = Ui.Label(canvas.transform, "", 33f, Color.white, TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(1200f, 90f));
			tip = Ui.Label(canvas.transform, "", 28f, Ui.Amber, TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(700f, 40f));
			resultsButton = Ui.RedButton(canvas.transform, "RACE RESULTS", 6.5f, new Vector2(0.5f, 0f), new Vector2(0f, 40f), () => RaceSync.EnterResults());
			HoverTip.Attach(resultsButton, tip, () => RaceSync.CanEnterResults() ? "" : "Waiting for the other players to finish.");
		}

		internal static void Stop()
		{
			if (!active && go == null && canvasObject == null && (object)muteOwner == null)
			{
				return;
			}
			if ((object)muteOwner != null)
			{
				GameInput.Mute(muteOwner, false); // also when the scene (and the camera) is already gone
				muteOwner = null;
			}
			if (go != null)
			{
				UnityEngine.Object.Destroy(go);
			}
			if (canvasObject != null)
			{
				UnityEngine.Object.Destroy(canvasObject);
			}
			active = false;
			TrackManager tm = RaceSync.TM;
			if (tm != null && tm.playerCarController != null)
			{
				if (tm.playerCarController.fpvCamera != null)
				{
					tm.playerCarController.fpvCamera.enabled = true;
				}
				if (tm.playerCarController.interiorCamera != null)
				{
					tm.playerCarController.interiorCamera.enabled = true;
				}
			}
			go = null;
			cam = null;
			canvasObject = null;
			target = null;
		}

		internal static void Tick()
		{
			if (!active || cam == null)
			{
				return;
			}
			Cursor.lockState = CursorLockMode.Confined;
			Cursor.visible = true;
			if (Time.unscaledTime >= refreshAt)
			{
				refreshAt = Time.unscaledTime + 0.5f;
				targets = RaceSync.WatchTargets();
			}
			bool cycle = Mod.SpectateNext() || Input.GetKeyDown(KeyCode.Space);
			int at = -1;
			for (int i = 0; i < targets.Count; i++)
			{
				if (targets[i].Key == target)
				{
					at = i;
				}
			}
			if (target == null || at < 0 || cycle)
			{
				if (targets.Count == 0)
				{
					target = null;
				}
				else
				{
					KeyValuePair<Transform, string> next = targets[(at + 1) % targets.Count];
					target = next.Key;
					targetName = next.Value;
				}
			}
			if (label != null)
			{
				label.text = target == null ? "NOBODY LEFT TO WATCH" : "WATCHING " + targetName.ToUpperInvariant() + "\nTAB: NEXT CAR";
			}
			if (resultsButton != null)
			{
				bool can = RaceSync.CanEnterResults();
				resultsButton.interactable = can;
				resultsButton.targetGraphic.color = can ? Color.white : new Color(0.45f, 0.45f, 0.45f, 1f);
			}
			if (target != null)
			{
				// a drone: high and well behind, easing after the car
				Vector3 want = target.position - target.forward * 17f + Vector3.up * 10f;
				cam.transform.position = Vector3.Lerp(cam.transform.position, want, 0.08f);
				Quaternion look = Quaternion.LookRotation((target.position + Vector3.up * 1.2f) - cam.transform.position, Vector3.up);
				cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, look, 0.15f);
			}
		}
	}
}