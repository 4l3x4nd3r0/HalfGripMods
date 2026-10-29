using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HalfGripModsInstaller
{
	/// <summary>
	/// Everything the author can change without a new installer: which BepInEx to install (with its checksum), where HalfGripTools and the
	/// mods live, the pictures of BepInEx and HalfGripTools, and the list of suggested mod links. It is one small file,
	/// installer\config.json on the main branch of HalfGripMods.
	/// </summary>
	internal sealed class Settings
	{
		internal BepInExPlan BepInEx;

		/// <summary>owner/repo of HalfGripTools, whose newest release is the loader.</summary>
		internal string ToolsRepo = "";

		internal string ToolsIconUrl = "";

		/// <summary>owner/repo of the official mods (and of this installer's own releases).</summary>
		internal string ModsRepo = "";

		internal string InstallerRepo = "";

		/// <summary>Links of suggested mods, exactly as they would be pasted into the add box.</summary>
		internal List<string> Recommended = new List<string>();

		private static readonly Regex RepoShape = new Regex("^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$");

		/// <summary>BepInEx's own releases are the only place BepInEx is downloaded from, whatever the file says.</summary>
		private const string BepInExReleases = "https://github.com/BepInEx/BepInEx/releases/download/";

		private const int MostRecommended = 12;

		/// <summary>
		/// Reads a config. A section that is missing is taken from <paramref name="fallback"/>; one that is there but wrong (a BepInEx address
		/// that is not an official release, a checksum that is not 64 hex digits, a repository that is not owner/repo) makes the whole file
		/// be refused, so a typo never half applies.
		/// </summary>
		internal static Settings Parse(string json, Settings fallback)
		{
			if (!(MiniJson.Parse(json) is Dictionary<string, object> file))
			{
				throw new FormatException("the file must be a JSON object");
			}
			Settings s = new Settings
			{
				BepInEx = fallback?.BepInEx,
				ToolsRepo = fallback?.ToolsRepo ?? "",
				ToolsIconUrl = fallback?.ToolsIconUrl ?? "",
				ModsRepo = fallback?.ModsRepo ?? "",
				InstallerRepo = fallback?.InstallerRepo ?? "",
				Recommended = fallback != null && !file.ContainsKey("recommended") ? fallback.Recommended.ToList() : new List<string>()
			};
			if (file.TryGetValue("bepinex", out object b) && b is Dictionary<string, object> bepinex)
			{
				BepInExPlan plan = new BepInExPlan { Version = MiniJson.String(bepinex, "version"), Url = MiniJson.String(bepinex, "url"), Sha = MiniJson.String(bepinex, "sha256"), IconUrl = MiniJson.String(bepinex, "icon"), FromFile = true };
				if (!Version.TryParse(plan.Version, out _))
				{
					throw new FormatException("the BepInEx version '" + plan.Version + "' is not a number like 5.4.23.5");
				}
				if (!plan.Url.StartsWith(BepInExReleases, StringComparison.Ordinal) || !plan.Url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || plan.Url.Contains(".."))
				{
					throw new FormatException("the BepInEx address must be an official BepInEx release (" + BepInExReleases + "...)");
				}
				if (!Regex.IsMatch(plan.Sha, "^[0-9A-Fa-f]{64}$"))
				{
					throw new FormatException("the BepInEx sha256 must be 64 hexadecimal characters");
				}
				CheckIcon(plan.IconUrl);
				s.BepInEx = plan;
			}
			if (file.TryGetValue("halfGripTools", out object t) && t is Dictionary<string, object> tools)
			{
				s.ToolsRepo = Repo(tools, "HalfGripTools", s.ToolsRepo);
				s.ToolsIconUrl = MiniJson.String(tools, "icon", s.ToolsIconUrl);
				CheckIcon(s.ToolsIconUrl);
			}
			if (file.TryGetValue("mods", out object m) && m is Dictionary<string, object> mods)
			{
				s.ModsRepo = Repo(mods, "mods", s.ModsRepo);
			}
			if (file.TryGetValue("installer", out object i) && i is Dictionary<string, object> installer)
			{
				s.InstallerRepo = Repo(installer, "installer", s.InstallerRepo);
			}
			// A plain link, or (as the first list was written) an object that has a "link".
			foreach (object entry in MiniJson.List(file, "recommended").Take(MostRecommended))
			{
				string link = entry as string ?? (entry is Dictionary<string, object> o ? MiniJson.String(o, "link") : "");
				if (link.Length > 0)
				{
					s.Recommended.Add(link);
				}
			}
			if (s.BepInEx == null || s.ToolsRepo == "" || s.ModsRepo == "" || s.InstallerRepo == "")
			{
				throw new FormatException("the file is missing the bepinex, halfGripTools, mods or installer part");
			}
			return s;
		}

		private static string Repo(Dictionary<string, object> section, string what, string fallback)
		{
			string repo = MiniJson.String(section, "repo", fallback);
			if (!RepoShape.IsMatch(repo))
			{
				throw new FormatException("the repository of " + what + " must look like owner/repo");
			}
			return repo;
		}

		/// <summary>A picture address is an https link on GitHub, nothing else. Empty means the picture built into the installer.</summary>
		internal static void CheckIcon(string url)
		{
			if (url.Length > 0 && !IsGitHubImage(url))
			{
				throw new FormatException("a picture address must be an https link on github.com or raw.githubusercontent.com");
			}
		}

		internal static bool IsGitHubImage(string url)
		{
			return Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && uri.Scheme == "https" && (uri.Host == "github.com" || uri.Host == "raw.githubusercontent.com");
		}
	}

	/// <summary>
	/// The saved copy of the config (config.json in the Half Games data folder) and how it is kept current. The first run writes it from
	/// the copy built into the program; after that a new one is asked for from GitHub when the last try is over an hour old, or on
	/// "Check now". An answer that is not valid is ignored and the saved copy stays, so a mistake on GitHub cannot break the installer.
	/// </summary>
	internal static class Config
	{
		internal const string DefaultUrl = "https://raw.githubusercontent.com/4l3x4nd3r0/HalfGripMods/main/installer/config.json";

		/// <summary>For testing: a local file or another address to read instead of the real one. Set by --config.</summary>
		internal static string Override = "";

		private static Settings current;

		private static Settings builtIn;

		internal static string SavedFile => Path.Combine(Cache.Root, "config.json");

		/// <summary>The marker for the last time GitHub was asked, so a failed try is not repeated every launch.</summary>
		private static string Stamp => Path.Combine(Cache.Root, "config.tried");

		/// <summary>The config the installer works with now. Read from the saved file the first time, without asking the network.</summary>
		internal static Settings Current
		{
			get
			{
				if (current == null)
				{
					current = ReadSaved();
				}
				return current;
			}
		}

		private static Settings BuiltIn()
		{
			if (builtIn == null)
			{
				using (Stream stream = typeof(Config).Assembly.GetManifestResourceStream("config.json"))
				using (StreamReader reader = new StreamReader(stream))
				{
					builtIn = Settings.Parse(reader.ReadToEnd(), null);
				}
			}
			return builtIn;
		}

		private static Settings ReadSaved()
		{
			try
			{
				if (!File.Exists(SavedFile))
				{
					// The first run: the saved copy starts as the one built into the program.
					Directory.CreateDirectory(Cache.Root);
					using (Stream stream = typeof(Config).Assembly.GetManifestResourceStream("config.json"))
					using (FileStream output = File.Create(SavedFile))
					{
						stream.CopyTo(output);
					}
					Log.Write("Wrote the first config.json to " + SavedFile);
					return BuiltIn();
				}
				return Settings.Parse(File.ReadAllText(SavedFile), BuiltIn());
			}
			catch (Exception e)
			{
				Log.Write("The saved config.json cannot be used (" + e.Message + "), using the one built in.");
				return BuiltIn();
			}
		}

		/// <summary>
		/// Asks GitHub for a newer config when the last try is over an hour old (or Cache.Force). Blocks, so call it off the window thread.
		/// </summary>
		internal static void Refresh()
		{
			Settings saved = Current;
			string location = Override != "" ? Override : DefaultUrl;
			if (Override == "" && Cache.Fresh(Stamp))
			{
				Cache.Used(File.GetLastWriteTimeUtc(SavedFile));
				return;
			}
			try
			{
				string json = File.Exists(location) ? File.ReadAllText(location) : Net.GetString(location);
				if (json == null)
				{
					throw new PlainException("there is no config.json at " + location);
				}
				Settings fresh = Settings.Parse(json, BuiltIn());
				Directory.CreateDirectory(Cache.Root);
				File.WriteAllText(SavedFile, json);
				current = fresh;
				Cache.Used(DateTime.UtcNow);
				Log.Write("config.json is current (from " + location + ")");
			}
			catch (Exception e)
			{
				Log.Write("config.json was not updated, the saved copy stays (" + e.Message + ")");
				Cache.Used(File.Exists(SavedFile) ? File.GetLastWriteTimeUtc(SavedFile) : DateTime.UtcNow);
				current = saved;
			}
			try
			{
				Directory.CreateDirectory(Cache.Root);
				File.WriteAllText(Stamp, DateTime.UtcNow.ToString("o"));
			}
			catch (IOException)
			{
				// Only a reminder of when it last tried.
			}
		}

		/// <summary>
		/// A picture from the config, downloaded into the cache for an hour. Returns its path, or null when there is none, it is not
		/// small and valid, or it cannot be fetched: the caller then uses the picture built into the program.
		/// </summary>
		internal static string Picture(string url)
		{
			if (string.IsNullOrEmpty(url) || !Settings.IsGitHubImage(url))
			{
				return null;
			}
			string file = Path.Combine(Cache.Dir, "i" + Theme.Fingerprint(url) + ".png");
			try
			{
				if (!Cache.Fresh(file))
				{
					string part = file + ".part";
					if (!Net.Download(url, part))
					{
						return File.Exists(file) ? file : null;
					}
					if (new FileInfo(part).Length > 256 * 1024)
					{
						File.Delete(part);
						throw new PlainException("the picture is bigger than 256 KB");
					}
					using (Image check = Image.FromFile(part))
					{
						if (check.Width < 8 || check.Height < 8 || check.Width > 1024 || check.Height > 1024)
						{
							throw new PlainException("the picture has a strange size");
						}
					}
					if (File.Exists(file))
					{
						File.Delete(file);
					}
					File.Move(part, file);
				}
				return file;
			}
			catch (Exception e)
			{
				Log.Write("Picture " + url + " not used (" + e.Message + ")");
				return File.Exists(file) ? file : null;
			}
		}
	}
}
