using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer
{
	/// <summary>Shows a line of text while the pointer is over a button, also over a greyed-out one.</summary>
	internal sealed class HoverTip : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
	{
		private TMP_Text label;
		private Func<string> text;
		private bool over;

		internal static void Attach(Button button, TMP_Text label, Func<string> text)
		{
			HoverTip tip = button.gameObject.AddComponent<HoverTip>();
			tip.label = label;
			tip.text = text;
			label.text = "";
		}

		public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData data) { over = true; }

		public void OnPointerExit(UnityEngine.EventSystems.PointerEventData data) { over = false; label.text = ""; }

		private void Update()
		{
			label.text = over ? text() : "";
		}

		private void OnDisable()
		{
			over = false;
			if (label != null)
			{
				label.text = "";
			}
		}
	}
}