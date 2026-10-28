using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Multiplayer
{
	/// <summary>Small helpers to build UI in code with the game's pixel font (m6x11). Sizes are multiples of 11 so the font stays crisp.</summary>
	internal static class Ui
	{
		internal static readonly Color Panel = new Color(0.08f, 0.08f, 0.1f, 0.97f);
		internal static readonly Color Edge = new Color(0.3f, 0.3f, 0.36f, 1f);
		internal static readonly Color Row = new Color(0.13f, 0.13f, 0.16f, 1f);
		internal static readonly Color Red = new Color(0.82f, 0.16f, 0.16f, 1f);
		internal static readonly Color DarkRed = new Color(0.5f, 0.09f, 0.09f, 1f);
		internal static readonly Color Green = new Color(0.25f, 0.72f, 0.35f, 1f);
		internal static readonly Color Grey = new Color(0.35f, 0.35f, 0.4f, 1f);
		internal static readonly Color Amber = new Color(0.95f, 0.72f, 0.2f, 1f);
		internal static readonly Color Soft = new Color(0.62f, 0.62f, 0.68f, 1f);
		internal static readonly Color Ink = new Color(0.113f, 0.113f, 0.113f, 1f);

		private static TMP_FontAsset font;
		private static GameObject bigTemplate;

		/// <summary>Keeps a copy of the main menu's red button alive, so other scenes (the race) can show the same button.</summary>
		internal static void StashRedButton(GameObject template)
		{
			if (bigTemplate != null || template == null)
			{
				return;
			}
			bigTemplate = UnityEngine.Object.Instantiate(template);
			bigTemplate.name = "MpRedButtonTemplate";
			bigTemplate.SetActive(false);
			UnityEngine.Object.DontDestroyOnLoad(bigTemplate);
		}

		/// <summary>The menu's red button in any canvas. Null when the menu was never seen (it always is first).</summary>
		internal static Button RedButton(Transform parent, string text, float fontSize, Vector2 corner, Vector2 position, UnityAction click)
		{
			if (bigTemplate == null)
			{
				return Button(parent, text, corner, position, new Vector2(300f, 90f), Red, click, 44f);
			}
			RectTransform slot = (RectTransform)new GameObject("Slot " + text, typeof(RectTransform)).transform;
			slot.SetParent(parent, false);
			Anchor(slot, corner, position, new Vector2(252f, 136f));
			GameObject copy = UnityEngine.Object.Instantiate(bigTemplate, slot, false);
			copy.SetActive(true);
			copy.name = text + " Container";
			Button button = copy.GetComponentInChildren<Button>(true);
			button.onClick = new Button.ButtonClickedEvent();
			button.onClick.AddListener(click);
			TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
			label.text = text;
			label.enableAutoSizing = false;
			label.fontSize = fontSize;
			return button;
		}

		internal static TMP_FontAsset Font
		{
			get
			{
				if (font == null)
				{
					font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f != null && f.name.StartsWith("m6x11", StringComparison.OrdinalIgnoreCase));
					if (font == null)
					{
						font = TMP_Settings.defaultFontAsset;
					}
				}
				return font;
			}
		}

		/// <summary>A full-screen overlay of its own, scaled like the game's menus (1920 wide reference).</summary>
		internal static Canvas Overlay(string name, int sort)
		{
			GameObject go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			go.layer = LayerMask.NameToLayer("UI");
			Canvas canvas = go.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = sort;
			CanvasScaler scaler = go.GetComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
			scaler.matchWidthOrHeight = 0.5f;
			return canvas;
		}

		internal static RectTransform Box(string name, Transform parent, Color color)
		{
			GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			go.transform.SetParent(parent, false);
			Image image = go.GetComponent<Image>();
			image.color = color;
			image.raycastTarget = color.a > 0.002f;
			return (RectTransform)go.transform;
		}

		/// <summary>A panel with a light edge: the game's own popups look like this.</summary>
		internal static RectTransform Framed(string name, Transform parent, Color fill, float edge = 4f)
		{
			RectTransform outer = Box(name, parent, Edge);
			RectTransform inner = Box("Fill", outer, fill);
			inner.anchorMin = Vector2.zero;
			inner.anchorMax = Vector2.one;
			inner.offsetMin = new Vector2(edge, edge);
			inner.offsetMax = new Vector2(-edge, -edge);
			return inner;
		}

		/// <summary>Pins a corner of the rect (0,1 = top left, 1,1 = top right, 0,0 = bottom left, 0.5,1 = top centre) at an offset from that corner of its parent.</summary>
		internal static void Anchor(RectTransform rect, Vector2 corner, Vector2 position, Vector2 size)
		{
			rect.anchorMin = rect.anchorMax = rect.pivot = corner;
			rect.anchoredPosition = position;
			rect.sizeDelta = size;
		}

		internal static void Stretch(RectTransform rect, float margin)
		{
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = new Vector2(margin, margin);
			rect.offsetMax = new Vector2(-margin, -margin);
		}

		internal static TMP_Text Label(Transform parent, string text, float size, Color color, TextAlignmentOptions align, Vector2 corner, Vector2 position, Vector2 box)
		{
			GameObject go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
			go.transform.SetParent(parent, false);
			Anchor((RectTransform)go.transform, corner, position, box);
			TMP_Text label = go.GetComponent<TMP_Text>();
			label.font = Font;
			label.text = text;
			label.fontSize = size;
			label.color = color;
			label.alignment = align;
			label.textWrappingMode = TextWrappingModes.Normal;
			label.overflowMode = TextOverflowModes.Overflow;
			label.raycastTarget = false;
			return label;
		}

		internal static Button Button(Transform parent, string text, Vector2 corner, Vector2 position, Vector2 size, Color color, UnityAction click, float fontSize = 44f)
		{
			RectTransform rect = Box("Button " + text, parent, color);
			Anchor(rect, corner, position, size);
			Outline outline = rect.gameObject.AddComponent<Outline>();
			outline.effectColor = Edge;
			outline.effectDistance = new Vector2(4f, 4f);
			Button button = rect.gameObject.AddComponent<Button>();
			button.targetGraphic = rect.GetComponent<Image>();
			ColorBlock colors = button.colors;
			colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
			colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
			colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
			colors.fadeDuration = 0.06f;
			button.colors = colors;
			button.onClick.AddListener(click);
			TMP_Text label = Label(rect, text, fontSize, Color.white, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero, Vector2.zero);
			Stretch((RectTransform)label.transform, 4f);
			return button;
		}

		internal static void SetText(Button button, string text)
		{
			TMP_Text label = button.GetComponentInChildren<TMP_Text>();
			if (label != null && label.text != text)
			{
				label.text = text;
			}
		}

		/// <summary>A text box. Typing blocks the game's own key bindings while it has focus.</summary>
		internal static TMP_InputField Input(Transform parent, string placeholder, Vector2 corner, Vector2 position, Vector2 size, float fontSize, int maxLength)
		{
			RectTransform box = Box("Input", parent, new Color(0.04f, 0.04f, 0.05f, 1f));
			Anchor(box, corner, position, size);
			RectTransform area = Box("Text Area", box, new Color(0f, 0f, 0f, 0f));
			area.gameObject.AddComponent<RectMask2D>();
			Stretch(area, 10f);
			TMP_Text hint = Label(area, placeholder, fontSize, new Color(1f, 1f, 1f, 0.3f), TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero, Vector2.zero);
			Stretch((RectTransform)hint.transform, 0f);
			TMP_Text text = Label(area, "", fontSize, Color.white, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero, Vector2.zero);
			Stretch((RectTransform)text.transform, 0f);
			text.textWrappingMode = TextWrappingModes.NoWrap;
			TMP_InputField field = box.gameObject.AddComponent<TMP_InputField>();
			field.textViewport = area;
			field.textComponent = text;
			field.placeholder = hint;
			field.characterLimit = maxLength;
			field.lineType = TMP_InputField.LineType.SingleLine;
			field.onFocusSelectAll = false;
			fields.Add(field);
			return field;
		}

		private static readonly System.Collections.Generic.List<TMP_InputField> fields = new System.Collections.Generic.List<TMP_InputField>();
		private static readonly System.Collections.Generic.HashSet<TMP_InputField> muted = new System.Collections.Generic.HashSet<TMP_InputField>();

		/// <summary>
		/// Every frame: the game's controls are muted while one of our text boxes has focus and not a moment longer. Done by polling because an
		/// <c>onDeselect</c> never fires when the box's scene is destroyed while it is selected (the joiner's controls stayed muted for the whole session).
		/// </summary>
		internal static void TickInputs()
		{
			for (int i = fields.Count - 1; i >= 0; i--)
			{
				TMP_InputField field = fields[i];
				if (field == null)
				{
					fields.RemoveAt(i);
					if (muted.Remove(field))
					{
						GameInput.Mute(field, false); // works on a destroyed object too: the set compares references
					}
					continue;
				}
				bool want = field.isActiveAndEnabled && field.isFocused;
				if (want && muted.Add(field))
				{
					GameInput.Mute(field, true);
				}
				else if (!want && muted.Remove(field))
				{
					GameInput.Mute(field, false);
				}
			}
		}

		/// <summary>Lets go of every mute a text box holds (leaving a room, a race starting, the mod unloading).</summary>
		internal static void ReleaseInputs()
		{
			foreach (TMP_InputField field in muted)
			{
				GameInput.Mute(field, false);
			}
			muted.Clear();
		}

		internal static void Clear(Transform parent)
		{
			for (int i = parent.childCount - 1; i >= 0; i--)
			{
				UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
			}
		}
	}
}
