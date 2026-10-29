using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HalfGripModsInstaller
{
	/// <summary>Finds Half Grip Demo through Steam's own files. Reads the registry and Steam's library list, writes nothing.</summary>
	internal static class GameFolder
	{
		private const string AppId = "4163360";

		internal static bool IsGame(string folder)
		{
			return !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "HalfGrip.exe"));
		}

		internal static bool IsRunning()
		{
			return Process.GetProcessesByName("HalfGrip").Length > 0;
		}

		/// <summary>The game folder, or null if Steam does not know it (the player is then asked to pick it).</summary>
		internal static string Find()
		{
			List<string> roots = new List<string>();
			AddRoot(roots, Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null));
			AddRoot(roots, Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null));
			AddRoot(roots, Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null));
			roots.Add(@"C:\Program Files (x86)\Steam");
			roots.Add(@"C:\Program Files\Steam");

			// Steam keeps one list of all the library folders (the other drives too).
			List<string> libraries = new List<string>();
			foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				if (!Directory.Exists(root))
				{
					continue;
				}
				libraries.Add(root);
				string list = Path.Combine(root, @"steamapps\libraryfolders.vdf");
				if (File.Exists(list))
				{
					foreach (Match m in Regex.Matches(File.ReadAllText(list), "\"path\"\\s+\"([^\"]+)\""))
					{
						libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
					}
				}
			}
			foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				string folderName = "Half Grip Demo";
				string manifest = Path.Combine(library, "steamapps", "appmanifest_" + AppId + ".acf");
				if (File.Exists(manifest))
				{
					Match m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
					if (m.Success)
					{
						folderName = m.Groups[1].Value;
					}
				}
				string candidate = Path.Combine(library, "steamapps", "common", folderName);
				if (IsGame(candidate))
				{
					return candidate;
				}
			}
			return null;
		}

		private static void AddRoot(List<string> roots, object value)
		{
			if (value is string text && text.Length > 0)
			{
				roots.Add(text.Replace('/', '\\'));
			}
		}
	}
}
