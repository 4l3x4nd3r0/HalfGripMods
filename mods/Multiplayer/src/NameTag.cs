using System;
using TMPro;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// The name of a car, floating above it in the world: orange for players, white for AI cars (as on the game's scoreboard), in the
	/// game's pixel font. It always faces the camera that is looking (the car's own or the spectator's drone) and grows a little with distance, never wider than the car, so
	/// it stays readable; it is hidden when the car is wrecked, far away or behind the camera.
	/// </summary>
	internal sealed class NameTag : MonoBehaviour
	{
		private const float ShowDistance = 220f;
		private const float Height = 4.3f;
		private const float CarWidth = 5f; // a car is about 5 m wide
		private const float MinScale = 0.12f;
		private const float MaxScale = 0.24f;

		private Transform follow;
		private EnemyController controller;
		private Func<string> name;
		private TextMeshPro text;

		internal static void Attach(GameObject car, Func<string> name, bool human)
		{
			if (car == null)
			{
				return;
			}
			GameObject go = new GameObject("MpNameTag");
			NameTag tag = go.AddComponent<NameTag>();
			tag.follow = car.transform;
			tag.controller = car.GetComponent<EnemyController>();
			tag.name = name;
			tag.text = go.AddComponent<TextMeshPro>();
			tag.text.font = Ui.Font;
			tag.text.fontSize = 36f;
			tag.text.alignment = TextAlignmentOptions.Center;
			tag.text.textWrappingMode = TextWrappingModes.NoWrap;
			tag.text.richText = false;
			tag.text.color = human ? new Color(1f, 0.55f, 0f, 1f) : Color.white;
			tag.text.outlineWidth = 0.22f;
			tag.text.outlineColor = new Color32(0, 0, 0, 255);
			tag.text.text = "";
		}

		private static Camera Looking()
		{
			if (Spectator.Cam != null)
			{
				return Spectator.Cam;
			}
			TrackManager tm = RaceSync.TM;
			return tm != null && tm.playerCarController != null ? tm.playerCarController.fpvCamera : null;
		}

		private void LateUpdate()
		{
			if (follow == null)
			{
				Destroy(gameObject); // the car is gone
				return;
			}
			Camera cam = Looking();
			bool alive = controller == null || controller.alive;
			if (cam == null || !alive || !Room.Active || Room.Phase != Phase.Racing)
			{
				text.enabled = false;
				return;
			}
			Vector3 at = follow.position + Vector3.up * Height;
			Vector3 toCar = at - cam.transform.position;
			float distance = toCar.magnitude;
			if (distance > ShowDistance || distance < 4f || Vector3.Dot(cam.transform.forward, toCar) <= 0f)
			{
				text.enabled = false;
				return;
			}
			string shown = name();
			if (text.text != shown)
			{
				text.text = shown;
			}
			text.enabled = true;
			transform.position = at;
			transform.rotation = cam.transform.rotation;
			// a little larger the farther away, but never wider than the car
			float scale = Mathf.Clamp(distance * 0.006f, MinScale, MaxScale);
			float widest = CarWidth * 0.95f / Mathf.Max(0.01f, text.preferredWidth);
			scale = Mathf.Min(scale, widest);
			transform.localScale = new Vector3(scale, scale, scale);
		}
	}
}