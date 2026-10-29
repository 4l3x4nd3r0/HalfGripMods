using UnityEngine;

namespace HalfGripSniper
{
	/// <summary>
	/// What you see through the scope: everything outside a round lens is blacked out, and a reticle sits at the aim point. It is drawn
	/// with the immediate-mode GUI, which is painted after every camera, so the wheel, the shifter and the rest of the cockpit (drawn by
	/// the interior camera) go under it. The lens follows the aim point, as the rifle does.
	/// </summary>
	internal class ScopeOverlay : MonoBehaviour
	{
		// The lens' radius as a share of the screen height, and how dark the surround gets at full zoom.
		private const float LensShare = 0.42f;

		private const float SurroundAlpha = 0.96f;

		private const int LensTextureSize = 512;

		private static readonly Color ReticleColour = new Color(0.04f, 0.04f, 0.05f, 1f);

		private static readonly Color ChargeColour = new Color(1f, 0.2f, 0.16f, 1f);

		private SniperGun gun;

		private Texture2D lens;

		public static ScopeOverlay Make(SniperGun owner)
		{
			var go = new GameObject("SniperScopeOverlay");
			ScopeOverlay overlay = go.AddComponent<ScopeOverlay>();
			overlay.gun = owner;
			overlay.lens = MakeLens();
			return overlay;
		}

		// A square of black with a round hole in the middle. The edge is soft, and the rim of the glass is a little darker than the middle.
		private static Texture2D MakeLens()
		{
			var texture = new Texture2D(LensTextureSize, LensTextureSize, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.HideAndDontSave
			};
			var pixels = new Color32[LensTextureSize * LensTextureSize];
			float half = LensTextureSize * 0.5f;
			for (int y = 0; y < LensTextureSize; y++)
			{
				for (int x = 0; x < LensTextureSize; x++)
				{
					float r = Mathf.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half)) / half;
					// Outside the lens: opaque. Across the last pixels of the edge: fade. Inside, near the rim: a faint vignette.
					float alpha = Mathf.Clamp01((r - 1f) * half / 1.5f + 0.5f);
					alpha = Mathf.Max(alpha, Mathf.Clamp01((r - 0.86f) / 0.14f) * 0.45f);
					pixels[y * LensTextureSize + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(alpha * 255f));
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply();
			return texture;
		}

		private void OnDestroy()
		{
			if (lens != null)
			{
				Destroy(lens);
			}
		}

		private void OnGUI()
		{
			if (Event.current.type != EventType.Repaint || gun == null || !gun.isActiveAndEnabled || !Mod.ShowScope.Value)
			{
				return;
			}
			float amount = Mathf.Clamp01((gun.ZoomAmount - 0.15f) / 0.85f);
			if (amount <= 0.01f)
			{
				return;
			}
			float width = Screen.width;
			float height = Screen.height;
			Vector2 aim = gun.ScreenAimPoint;
			// The GUI's y runs down the screen, the game's up.
			float cx = aim.x;
			float cy = height - aim.y;
			float r = height * LensShare;
			Color previous = GUI.color;

			GUI.color = new Color(1f, 1f, 1f, amount * SurroundAlpha);
			GUI.DrawTexture(new Rect(cx - r, cy - r, 2f * r, 2f * r), lens);
			// The fills use the white texture, so they need a black tint (the lens texture is already black).
			GUI.color = new Color(0f, 0f, 0f, amount * SurroundAlpha);
			Fill(0f, 0f, cx - r, height);
			Fill(cx + r, 0f, width - (cx + r), height);
			Fill(cx - r, 0f, 2f * r, cy - r);
			Fill(cx - r, cy + r, 2f * r, height - (cy + r));

			DrawReticle(cx, cy, r, amount);
			GUI.color = previous;
		}

		private static void Fill(float x, float y, float w, float h)
		{
			if (w > 0f && h > 0f)
			{
				GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
			}
		}

		// A duplex reticle: thick posts out at the edge that thin to hairlines toward the middle, with a gap at the centre, a few mil
		// dots along each hairline, and a dot in the middle that turns red as the shot charges. A bar under it shows the charge too.
		private void DrawReticle(float cx, float cy, float r, float amount)
		{
			float thin = Mathf.Max(1f, Screen.height * 0.0016f);
			float thick = thin * 5f;
			float gap = r * 0.035f;
			float postStart = r * 0.62f;

			GUI.color = new Color(ReticleColour.r, ReticleColour.g, ReticleColour.b, amount);
			// Hairlines, left, right, up and down of the centre.
			Fill(cx - r, cy - thin * 0.5f, r - gap, thin);
			Fill(cx + gap, cy - thin * 0.5f, r - gap, thin);
			Fill(cx - thin * 0.5f, cy - r, thin, r - gap);
			Fill(cx - thin * 0.5f, cy + gap, thin, r - gap);
			// Posts.
			Fill(cx - r, cy - thick * 0.5f, r - postStart, thick);
			Fill(cx + postStart, cy - thick * 0.5f, r - postStart, thick);
			Fill(cx - thick * 0.5f, cy - r, thick, r - postStart);
			Fill(cx - thick * 0.5f, cy + postStart, thick, r - postStart);
			// Mil dots.
			float dot = thin * 2.6f;
			for (int i = 1; i <= 4; i++)
			{
				float d = gap + (postStart - gap) * i / 5f;
				Fill(cx - d - dot * 0.5f, cy - dot * 0.5f, dot, dot);
				Fill(cx + d - dot * 0.5f, cy - dot * 0.5f, dot, dot);
				Fill(cx - dot * 0.5f, cy - d - dot * 0.5f, dot, dot);
				Fill(cx - dot * 0.5f, cy + d - dot * 0.5f, dot, dot);
			}

			float charge = gun.Charge;
			GUI.color = new Color(ChargeColour.r, ChargeColour.g, ChargeColour.b, amount * Mathf.Lerp(0.35f, 1f, charge));
			float centre = thin * Mathf.Lerp(2.5f, 4.5f, charge);
			Fill(cx - centre, cy - centre, centre * 2f, centre * 2f);

			// The charge bar, under the reticle.
			float barWidth = r * 0.5f;
			float barHeight = Mathf.Max(3f, Screen.height * 0.006f);
			float barY = cy + r * 0.74f;
			GUI.color = new Color(0f, 0f, 0f, amount * 0.7f);
			Fill(cx - barWidth * 0.5f - 2f, barY - 2f, barWidth + 4f, barHeight + 4f);
			GUI.color = new Color(ChargeColour.r, ChargeColour.g, ChargeColour.b, amount);
			Fill(cx - barWidth * 0.5f, barY, barWidth * charge, barHeight);
		}
	}
}
