using UnityEngine;

namespace HalfGripSniper
{
	/// <summary>
	/// The red dot where the rifle is pointed. A flat sprite stood on the thing being aimed at, turned to face the view and kept about
	/// the same size on screen whatever the distance. Brighter as the charge builds. It is only for show: the shot is not steered by it.
	/// </summary>
	internal class LaserDot : MonoBehaviour
	{
		// How much of the screen's height the dot takes up, at no charge and at full charge.
		private const float SmallShare = 0.014f;

		private const float LargeShare = 0.02f;

		private const float DotPixelsPerUnit = 16f;

		private SpriteRenderer dotRenderer;

		public static LaserDot Make()
		{
			var go = new GameObject("SniperLaserDot");
			go.SetActive(false);
			LaserDot dot = go.AddComponent<LaserDot>();
			dot.dotRenderer = go.AddComponent<SpriteRenderer>();
			Sprite sprite = Mod.Ctx.Sprites.LoadSprite("sniper_dot.png", null, DotPixelsPerUnit);
			dot.dotRenderer.sprite = sprite;
			dot.dotRenderer.sortingOrder = 1000;
			return dot;
		}

		public void Show(Vector3 point, int layer, Camera view, float charge)
		{
			if (dotRenderer == null || dotRenderer.sprite == null || view == null)
			{
				return;
			}
			Vector3 toView = view.transform.position - point;
			float distance = toView.magnitude;
			if (distance < 0.01f)
			{
				return;
			}
			// A little way out from the surface so it does not sink into it, and further out the further away it is.
			transform.position = point + toView / distance * Mathf.Max(0.05f, distance * 0.004f);
			transform.rotation = view.transform.rotation;
			float screenHeight = 2f * distance * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
			float size = screenHeight * Mathf.Lerp(SmallShare, LargeShare, charge);
			// The sprite is 16 pixels across at 16 pixels per unit, so it is a unit wide before scaling.
			transform.localScale = Vector3.one * size;
			gameObject.layer = layer;
			dotRenderer.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.45f, 1f, charge));
			if (!gameObject.activeSelf)
			{
				gameObject.SetActive(true);
			}
		}

		public void Hide()
		{
			if (gameObject.activeSelf)
			{
				gameObject.SetActive(false);
			}
		}
	}
}
