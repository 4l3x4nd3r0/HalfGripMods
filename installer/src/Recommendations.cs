using System;
using System.Collections.Generic;
using System.Linq;

namespace HalfGripModsInstaller
{
	/// <summary>
	/// The mods the author suggests: a list of plain GitHub links in config.json. Editing that file on GitHub is all it takes to suggest
	/// another repository, no new installer. Only the link is written by hand; the name, version, author, description and picture
	/// come from the mod's own mod.json, and the line under the section's title is the repository's own description on GitHub.
	/// A suggestion is only shown. It is downloaded and checked like any pasted link, and nothing is installed until the player clicks.
	/// </summary>
	internal static class Recommendations
	{
		/// <summary>
		/// The suggested sources that are not in <paramref name="taken"/> already. A link that does not parse is skipped with a line in the
		/// log: the list is a bonus, never a reason for the installer to fail.
		/// </summary>
		internal static List<Source> Load(IEnumerable<Source> taken)
		{
			List<Source> found = new List<Source>();
			HashSet<string> seen = new HashSet<string>(taken.Select(s => s.Identity));
			foreach (string link in Config.Current.Recommended)
			{
				try
				{
					Source source = Source.Parse(link);
					if (!seen.Add(source.Identity))
					{
						continue;
					}
					source.Recommended = true;
					source.Label = source.Name;
					source.By = source.Owner;
					source.Note = Describe(source);
					found.Add(source);
				}
				catch (PlainException e)
				{
					Log.Write("Recommended link skipped: " + link + " - " + e.Message);
				}
			}
			Log.Write(found.Count + " recommended source(s)");
			return found;
		}

		/// <summary>The one-line description the repository has on GitHub, kept for an hour. Empty when it has none or GitHub cannot be asked.</summary>
		private static string Describe(Source source)
		{
			if (source.Owner == "")
			{
				return "";
			}
			try
			{
				string json = Cache.Text("repo-" + Theme.Fingerprint(source.Key) + ".json", () => Net.GetString("https://api.github.com/repos/" + source.Owner + "/" + source.Repo));
				if (json != null && MiniJson.Parse(json) is Dictionary<string, object> repo)
				{
					string text = MiniJson.String(repo, "description");
					return text.Length > 140 ? text.Substring(0, 137) + "..." : text;
				}
			}
			catch (Exception e)
			{
				Log.Write("No description for " + source.Name + " (" + e.Message + ")");
			}
			return "";
		}
	}
}
