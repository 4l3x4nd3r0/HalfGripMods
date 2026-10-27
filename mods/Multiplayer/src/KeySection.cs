using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// A "Multiplayer" mini section in Settings > Controls. The game lists its keys section by section from the fixed enum
	/// <see cref="Controls.Section"/>, so a mod key registered under a value the game does not loop over is not listed at all; this adds
	/// those rows itself, after the game's own, with the game's own row, rebind and "Default" button code. If any of the private pieces
	/// it needs are not found (the game changed), <see cref="Supported"/> is false and the keys go to the Race section instead.
	/// </summary>
	internal static class KeySection
	{
		/// <summary>A section number the game's own loop never reaches.</summary>
		internal const Controls.Section Own = (Controls.Section)10;

		internal sealed class Entry
		{
			internal KeyCode Key;
			internal string Label;
		}

		internal static readonly List<Entry> Entries = new List<Entry>();

		private static readonly Type Ui = typeof(ControlsSettingsUI);
		private static readonly MethodInfo Header = AccessTools.Method(Ui, "Header", new[] { typeof(string), typeof(float).MakeByRefType(), typeof(bool) });
		private static readonly MethodInfo Row = AccessTools.Method(Ui, "Row", new[] { typeof(string), typeof(string), typeof(float).MakeByRefType(), typeof(Action<KeyCode>), typeof(bool), typeof(bool), typeof(bool) });
		private static readonly MethodInfo Side = AccessTools.Method(Ui, "SideButton", new[] { typeof(Transform), typeof(string), typeof(Action) });
		private static readonly MethodInfo Rebind = AccessTools.Method(Ui, "Rebind", new[] { typeof(KeyCode), typeof(KeyCode) });
		private static readonly FieldInfo Content = AccessTools.Field(Ui, "content");
		private static readonly MethodInfo Fill = AccessTools.Method(Ui, "Fill");

		internal static bool Supported
		{
			get { return Header != null && Row != null && Side != null && Rebind != null && Content != null && Fill != null; }
		}

		private static bool warned;

		/// <summary>After the game has filled the list: our header and rows at the end, and the scroll area made long enough.</summary>
		internal static void Append(ControlsSettingsUI ui)
		{
			if (Entries.Count == 0 || !Supported)
			{
				return;
			}
			try
			{
				RectTransform content = (RectTransform)Content.GetValue(ui);
				float y = content.sizeDelta.y;
				object[] header = { "Multiplayer", y, false };
				Header.Invoke(ui, header);
				y = (float)header[1];
				foreach (Entry entry in Entries)
				{
					KeyCode key = entry.Key;
					Action<KeyCode> onKey = pressed => Rebind.Invoke(ui, new object[] { key, pressed });
					object[] row = { entry.Label, Controls.Name(Controls.Physical(key)), y, onKey, false, false, false };
					RectTransform made = (RectTransform)Row.Invoke(ui, row);
					y = (float)row[2];
					if (Controls.Physical(key) != key)
					{
						Action reset = () => Rebind.Invoke(ui, new object[] { key, key });
						Side.Invoke(ui, new object[] { made, Loc.T("Default"), reset });
					}
				}
				content.sizeDelta = new Vector2(0f, y);
			}
			catch (Exception e)
			{
				if (!warned)
				{
					warned = true;
					Mod.Log.LogWarning("The Multiplayer section of Settings > Controls could not be built: " + e.Message);
				}
			}
		}
	}

	/// <summary>The game fills its controls list in <c>ControlsSettingsUI.Fill</c> (on opening it and after every rebind).</summary>
	[HarmonyPatch(typeof(ControlsSettingsUI), "Fill")]
	internal static class KeySectionPatch
	{
		private static void Postfix(ControlsSettingsUI __instance)
		{
			KeySection.Append(__instance);
		}
	}
}
