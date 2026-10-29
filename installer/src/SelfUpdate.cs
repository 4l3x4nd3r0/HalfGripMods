using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HalfGripModsInstaller
{
	/// <summary>The web addresses the window opens: the pages of the repositories named in config.json. Nothing else is opened in the browser.</summary>
	internal static class Links
	{
		internal static string Repo => "https://github.com/" + Config.Current.ModsRepo;

		internal static string Releases => "https://github.com/" + Config.Current.InstallerRepo + "/releases";

		internal static string ToolsRepo => "https://github.com/" + Config.Current.ToolsRepo;
	}

	/// <summary>
	/// The installer is released by hand, only when it changes, so a player who keeps an old copy needs to be told. This looks at the
	/// newest release of the HalfGripMods repository (through the hourly cache) and, if its tag holds a higher version than this
	/// copy, the window shows a note with a link to the download page. It never downloads or runs anything.
	/// </summary>
	internal static class SelfUpdate
	{
		/// <summary>The version of a newer installer, or null when this one is the newest (or nothing could be read).</summary>
		internal static string NewerVersion()
		{
			try
			{
				string json = Cache.Text("installer-release.json", () => Net.GetString("https://api.github.com/repos/" + Config.Current.InstallerRepo + "/releases/latest"));
				if (json == null || !(MiniJson.Parse(json) is Dictionary<string, object> release))
				{
					return null;
				}
				Match m = Regex.Match(MiniJson.String(release, "tag_name"), "(\\d+)\\.(\\d+)\\.(\\d+)");
				if (!m.Success)
				{
					return null;
				}
				Version newest = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
				Version mine = typeof(SelfUpdate).Assembly.GetName().Version;
				return newest > new Version(mine.Major, mine.Minor, Math.Max(mine.Build, 0)) ? newest.ToString(3) : null;
			}
			catch (Exception e)
			{
				Log.Write("Could not look for a newer installer: " + e.Message);
				return null;
			}
		}
	}
}
