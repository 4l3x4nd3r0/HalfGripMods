using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace Multiplayer
{
	/// <summary>
	/// Translations, the way the game does them. The game shows every TextMeshPro text through <c>Loc.T(english)</c>: the English text is the
	/// key, and a table of "English,translation" rows is read for the chosen language. This mod adds its own rows to that table, from
	/// <c>lang\&lt;language&gt;.csv</c> in the mod folder (the language names are the game's own: <c>french.csv</c>, <c>german.csv</c>, ...). Nothing else
	/// is needed: a label that is plain English in the code is translated when its text is in the file. Text with a name or a number in it
	/// goes through <see cref="F"/> so the file has one row for the whole sentence, with {0} where the value goes.
	/// A missing file or row just leaves the English text.
	/// </summary>
	internal static class Tx
	{
		private static readonly FieldInfo TableField = AccessTools.Field(typeof(Loc), "table");
		private static readonly FieldInfo LowerField = AccessTools.Field(typeof(Loc), "byLower");

		/// <summary>The text in the player's language, or the English text itself.</summary>
		internal static string T(string english)
		{
			return Loc.T(english);
		}

		/// <summary>A sentence with values in it: the row in the language file is the format string, {0} {1} where the values go.</summary>
		internal static string F(string english, params object[] args)
		{
			return Loc.F(english, args);
		}

		/// <summary>Adds this mod's rows for a language to the game's table. The game has just (re)built the table for that language.</summary>
		internal static void Merge(string language)
		{
			try
			{
				if (language == null || language == Loc.English || Mod.Ctx == null)
				{
					return;
				}
				string path = Path.Combine(Path.Combine(Mod.Ctx.Folder, "lang"), language + ".csv");
				if (!File.Exists(path))
				{
					return;
				}
				Dictionary<string, string> table = TableField.GetValue(null) as Dictionary<string, string>;
				Dictionary<string, string> lower = LowerField.GetValue(null) as Dictionary<string, string>;
				if (table == null || lower == null)
				{
					Mod.Log.LogWarning("Translations: the game's text table was not found, so lang\\" + language + ".csv is not used.");
					return;
				}
				List<List<string>> rows = LocCsv.Parse(File.ReadAllText(path));
				int added = 0;
				for (int i = 1; i < rows.Count; i++)
				{
					if (rows[i].Count < 2)
					{
						continue;
					}
					string key = Loc.Key(rows[i][0]);
					if (key.Length == 0 || rows[i][1].Trim().Length == 0)
					{
						continue;
					}
					table[key] = rows[i][1];
					if (Loc.CasingOf(key) != Loc.Casing.Other)
					{
						string small = key.ToLowerInvariant();
						if (!lower.ContainsKey(small))
						{
							lower[small] = rows[i][1];
						}
					}
					added++;
				}
				Mod.Log.LogInfo("Translations: " + added + " row(s) from lang\\" + language + ".csv.");
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Translations for " + language + " could not be read: " + e.Message);
			}
		}
	}

	/// <summary>The game builds its text table in <c>Loc.Load(language)</c>, at start-up and on every language change.</summary>
	[HarmonyPatch(typeof(Loc), "Load")]
	internal static class TranslationsPatch
	{
		private static void Postfix(string lang)
		{
			Tx.Merge(lang);
		}
	}
}
