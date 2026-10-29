using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HalfGripModsInstaller
{
	/// <summary>One mod found in a source, with everything the check said about it.</summary>
	internal sealed class ModCandidate
	{
		internal string SourceName = "";

		internal string SourceKey = "";

		/// <summary>The folder name it gets in BepInEx\HalfGripMods.</summary>
		internal string Folder = "";

		/// <summary>Where it sits after unzipping (a temporary folder).</summary>
		internal string Dir = "";

		internal string Id = "";

		internal string Name = "";

		internal string Version = "";

		internal string Author = "";

		internal string Description = "";

		internal string GameVersion = "";

		internal string Tools = "";

		/// <summary>The mod's picture: its mod.json "icon", else a sprites\*icon*.png. Null if it has none.</summary>
		internal string IconPath;

		internal string[] Requires = new string[0];

		/// <summary>Where it came from. Needed to remember the link when a suggested mod gets installed.</summary>
		internal Source Source;

		/// <summary>It comes from the remote recommended list, so it is a suggestion and is left out of "Install all".</summary>
		internal bool Recommended;

		/// <summary>The version in the game now, null if the mod is not installed.</summary>
		internal string Installed;

		/// <summary>When set, the mod is refused and this is why, in plain words.</summary>
		internal string Problem;

		internal readonly List<string> Warnings = new List<string>();

		internal string VersionText
		{
			get
			{
				if (Installed == null)
				{
					return Version;
				}
				return Installed == Version ? Version + " (same)" : Installed + " → " + Version;
			}
		}

		internal string Status
		{
			get
			{
				if (Problem != null)
				{
					return "Not installed: " + Problem;
				}
				string state = Installed == null ? "New" : (Installed == Version ? "Reinstalls" : "Updates");
				return Warnings.Count == 0 ? state : state + ". " + string.Join(" ", Warnings);
			}
		}
	}

	/// <summary>What happened to one link: the mods it holds, or why it failed.</summary>
	internal sealed class SourceResult
	{
		internal Source Source;

		internal string From = "";

		internal string Error;

		internal readonly List<ModCandidate> Mods = new List<ModCandidate>();
	}

	internal sealed class CheckResult
	{
		internal readonly List<SourceResult> Sources = new List<SourceResult>();

		internal ToolsPlan Tools;

		internal string ToolsError;

		internal string ToolsInstalled;

		/// <summary>Where the pictures of BepInEx and HalfGripTools were saved (from config.json). Null means the ones built into the installer.</summary>
		internal string BepInExIcon;

		internal string ToolsIcon;

		/// <summary>The BepInEx version in the game (BepInEx.dll), null if it is not installed.</summary>
		internal string BepInExVersion;

		/// <summary>The BepInEx release the installer wants the game to have (see Setup.PlanBepInEx).</summary>
		internal BepInExPlan BepInExTarget;

		internal bool BepInEx => BepInExVersion != null;

		/// <summary>BepInEx is missing, or older than the version in versions.json.</summary>
		internal bool BepInExNeeded => BepInExVersion == null || Setup.IsOlder(BepInExVersion, BepInExTarget.Version);

		/// <summary>HalfGripTools is missing or is not the newest release. False when its release could not be looked up.</summary>
		internal bool ToolsNeeded => Tools != null && !Setup.ToolsCurrent(ToolsInstalled, Tools);

		internal IEnumerable<ModCandidate> AllMods => Sources.SelectMany(s => s.Mods);

		/// <summary>The mods the player asked for (the official ones and their own links), without the suggestions.</summary>
		internal IEnumerable<ModCandidate> OwnMods => AllMods.Where(m => !m.Recommended);

		/// <summary>Links of the player's own that failed. A suggestion that fails is not reported: the player never asked for it.</summary>
		internal IEnumerable<SourceResult> FailedLinks => Sources.Where(s => s.Error != null && !s.Source.Recommended);

		/// <summary>True when there is something worth pressing Install for.</summary>
		internal bool CanInstall => Tools != null && AllMods.Any(m => m.Problem == null);
	}

	/// <summary>
	/// Downloads every source, unzips it and judges each mod it finds the way the game loader will. Nothing here touches the game
	/// folder; the unzipped files stay in the work folder until the install copies them.
	/// </summary>
	internal static class Checker
	{
		// A mod is source and pictures. Anything that runs by itself is refused, which is the point of "all code visible".
		private static readonly string[] Forbidden = { ".dll", ".exe", ".so", ".dylib", ".bat", ".cmd", ".ps1", ".vbs", ".msi", ".scr", ".com" };

		private const int MaxDepth = 3;

		internal static CheckResult Run(string gameDir, IList<Source> sources, string work, Action<string> report)
		{
			CheckResult result = new CheckResult { BepInExVersion = Setup.InstalledBepInEx(gameDir), BepInExTarget = Setup.PlanBepInEx(), ToolsInstalled = Setup.InstalledTools(gameDir) };
			result.BepInExIcon = Config.Picture(result.BepInExTarget.IconUrl);
			result.ToolsIcon = Config.Picture(Config.Current.ToolsIconUrl);
			report("Looking up HalfGripTools ...");
			try
			{
				result.Tools = Setup.PlanTools();
			}
			catch (Exception e)
			{
				result.ToolsError = Setup.Friendly(e);
			}

			List<InstalledMod> installed = Setup.ReadInstalled(gameDir);
			Memory memory = Memory.Load(gameDir);
			for (int i = 0; i < sources.Count; i++)
			{
				report("Checking " + (i + 1) + " of " + sources.Count + ": " + sources[i].Name + " ...");
				SourceResult one = new SourceResult { Source = sources[i] };
				try
				{
					CheckSource(one, Path.Combine(work, "s" + Theme.Fingerprint(sources[i].Text)), installed);
				}
				catch (Exception e)
				{
					one.Error = Setup.Friendly(e);
					one.Mods.Clear();
				}
				result.Sources.Add(one);
			}
			CrossCheck(result, installed, memory);
			return result;
		}

		private static void CheckSource(SourceResult one, string dir, List<InstalledMod> installed)
		{
			if (one.Source.LocalDir != "")
			{
				one.From = "folder on this PC";
				Find(one, one.Source.LocalDir, 0, one.Source.TopName, installed);
				if (one.Mods.Count == 0)
				{
					throw new PlainException("No mod found here. A mod is a folder with a mod.json file in it.");
				}
				return;
			}
			// The download is kept for an hour (see Cache). It is unpacked into the work folder once per session, and again
			// only when a newer download has replaced it.
			string zip = Path.Combine(Cache.Dir, "z" + Theme.Fingerprint(one.Source.Text) + ".zip");
			one.From = Fetch(one.Source, zip);
			string stamp = dir + ".from";
			if (!File.Exists(stamp) || File.GetLastWriteTimeUtc(zip) > File.GetLastWriteTimeUtc(stamp))
			{
				if (Directory.Exists(dir))
				{
					Directory.Delete(dir, true);
				}
				Setup.Extract(zip, dir, true);
				File.WriteAllText(stamp, one.From);
			}

			// GitHub zips hold one top folder named after the repository. Step into it.
			string root = dir;
			string[] top = Directory.GetDirectories(dir);
			if (top.Length == 1 && Directory.GetFiles(dir).Length == 0)
			{
				root = top[0];
			}
			if (one.Source.SubPath != "")
			{
				root = Path.Combine(root, one.Source.SubPath.Replace('/', '\\'));
				if (!Directory.Exists(root))
				{
					throw new PlainException("The folder '" + one.Source.SubPath + "' is not in this repository.");
				}
			}
			Find(one, root, 0, one.Source.TopName, installed);
			if (one.Mods.Count == 0)
			{
				throw new PlainException("No mod found here. A mod is a folder with a mod.json file in it.");
			}
		}

		/// <summary>
		/// Makes sure the zip of a source is in the cache, under an hour old unless "Check now" asked for a new one, and returns the
		/// words that describe it ("release v1.0", "main code"). If GitHub cannot be reached the saved zip is used instead.
		/// </summary>
		private static string Fetch(Source source, string zip)
		{
			string note = zip + ".from";
			if (Cache.Fresh(zip) && File.Exists(note))
			{
				Cache.Used(File.GetLastWriteTimeUtc(zip));
				return File.ReadAllText(note);
			}
			string part = zip + ".part";
			Resolved resolved;
			bool found;
			try
			{
				resolved = source.Resolve();
				found = Net.Download(resolved.ZipUrl, part);
			}
			catch (PlainException e) when (File.Exists(zip) && File.Exists(note))
			{
				Log.Write("Using the saved copy of " + source.Name + " (" + e.Message + ")");
				Cache.Used(File.GetLastWriteTimeUtc(zip));
				return File.ReadAllText(note) + ", saved copy";
			}
			if (!found)
			{
				throw new PlainException("Nothing was found at this link. Check the spelling, and that the repository is public.");
			}
			if (File.Exists(zip))
			{
				File.Delete(zip);
			}
			File.Move(part, zip);
			File.WriteAllText(note, resolved.Describe);
			Cache.Used(DateTime.UtcNow);
			return resolved.Describe;
		}

		/// <summary>Looks for mod.json up to three folders down. A folder that holds one is a mod and is not searched further.</summary>
		private static void Find(SourceResult one, string dir, int depth, string topName, List<InstalledMod> installed)
		{
			if (File.Exists(Path.Combine(dir, "mod.json")))
			{
				one.Mods.Add(Inspect(one, dir, depth == 0 ? topName : Path.GetFileName(dir), installed));
				return;
			}
			if (depth >= MaxDepth)
			{
				return;
			}
			foreach (string sub in Directory.GetDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
			{
				if (!Path.GetFileName(sub).StartsWith("."))
				{
					Find(one, sub, depth + 1, topName, installed);
				}
			}
		}

		private static ModCandidate Inspect(SourceResult one, string dir, string folder, List<InstalledMod> installed)
		{
			ModCandidate mod = new ModCandidate
			{
				SourceName = one.Source.Name,
				SourceKey = one.Source.Key,
				Source = one.Source,
				Recommended = one.Source.Recommended,
				Folder = folder,
				Dir = dir,
				Name = folder
			};
			string iconName = "";
			try
			{
				Dictionary<string, object> obj = MiniJson.Parse(File.ReadAllText(Path.Combine(dir, "mod.json"))) as Dictionary<string, object>;
				if (obj == null)
				{
					throw new FormatException("mod.json must be a JSON object");
				}
				mod.Id = MiniJson.String(obj, "id");
				mod.Name = MiniJson.String(obj, "name", folder);
				mod.Version = MiniJson.String(obj, "version", "1.0.0");
				mod.Author = MiniJson.String(obj, "author");
				mod.Description = MiniJson.String(obj, "description");
				mod.GameVersion = MiniJson.String(obj, "gameVersion");
				mod.Tools = MiniJson.String(obj, "tools");
				mod.Requires = MiniJson.List(obj, "requires").OfType<string>().ToArray();
				iconName = MiniJson.String(obj, "icon");
			}
			catch (FormatException e)
			{
				mod.Problem = "its mod.json is not valid (" + e.Message + ").";
				return mod;
			}
			if (!Regex.IsMatch(mod.Id, "^[a-z0-9_-]+$"))
			{
				mod.Problem = "its id must be lower case letters, digits, - or _ (it is '" + mod.Id + "').";
				return mod;
			}
			string compiled = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).FirstOrDefault(f => Forbidden.Contains(Path.GetExtension(f).ToLowerInvariant()));
			if (compiled != null)
			{
				mod.Problem = "it contains a program file (" + Path.GetFileName(compiled) + "). Mods here must be readable source only.";
				return mod;
			}
			bool hasContent = (Directory.Exists(Path.Combine(dir, "src")) && Directory.GetFiles(Path.Combine(dir, "src"), "*.cs", SearchOption.AllDirectories).Length > 0)
				|| (Directory.Exists(Path.Combine(dir, "items")) && Directory.GetFiles(Path.Combine(dir, "items"), "*.json").Length > 0);
			if (!hasContent)
			{
				mod.Warnings.Add("It has no code or items, so it may do nothing.");
			}
			mod.IconPath = FindIcon(dir, iconName);
			InstalledMod here = installed.FirstOrDefault(m => string.Equals(m.Folder, folder, StringComparison.OrdinalIgnoreCase));
			mod.Installed = here?.Version;
			return mod;
		}

		/// <summary>
		/// The picture of a mod: the .png its mod.json names (it must sit inside the mod's own folder), else the first sprites\*icon*.png,
		/// else null and the card shows a letter. The same rule the game's mod manager uses for "icon".
		/// </summary>
		private static string FindIcon(string dir, string named)
		{
			if (named.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !Path.IsPathRooted(named))
			{
				string root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
				string path = Path.GetFullPath(Path.Combine(dir, named.Replace('/', '\\')));
				if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
				{
					return path;
				}
			}
			string sprites = Path.Combine(dir, "sprites");
			return Directory.Exists(sprites) ? Directory.GetFiles(sprites, "*icon*.png").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault() : null;
		}

		/// <summary>The checks that need every source at once: clashes between mods, missing requirements, a loader that is too old.</summary>
		private static void CrossCheck(CheckResult result, List<InstalledMod> installed, Memory memory)
		{
			Dictionary<string, ModCandidate> ids = new Dictionary<string, ModCandidate>();
			Dictionary<string, ModCandidate> folders = new Dictionary<string, ModCandidate>(StringComparer.OrdinalIgnoreCase);
			foreach (ModCandidate mod in result.AllMods.Where(m => m.Problem == null))
			{
				if (ids.TryGetValue(mod.Id, out ModCandidate sameId))
				{
					mod.Problem = "its id '" + mod.Id + "' is already used by " + sameId.Name + " from " + sameId.SourceName + ".";
				}
				else if (folders.TryGetValue(mod.Folder, out ModCandidate sameFolder))
				{
					mod.Problem = "its folder name is already used by " + sameFolder.Name + " from " + sameFolder.SourceName + ".";
				}
				else
				{
					ids[mod.Id] = mod;
					folders[mod.Folder] = mod;
				}
			}

			foreach (ModCandidate mod in result.AllMods.Where(m => m.Problem == null))
			{
				InstalledMod otherFolder = installed.FirstOrDefault(m => m.Id == mod.Id && !string.Equals(m.Folder, mod.Folder, StringComparison.OrdinalIgnoreCase));
				if (otherFolder != null)
				{
					mod.Problem = "a mod with this id is already installed in the folder '" + otherFolder.Folder + "'. Remove that one first.";
				}
				else if (memory.Mods.TryGetValue(mod.Folder, out string from) && from != mod.SourceKey)
				{
					mod.Problem = "its folder is taken by an installed mod from " + from + ". Remove that one first.";
				}
				else if (result.Tools != null && Version.TryParse(mod.Tools, out Version needed) && result.Tools.Number != null && needed > result.Tools.Number)
				{
					mod.Problem = "it needs HalfGripTools " + mod.Tools + " or newer, and the newest is " + result.Tools.Tag + ".";
				}
			}

			HashSet<string> available = new HashSet<string>(result.AllMods.Where(m => m.Problem == null).Select(m => m.Id));
			available.UnionWith(installed.Select(m => m.Id));
			foreach (ModCandidate mod in result.AllMods.Where(m => m.Problem == null))
			{
				string[] missing = mod.Requires.Where(r => !available.Contains(r)).ToArray();
				if (missing.Length > 0)
				{
					mod.Warnings.Add("Needs " + string.Join(", ", missing) + ", which is not installed.");
				}
			}
		}
	}
}
