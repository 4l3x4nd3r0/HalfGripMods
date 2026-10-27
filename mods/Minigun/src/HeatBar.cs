using UnityEngine;

namespace HalfGripMinigun
{
	/// <summary>
	/// The heat display: a wide, riveted casing with a black screen and HEAT written on it in the game's letters, lying along the dash
	/// where the ammo rack's magazines stood (its right edge at the rack's right edge). A row of segments lights up from the left as the
	/// gun heats, and the whole row goes from amber toward red with every shot. After an overheat it shows the lock running out instead,
	/// flashing red. It is flat sprites on the rack's layer, so the interior camera draws it.
	/// </summary>
	internal class HeatBar : MonoBehaviour
	{
		// The casing art is 128x24 pixels; at this density it is 5 units wide, across the dash. The screen has 21 segments, 3x10 pixels
		// each, every 4 pixels from x 34, from row 7 (make_minigun_sprites.py).
		private const float PixelsPerUnit = 25.6f;

		private const int Segments = 21;

		private static readonly Color Amber = new Color(1f, 0.76f, 0.16f, 1f);

		private static readonly Color Red = new Color(0.93f, 0.1f, 0.08f, 1f);

		private static Sprite white;

		private SpriteRenderer[] cells;

		private SpriteRenderer glow;

		private SpriteRenderer casing;

		public static HeatBar Make(Transform rack, Bounds area, int layer, int sortingLayer, int order)
		{
			Texture2D art = Mod.Ctx.Sprites.LoadTexture("minigun_display.png");
			if (art == null || art.width != 128 || art.height != 24)
			{
				Mod.Log.LogWarning("minigun_display.png is missing or not 128x24, so there is no heat display");
				return null;
			}
			// Drawn at DisplayScale of its first size: a higher pixels-per-unit makes the same picture smaller.
			float ppu = PixelsPerUnit / Mathf.Clamp(Mod.DisplayScale.Value, 0.3f, 1.5f);
			float width = art.width / ppu;
			float height = art.height / ppu;

			var go = new GameObject("MinigunHeat");
			go.layer = layer;
			go.transform.SetParent(rack, false);
			// Right edge on the rack's right edge, bottom on the rack's bottom, same depth.
			Vector3 centre = area.center + rack.right * (area.extents.x - width * 0.5f + Mod.DisplayShift.Value) + rack.up * (height * 0.5f - area.extents.y);
			go.transform.position = centre;
			go.transform.rotation = rack.rotation;
			// Children are sized in world units: undo the rack's own scale.
			Vector3 scale = rack.lossyScale;
			go.transform.localScale = new Vector3(1f / Mathf.Max(0.0001f, scale.x), 1f / Mathf.Max(0.0001f, scale.y), 1f / Mathf.Max(0.0001f, scale.z));

			HeatBar bar = go.AddComponent<HeatBar>();
			var casing = new GameObject("casing");
			casing.layer = layer;
			casing.transform.SetParent(go.transform, false);
			SpriteRenderer back = casing.AddComponent<SpriteRenderer>();
			art.filterMode = FilterMode.Point;
			back.sprite = Sprite.Create(art, new Rect(0f, 0f, art.width, art.height), new Vector2(0.5f, 0.5f), ppu);
			back.sortingLayerID = sortingLayer;
			back.sortingOrder = order;
			bar.casing = back;

			// A red wash over the screen that grows with the heat, under the segments.
			bar.glow = bar.Cell(go.transform, "glow", layer, sortingLayer, order + 1);
			bar.glow.transform.localPosition = new Vector3(-0.5f / ppu, 0.5f / ppu, 0f);
			bar.glow.transform.localScale = new Vector3(116f / ppu, 13f / ppu, 1f);

			bar.cells = new SpriteRenderer[Segments];
			for (int i = 0; i < Segments; i++)
			{
				SpriteRenderer cell = bar.Cell(go.transform, "segment " + i, layer, sortingLayer, order + 2);
				float x = 34f + i * 4f + 1.5f - art.width * 0.5f;
				cell.transform.localPosition = new Vector3(x / ppu, (art.height * 0.5f - 11.5f) / ppu, 0f);
				cell.transform.localScale = new Vector3(3f / ppu, 10f / ppu, 1f);
				cell.enabled = false;
				bar.cells[i] = cell;
			}
			Mod.Log.LogInfo($"Heat display on '{rack.name}': {width:0.##} x {height:0.##} units at {centre}, layer {layer}, order {order}");
			return bar;
		}

		private SpriteRenderer Cell(Transform parent, string name, int layer, int sortingLayer, int order)
		{
			var go = new GameObject(name);
			go.layer = layer;
			go.transform.SetParent(parent, false);
			SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
			renderer.sprite = White();
			renderer.sortingLayerID = sortingLayer;
			renderer.sortingOrder = order;
			return renderer;
		}

		// A one-unit white square, centred.
		private static Sprite White()
		{
			if (white == null)
			{
				var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
				texture.SetPixel(0, 0, Color.white);
				texture.Apply();
				white = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
				white.hideFlags = HideFlags.HideAndDontSave;
			}
			return white;
		}

		// Raising the remote hides every sprite under the gun's parent (the rack is one of them), and lowering it shows them all, so this
		// runs after the game's own Update and puts the display back every frame: it stays on whatever the remote is doing.
		private void LateUpdate()
		{
			MiniHeat heat = MiniHeat.Current;
			if (heat == null || cells == null)
			{
				return;
			}
			casing.enabled = true;
			glow.enabled = true;
			bool jammed = heat.Jammed;
			float amount = jammed ? heat.LockFraction : heat.Fraction;
			int lit = Mathf.Min(Segments, Mathf.CeilToInt(amount * Segments - 0.001f));
			float flash = Mathf.Repeat(Time.time * 4f, 1f) < 0.5f ? 1f : 0.35f;
			for (int i = 0; i < Segments; i++)
			{
				SpriteRenderer cell = cells[i];
				bool on = i < lit;
				if (cell.enabled != on)
				{
					cell.enabled = on;
				}
				if (!on)
				{
					continue;
				}
				// Hotter, redder: the whole row shifts with the heat, and the far end of the row is a little redder than the near end.
				float t = jammed ? 1f : Mathf.Clamp01(heat.Fraction * 1.05f + (i / (Segments - 1f) - 0.5f) * 0.3f);
				Color colour = Color.Lerp(Amber, Red, t);
				if (jammed)
				{
					colour *= flash;
					colour.a = 1f;
				}
				cell.color = colour;
			}
			float wash = jammed ? 0.35f * flash : heat.Fraction * 0.3f;
			glow.color = new Color(1f, 0.05f, 0.02f, wash);
		}
	}
}
