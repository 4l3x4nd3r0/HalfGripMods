using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalfGripBombRace
{
	/// <summary>What the bomb's little screen shows.</summary>
	internal enum BombDisplay
	{
		/// <summary>Not armed yet: three bomb icons.</summary>
		Icons,

		/// <summary>Armed and the fuse is out: ARMED, in red.</summary>
		Armed,

		/// <summary>Under the limit and the fuse is burning: the seconds left, in red.</summary>
		Countdown,

		/// <summary>Back over the limit and the fuse is burning down: your speed, in green.</summary>
		Recover,

		/// <summary>Race over or car gone: blank.</summary>
		Off
	}

	/// <summary>
	/// The bomb on the dash: a block of C4 with a screen and a red and a green bulb, placed like one of the dash's own items but on the
	/// other side of the steering wheel (it is the gear display's position mirrored across the wheel). It is a sprite with the text drawn
	/// on its screen in the game's own pixel font, hung on the wheel's mount and drawn on the wheel's layer, so the interior camera
	/// draws it and it moves with the cockpit.
	/// </summary>
	internal class DashBomb : MonoBehaviour
	{
		// Pixels per unit the plugin's own sprites are made at; only ratios matter, the block is scaled to the wheel.
		private const float Ppu = 100f;

		// How far across the wheel the gear display's place is mirrored (1 is the same distance on the other side).
		private const float MirrorReach = 0.85f;

		// Used when the gear display cannot be found: where the block sits, in wheel widths across and up from the wheel's centre.
		private static readonly Vector2 FallbackOffset = new Vector2(-0.62f, 0.42f);

		// From the art script (DASH_*): the block's size, its black screen (x, y, width, height) and the top-left of each 4x4 bulb, in pixels.
		private const float BlockW = 44f;

		private const float BlockH = 26f;

		private static readonly Rect Screen = new Rect(4f, 4f, 26f, 13f);

		private static readonly Vector2 RedPixel = new Vector2(35f, 4f);

		private static readonly Vector2 GreenPixel = new Vector2(35f, 12f);

		private static readonly Color RedOn = new Color(1f, 0.16f, 0.1f, 1f);

		private static readonly Color GreenOn = new Color(0.2f, 1f, 0.3f, 1f);

		private static readonly Color Unlit = new Color(0.16f, 0.12f, 0.12f, 1f);

		// The same red as the game's own red text ("Bomb: stay over...") and a matching green.
		private static readonly Color TextRed = new Color(1f, 0.35f, 0.29f, 1f);

		private static readonly Color TextGreen = new Color(0.35f, 1f, 0.42f, 1f);

		private SpriteRenderer redBulb;

		private SpriteRenderer greenBulb;

		private SpriteRenderer redGlow;

		private SpriteRenderer greenGlow;

		private readonly SpriteRenderer[] icons = new SpriteRenderer[3];

		private TextMeshPro label;

		private string shownText;

		private Color shownColour;

		internal static DashBomb Make(CarLink car)
		{
			Camera view = null;
			GameObject cameraObject = GameObject.Find("InteriorCamera");
			if (cameraObject != null)
			{
				view = cameraObject.GetComponent<Camera>();
			}
			Transform wheel = car != null && car.sw != null && car.sw.wheel != null ? car.sw.wheel.transform : null;
			if (view == null || wheel == null)
			{
				Mod.Log.LogWarning("No interior camera or steering wheel found, so no bomb on the dash");
				return null;
			}
			Sprite bodySprite = Mod.Ctx.Sprites.LoadSprite("bomb_dash.png", null, Ppu);
			Sprite miniSprite = Mod.Ctx.Sprites.LoadSprite("bomb_mini.png", null, Ppu);
			Sprite bulbSprite = Mod.Ctx.Sprites.LoadSprite("bomb_bulb.png", null, Ppu);
			Sprite glowSprite = Mod.Ctx.Sprites.LoadSprite("bomb_glow.png", null, Ppu);
			Shader plain = Shader.Find("Sprites/Default");
			if (bodySprite == null || miniSprite == null || bulbSprite == null || glowSprite == null || plain == null)
			{
				return null;
			}

			// Match the wheel's layer and sorting, so the same camera draws it in front of the dash.
			SpriteRenderer like = wheel.GetComponentInChildren<SpriteRenderer>(true);
			int layer = wheel.gameObject.layer;
			int sortingLayer = like != null ? like.sortingLayerID : 0;
			int order = like != null ? like.sortingOrder + 20 : 100;

			// Where: the gear display's place, mirrored across the wheel, measured the way the camera sees the cockpit (so it holds
			// at any window shape). The wheel's apparent width is the unit.
			float wheelWidth = ApparentWidth(wheel, view);
			Vector3 world;
			Transform gear = car.gearIndicator != null ? car.gearIndicator.transform : null;
			if (gear != null)
			{
				Vector3 d = gear.position - wheel.position;
				float across = Vector3.Dot(d, view.transform.right);
				float up = Vector3.Dot(d, view.transform.up);
				float deep = Vector3.Dot(d, view.transform.forward);
				if (wheelWidth <= 0.0001f)
				{
					wheelWidth = Mathf.Abs(across) / 0.7f;
				}
				world = wheel.position + view.transform.right * (-across * MirrorReach + Mod.NudgeX.Value * wheelWidth)
					+ view.transform.up * (up + Mod.NudgeY.Value * wheelWidth) + view.transform.forward * deep;
			}
			else
			{
				if (wheelWidth <= 0.0001f)
				{
					wheelWidth = 1f;
				}
				world = wheel.position + view.transform.right * ((FallbackOffset.x + Mod.NudgeX.Value) * wheelWidth)
					+ view.transform.up * ((FallbackOffset.y + Mod.NudgeY.Value) * wheelWidth);
			}
			float scale = Mod.DashWidth.Value * wheelWidth / (bodySprite.rect.width / Ppu);

			var go = new GameObject("BombDash");
			go.layer = layer;
			go.transform.SetParent(wheel.parent != null ? wheel.parent : wheel, true);
			go.transform.position = world;
			go.transform.rotation = view.transform.rotation;
			Vector3 parentScale = go.transform.parent.lossyScale;
			go.transform.localScale = new Vector3(scale / Mathf.Max(0.0001f, parentScale.x), scale / Mathf.Max(0.0001f, parentScale.y), scale / Mathf.Max(0.0001f, parentScale.z));

			var bomb = go.AddComponent<DashBomb>();
			// Drawn over whatever is behind it whatever the depth: the wheel's mount is not a fixed distance from the camera.
			var material = new Material(plain) { name = "BombDash", hideFlags = HideFlags.HideAndDontSave };
			material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);

			SpriteRenderer body = go.AddComponent<SpriteRenderer>();
			body.sprite = bodySprite;
			body.sharedMaterial = material;
			body.sortingLayerID = sortingLayer;
			body.sortingOrder = order;

			bomb.redGlow = Part(go.transform, "RedGlow", glowSprite, material, layer, sortingLayer, order + 1, BulbCentre(RedPixel), 1.3f);
			bomb.greenGlow = Part(go.transform, "GreenGlow", glowSprite, material, layer, sortingLayer, order + 1, BulbCentre(GreenPixel), 1.3f);
			bomb.redBulb = Part(go.transform, "RedBulb", bulbSprite, material, layer, sortingLayer, order + 2, BulbCentre(RedPixel), 1f);
			bomb.greenBulb = Part(go.transform, "GreenBulb", bulbSprite, material, layer, sortingLayer, order + 2, BulbCentre(GreenPixel), 1f);

			// Three little bombs across the screen.
			Vector3 middle = PixelToLocal(Screen.center);
			for (int i = 0; i < bomb.icons.Length; i++)
			{
				Vector3 at = middle + new Vector3((i - 1) * 9f / Ppu, 0f, 0f);
				bomb.icons[i] = Part(go.transform, "Icon" + i, miniSprite, material, layer, sortingLayer, order + 3, at, 1f);
			}

			bomb.MakeLabel(go.transform, middle, material, layer, sortingLayer, order + 4);
			bomb.SetLights(FuseState.Dim, FuseState.Dim);
			bomb.Show(BombDisplay.Icons, 0f);

			if (Mod.DumpInfo.Value)
			{
				Mod.Log.LogInfo($"Dash bomb at {world} (wheel width {wheelWidth:0.000}, gear display {(gear != null ? gear.position.ToString() : "not found")}), scale {scale:0.000}, layer {LayerMask.LayerToName(layer)}, parent {go.transform.parent.name}, wheel sprite {(like != null ? like.sprite.name + " ppu " + like.sprite.pixelsPerUnit : "none")}, text {(bomb.label != null)}");
			}
			return bomb;
		}

		/// <summary>The bulbs' brightness, 0 (out) to 1 (flash).</summary>
		internal void SetLights(float red, float green)
		{
			Light(redBulb, redGlow, RedOn, red);
			Light(greenBulb, greenGlow, GreenOn, green);
		}

		/// <summary>Sets what the screen shows. <paramref name="value"/> is the seconds left for Countdown and the speed for Recover.</summary>
		internal void Show(BombDisplay mode, float value)
		{
			foreach (SpriteRenderer icon in icons)
			{
				if (icon != null)
				{
					icon.enabled = mode == BombDisplay.Icons;
				}
			}
			if (label == null)
			{
				return;
			}
			string text;
			Color colour = TextRed;
			switch (mode)
			{
				case BombDisplay.Armed:
					text = "ARMED";
					break;
				case BombDisplay.Countdown:
					text = Mathf.Max(0f, value).ToString("0.0");
					break;
				case BombDisplay.Recover:
					text = Mathf.RoundToInt(value).ToString();
					colour = TextGreen;
					break;
				default:
					text = "";
					break;
			}
			// Only rebuild the text when it changes.
			if (text != shownText || colour != shownColour)
			{
				shownText = text;
				shownColour = colour;
				label.text = text;
				label.color = colour;
			}
		}

		private static void Light(SpriteRenderer bulb, SpriteRenderer glow, Color on, float level)
		{
			if (bulb == null)
			{
				return;
			}
			level = Mathf.Clamp01(level);
			bulb.color = Color.Lerp(Unlit, on, level);
			Color halo = on;
			halo.a = level * level * 0.85f;
			glow.color = halo;
		}

		// The text on the screen, in the game's own pixel font: borrowed from the cockpit's lap board (or, failing that, the speed display).
		private void MakeLabel(Transform parent, Vector3 centre, Material like, int layer, int sortingLayer, int order)
		{
			TextMeshPro template = null;
			LapCounter laps = FindFirstObjectByType<LapCounter>();
			if (laps != null)
			{
				template = laps.GetComponent<TextMeshPro>();
			}
			if (template == null)
			{
				SpeedDashboard speed = FindFirstObjectByType<SpeedDashboard>();
				template = speed != null ? speed.textMeshPro : null;
			}
			if (template == null || template.font == null)
			{
				Mod.Log.LogWarning("No game text to borrow a font from, so the bomb's screen will show only the little bombs");
				return;
			}

			var go = new GameObject("BombText", typeof(RectTransform));
			go.layer = layer;
			go.transform.SetParent(parent, false);
			go.transform.localRotation = Quaternion.identity;
			// A little nearer the camera than the screen, which is at z = 0 (the camera looks along +z, the sprites face -z).
			go.transform.localPosition = centre + new Vector3(0f, 0f, -0.01f);
			label = go.AddComponent<TextMeshPro>();
			label.font = template.font;
			label.fontSharedMaterial = template.fontSharedMaterial;
			label.fontSize = 10f;
			label.alignment = TextAlignmentOptions.Center;
			label.textWrappingMode = TextWrappingModes.NoWrap;
			label.rectTransform.sizeDelta = new Vector2(10000f, 10000f);
			// Draw it whatever is in front of it, like the sprites round it.
			Material material = label.fontMaterial;
			if (material.HasProperty("_ZTestMode"))
			{
				material.SetFloat("_ZTestMode", (float)CompareFunction.Always);
			}
			MeshRenderer renderer = go.GetComponent<MeshRenderer>();
			renderer.sortingLayerID = sortingLayer;
			renderer.sortingOrder = order;

			// Size it so the longest thing it shows (ARMED) fills most of the screen; numbers are narrower.
			label.text = "ARMED";
			label.ForceMeshUpdate();
			float width = Mathf.Max(0.0001f, label.preferredWidth);
			float height = Mathf.Max(0.0001f, label.preferredHeight);
			float fit = Mathf.Min(Screen.width * 0.9f / Ppu / width, Screen.height * 0.75f / Ppu / height);
			go.transform.localScale = Vector3.one * fit;
			label.text = "";
			shownText = "";
		}

		// How wide the thing looks from the camera, in world units, from the pieces it is made of; 0 when it cannot be told.
		private static float ApparentWidth(Transform root, Camera view)
		{
			float min = float.MaxValue;
			float max = float.MinValue;
			foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
			{
				Bounds b = renderer.bounds;
				for (int i = 0; i < 8; i++)
				{
					Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
					float x = view.transform.InverseTransformPoint(corner).x;
					min = Mathf.Min(min, x);
					max = Mathf.Max(max, x);
				}
			}
			return max > min ? max - min : 0f;
		}

		// A pixel position on the block (from its top left) as a place in the sprite, in sprite units from its centre.
		private static Vector3 PixelToLocal(Vector2 pixel)
		{
			return new Vector3((pixel.x - BlockW * 0.5f) / Ppu, (BlockH * 0.5f - pixel.y) / Ppu, 0f);
		}

		private static Vector3 BulbCentre(Vector2 topLeft)
		{
			return PixelToLocal(topLeft + new Vector2(2f, 2f));
		}

		private static SpriteRenderer Part(Transform parent, string name, Sprite sprite, Material material, int layer, int sortingLayer, int order, Vector3 position, float scale)
		{
			var go = new GameObject(name);
			go.layer = layer;
			go.transform.SetParent(parent, false);
			go.transform.localPosition = position;
			go.transform.localScale = Vector3.one * scale;
			SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
			renderer.sprite = sprite;
			renderer.sharedMaterial = material;
			renderer.sortingLayerID = sortingLayer;
			renderer.sortingOrder = order;
			return renderer;
		}
	}
}
