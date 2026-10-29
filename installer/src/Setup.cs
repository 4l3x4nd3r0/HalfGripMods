using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HalfGripModsInstaller
{
	/// <summary>The newest HalfGripTools release, looked up before anything is installed.</summary>
	internal sealed class ToolsPlan
	{
		internal string Tag = "";

		internal string ZipName = "";

		internal string ZipUrl = "";

		internal string SumsUrl = "";

		/// <summary>The loader version as a number, for a mod's "tools" field. Null if the tag is not a version.</summary>
		internal Version Number
		{
			get
			{
				return Version.TryParse(Tag.TrimStart('v', 'V'), out Version v) ? v : null;
			}
		}
	}

	/// <summary>The BepInEx release to have in the game: from installer\versions.json on GitHub, or the one built into this installer.</summary>
	internal sealed class BepInExPlan
	{
		/// <summary>The version of BepInEx.dll inside the zip, for example 5.4.23.5. It is what is compared with the game's copy.</summary>
		internal string Version = "";

		internal string Url = "";

		internal string Sha = "";

		/// <summary>The picture address from the config, or empty for the one built into the installer.</summary>
		internal string IconUrl = "";

		/// <summary>True when the numbers came from config.json (the saved copy or a fresh one), false for the copy built into the installer.</summary>
		internal bool FromFile;
	}

	/// <summary>A mod folder that is already in the game.</summary>
	internal sealed class InstalledMod
	{
		internal string Folder = "";

		internal string Id = "";

		internal string Version = "";
	}

	/// <summary>
	/// What the installer remembers, as a plain text file next to the mods: the links the player added and where each
	/// mod folder came from. The game loader only looks at folders, so this file does nothing in the game.
	/// </summary>
	internal sealed class Memory
	{
		internal readonly List<string> Links = new List<string>();

		/// <summary>Mod folder name to the source key it was installed from.</summary>
		internal readonly Dictionary<string, string> Mods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		private static string FileFor(string gameDir)
		{
			return Path.Combine(gameDir, "BepInEx", "HalfGripMods", "installer-sources.txt");
		}

		internal static Memory Load(string gameDir)
		{
			Memory memory = new Memory();
			string file = FileFor(gameDir);
			if (!File.Exists(file))
			{
				return memory;
			}
			foreach (string line in File.ReadAllLines(file))
			{
				string[] parts = line.Split('\t');
				if (parts.Length == 2 && parts[0] == "link")
				{
					memory.Links.Add(parts[1]);
				}
				else if (parts.Length == 3 && parts[0] == "mod")
				{
					memory.Mods[parts[1]] = parts[2];
				}
			}
			return memory;
		}

		internal void Save(string gameDir)
		{
			StringBuilder text = new StringBuilder();
			text.AppendLine("# Written by the Half Grip mods installer. Links you added and where each mod folder came from. Safe to delete.");
			foreach (string link in Links)
			{
				text.AppendLine("link\t" + link);
			}
			foreach (KeyValuePair<string, string> mod in Mods)
			{
				text.AppendLine("mod\t" + mod.Key + "\t" + mod.Value);
			}
			Directory.CreateDirectory(Path.GetDirectoryName(FileFor(gameDir)));
			File.WriteAllText(FileFor(gameDir), text.ToString());
		}
	}

	/// <summary>Everything that changes the game folder: BepInEx, HalfGripTools, the mod folders, and taking them out again.</summary>
	internal static class Setup
	{
		internal static string ModsDir(string gameDir) => Path.Combine(gameDir, "BepInEx", "HalfGripMods");

		internal static bool BepInExInstalled(string gameDir)
		{
			return File.Exists(Path.Combine(gameDir, "BepInEx", "core", "BepInEx.dll")) && File.Exists(Path.Combine(gameDir, "winhttp.dll"));
		}

		/// <summary>The BepInEx version in the game (from BepInEx.dll), or null if BepInEx is not there.</summary>
		internal static string InstalledBepInEx(string gameDir)
		{
			if (!BepInExInstalled(gameDir))
			{
				return null;
			}
			string version = FileVersionInfo.GetVersionInfo(Path.Combine(gameDir, "BepInEx", "core", "BepInEx.dll")).FileVersion;
			return string.IsNullOrEmpty(version) ? "installed" : version;
		}

		/// <summary>True when <paramref name="installed"/> is a lower version than <paramref name="wanted"/>. A version that cannot be read is left alone.</summary>
		internal static bool IsOlder(string installed, string wanted)
		{
			return Version.TryParse(installed, out Version have) && Version.TryParse(wanted, out Version want) && have < want;
		}

		/// <summary>Which BepInEx to have: the one config.json names (already checked when it was read, see Settings.Parse).</summary>
		internal static BepInExPlan PlanBepInEx()
		{
			return Config.Current.BepInEx;
		}

		/// <summary>The HalfGripTools version in the game, or null if it is not installed.</summary>
		internal static string InstalledTools(string gameDir)
		{
			string dll = Path.Combine(gameDir, "BepInEx", "plugins", "HalfGripTools", "HalfGripTools.dll");
			if (!File.Exists(dll))
			{
				return null;
			}
			string version = FileVersionInfo.GetVersionInfo(dll).FileVersion;
			if (string.IsNullOrEmpty(version))
			{
				return "installed";
			}
			// The file says 1.0.4.0 where the release says v1.0.4.
			return Version.TryParse(version, out Version parsed) ? parsed.Major + "." + parsed.Minor + "." + Math.Max(parsed.Build, 0) : version;
		}

		internal static ToolsPlan PlanTools()
		{
			string json = Cache.Text("tools-release.json", () => Net.GetString("https://api.github.com/repos/" + Config.Current.ToolsRepo + "/releases/latest"));
			if (json == null || !(MiniJson.Parse(json) is Dictionary<string, object> release))
			{
				throw new PlainException("HalfGripTools has no release to download yet.");
			}
			ToolsPlan plan = new ToolsPlan { Tag = MiniJson.String(release, "tag_name") };
			foreach (Dictionary<string, object> asset in MiniJson.List(release, "assets").OfType<Dictionary<string, object>>())
			{
				string name = MiniJson.String(asset, "name");
				string url = MiniJson.String(asset, "browser_download_url");
				if (Regex.IsMatch(name, "^HalfGripTools-.*\\.zip$"))
				{
					plan.ZipName = name;
					plan.ZipUrl = url;
				}
				else if (name == "SHA256SUMS.txt")
				{
					plan.SumsUrl = url;
				}
			}
			if (plan.ZipUrl == "" || plan.SumsUrl == "")
			{
				throw new PlainException("The newest HalfGripTools release has no zip or no SHA256SUMS.txt, so it cannot be checked. Please report it on the HalfGripTools GitHub page.");
			}
			return plan;
		}

		/// <summary>True when the HalfGripTools in the game is the release that would be installed (the file says 1.0.4, the tag v1.0.4).</summary>
		internal static bool ToolsCurrent(string installed, ToolsPlan plan)
		{
			return installed != null && plan != null && string.Equals(installed.TrimStart('v', 'V'), plan.Tag.TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Reads mod.json of every mod folder in the game.</summary>
		internal static List<InstalledMod> ReadInstalled(string gameDir)
		{
			List<InstalledMod> found = new List<InstalledMod>();
			string root = ModsDir(gameDir);
			if (!Directory.Exists(root))
			{
				return found;
			}
			foreach (string dir in Directory.GetDirectories(root))
			{
				string file = Path.Combine(dir, "mod.json");
				if (!File.Exists(file))
				{
					continue;
				}
				InstalledMod mod = new InstalledMod { Folder = Path.GetFileName(dir) };
				try
				{
					Dictionary<string, object> obj = MiniJson.Parse(File.ReadAllText(file)) as Dictionary<string, object>;
					if (obj != null)
					{
						mod.Id = MiniJson.String(obj, "id");
						mod.Version = MiniJson.String(obj, "version", "1.0.0");
					}
				}
				catch (FormatException)
				{
					// A broken mod.json in the game: the folder still counts as taken.
				}
				found.Add(mod);
			}
			return found;
		}

		/// <summary>Unzips without trusting the file: nothing may land outside the destination folder.</summary>
		internal static void Extract(string zip, string destination, bool overwrite, Func<string, bool> keepIfThere = null)
		{
			string root = Path.GetFullPath(destination).TrimEnd('\\');
			using (ZipArchive archive = ZipFile.OpenRead(zip))
			{
				foreach (ZipArchiveEntry entry in archive.Entries)
				{
					string target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', '\\')));
					if (!target.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
					{
						throw new PlainException("The download holds a file that wants to leave its folder, so it was refused. Nothing was installed from it.");
					}
					if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
					{
						Directory.CreateDirectory(target);
						continue;
					}
					Directory.CreateDirectory(Path.GetDirectoryName(target));
					if (keepIfThere != null && File.Exists(target) && keepIfThere(Path.GetFileName(target)))
					{
						continue;
					}
					entry.ExtractToFile(target, overwrite);
				}
			}
		}

		private static void CopyFolder(string from, string to)
		{
			if (Directory.Exists(to))
			{
				Directory.Delete(to, true);
			}
			foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
			{
				Directory.CreateDirectory(Path.Combine(to, dir.Substring(from.Length).TrimStart('\\')));
			}
			Directory.CreateDirectory(to);
			foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
			{
				File.Copy(file, Path.Combine(to, file.Substring(from.Length).TrimStart('\\')), true);
			}
		}

		/// <summary>The messages for the errors a player can fix, so they never see a stack trace.</summary>
		internal static string Friendly(Exception e)
		{
			if (e is PlainException)
			{
				return e.Message;
			}
			if (e is UnauthorizedAccessException)
			{
				return "Windows would not let the installer change the game folder. Close the game, then right click the installer and choose Run as administrator.";
			}
			if (e is IOException)
			{
				return "A file could not be written (" + e.Message + "). Close the game and anything that has the game folder open, then try again.";
			}
			return "Something unexpected went wrong: " + e.Message;
		}

		/// <summary>
		/// Puts in BepInEx and HalfGripTools when asked to, then the given mods, and remembers the links. Returns what it did, one line each.
		/// </summary>
		internal static List<string> Install(string gameDir, BepInExPlan bepinex, bool installBepInEx, ToolsPlan tools, bool installTools, List<ModCandidate> mods, List<Source> extraSources, string work, Action<string> report)
		{
			List<string> done = new List<string>();
			if (GameFolder.IsRunning())
			{
				throw new PlainException("Half Grip is running. Close the game and try again.");
			}

			if (!installBepInEx)
			{
				done.Add("BepInEx was left as it is.");
			}
			else
			{
				bool update = BepInExInstalled(gameDir);
				report("Downloading BepInEx " + bepinex.Version + " ...");
				string zip = Path.Combine(work, "bepinex.zip");
				if (!Net.Download(bepinex.Url, zip))
				{
					throw new PlainException("BepInEx could not be downloaded (the page is gone).");
				}
				Net.CheckHash(zip, bepinex.Sha, "BepInEx");
				// An update keeps the player's own doorstop_config.ini; everything else in the zip is replaced.
				Extract(zip, gameDir, true, name => update && string.Equals(name, "doorstop_config.ini", StringComparison.OrdinalIgnoreCase));
				done.Add((update ? "Updated BepInEx to " : "Installed BepInEx ") + bepinex.Version + " (checksum matched).");
			}

			if (!installTools || tools == null)
			{
				done.Add("HalfGripTools was left as it is.");
			}
			else
			{
				report("Downloading HalfGripTools " + tools.Tag + " ...");
				string toolsZip = Path.Combine(work, tools.ZipName);
				string sums = Path.Combine(work, "SHA256SUMS.txt");
				if (!Net.Download(tools.ZipUrl, toolsZip) || !Net.Download(tools.SumsUrl, sums))
				{
					throw new PlainException("HalfGripTools could not be downloaded (the page is gone).");
				}
				string expected = null;
				foreach (string line in File.ReadAllLines(sums))
				{
					Match m = Regex.Match(line, "^\\s*([0-9A-Fa-f]{64})\\s+\\*?(.+?)\\s*$");
					if (m.Success && m.Groups[2].Value == tools.ZipName)
					{
						expected = m.Groups[1].Value;
					}
				}
				if (expected == null)
				{
					throw new PlainException("SHA256SUMS.txt has no line for " + tools.ZipName + ", so HalfGripTools cannot be checked.");
				}
				Net.CheckHash(toolsZip, expected, "HalfGripTools");
				Extract(toolsZip, gameDir, true);
				done.Add("Installed HalfGripTools " + tools.Tag + " (checksum matched).");
			}

			Memory memory = Memory.Load(gameDir);
			memory.Links.Clear();
			memory.Links.AddRange(extraSources.Select(s => s.Text));
			foreach (ModCandidate mod in mods)
			{
				report("Installing " + mod.Name + " ...");
				CopyFolder(mod.Dir, Path.Combine(ModsDir(gameDir), mod.Folder));
				memory.Mods[mod.Folder] = mod.SourceKey;
				done.Add("Installed mod " + mod.Name + " " + mod.Version + ".");
			}
			memory.Save(gameDir);

			// Only what was put in this time is checked: installing HalfGripTools alone must not complain about BepInEx.
			List<string> expectedFiles = new List<string>();
			if (installBepInEx)
			{
				expectedFiles.Add(@"BepInEx\core\BepInEx.dll");
			}
			if (installTools && tools != null)
			{
				expectedFiles.Add(@"BepInEx\plugins\HalfGripTools\HalfGripTools.dll");
			}
			foreach (string file in expectedFiles)
			{
				if (!File.Exists(Path.Combine(gameDir, file)))
				{
					throw new PlainException("Something went wrong: " + file + " is missing after the install.");
				}
			}
			return done;
		}

		/// <summary>What <see cref="Remove"/> would delete, for the question the player is asked.</summary>
		internal static string Describe(string gameDir)
		{
			List<string> folders = ReadInstalled(gameDir).Select(m => m.Folder).ToList();
			bool tools = Directory.Exists(Path.Combine(gameDir, "BepInEx", "plugins", "HalfGripTools"));
			if (!tools && folders.Count == 0)
			{
				return null;
			}
			return "This removes HalfGripTools and these mods: " + (folders.Count > 0 ? string.Join(", ", folders) : "(none)") +
				"\nIt also removes their settings and cache from BepInEx\\config and BepInEx\\cache. Your saves are not touched.";
		}

		/// <summary>Takes out HalfGripTools, the mods, their settings and cache. BepInEx only when asked. Never touches saves.</summary>
		internal static void Remove(string gameDir, bool removeBepInEx)
		{
			if (GameFolder.IsRunning())
			{
				throw new PlainException("Half Grip is running. Close the game and try again.");
			}
			string bepinex = Path.Combine(gameDir, "BepInEx");
			foreach (string path in new[] { ModsDir(gameDir), Path.Combine(bepinex, "plugins", "HalfGripTools"), Path.Combine(bepinex, "cache", "HalfGripMods") })
			{
				if (Directory.Exists(path))
				{
					Directory.Delete(path, true);
				}
			}
			string config = Path.Combine(bepinex, "config");
			if (Directory.Exists(config))
			{
				foreach (string pattern in new[] { "halfgrip.*", "HalfGripTools.*" })
				{
					foreach (string file in Directory.GetFiles(config, pattern))
					{
						File.Delete(file);
					}
				}
			}
			if (removeBepInEx)
			{
				if (Directory.Exists(bepinex))
				{
					Directory.Delete(bepinex, true);
				}
				foreach (string file in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" })
				{
					string path = Path.Combine(gameDir, file);
					if (File.Exists(path))
					{
						File.Delete(path);
					}
				}
			}
		}

		/// <summary>Other plugins that live in BepInEx, named in the question about removing BepInEx.</summary>
		internal static string[] OtherPlugins(string gameDir)
		{
			string plugins = Path.Combine(gameDir, "BepInEx", "plugins");
			if (!Directory.Exists(plugins))
			{
				return new string[0];
			}
			return Directory.GetFileSystemEntries(plugins).Select(Path.GetFileName).Where(n => n != "HalfGripTools").ToArray();
		}
	}
}
