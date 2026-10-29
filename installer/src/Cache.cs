using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HalfGripModsInstaller
{
	/// <summary>
	/// What GitHub said and what was downloaded, kept for an hour in %LOCALAPPDATA%\HalfGrip-Mods-Installer\cache. Opening the
	/// installer again within the hour asks GitHub nothing and starts at once; after an hour it asks again; "Check now" asks
	/// again whatever the age. When GitHub cannot be reached the saved copy is used, so the window still has something to show.
	/// Only downloads and answers from GitHub are kept here, nothing about the player.
	/// </summary>
	internal static class Cache
	{
		internal static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);

		/// <summary>True for the length of a "Check now": everything is asked again.</summary>
		internal static bool Force;

		private static DateTime oldest = DateTime.MaxValue;

		/// <summary>For testing: keep everything in this folder instead of the real one. Set by --data-dir.</summary>
		internal static string RootOverride = "";

		/// <summary>
		/// Where the installer keeps its own files, next to the game's own data: LocalLow\Half Games\HalfGripMods. It holds config.json,
		/// the cache and a few markers. Nothing about the player is kept there.
		/// </summary>
		internal static string Root
		{
			get
			{
				if (RootOverride != "")
				{
					return RootOverride;
				}
				string low = null;
				try
				{
					if (SHGetKnownFolderPath(LocalAppDataLow, 0, IntPtr.Zero, out string path) == 0)
					{
						low = path;
					}
				}
				catch (Exception)
				{
					// Falls back to the usual place below.
				}
				if (string.IsNullOrEmpty(low))
				{
					low = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
				}
				return Path.Combine(low, "Half Games", "HalfGripMods");
			}
		}

		private static readonly Guid LocalAppDataLow = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");

		[DllImport("shell32.dll")]
		private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid folder, uint flags, IntPtr token, [MarshalAs(UnmanagedType.LPWStr)] out string path);

		internal static string Dir
		{
			get
			{
				string dir = Path.Combine(Root, "cache");
				Directory.CreateDirectory(dir);
				return dir;
			}
		}

		/// <summary>Called at the start of every check: forget how old the last one was.</summary>
		internal static void StartCheck()
		{
			oldest = DateTime.MaxValue;
		}

		/// <summary>Remembers when the data a check used was fetched; the oldest one is what "updated 12 minutes ago" shows.</summary>
		internal static void Used(DateTime fetchedUtc)
		{
			if (fetchedUtc < oldest)
			{
				oldest = fetchedUtc;
			}
		}

		/// <summary>When the data of the last check was fetched from GitHub (UTC).</summary>
		internal static DateTime FetchedUtc => oldest == DateTime.MaxValue ? DateTime.UtcNow : oldest;

		internal static bool Fresh(string file)
		{
			return !Force && File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < MaxAge;
		}

		/// <summary>
		/// A GitHub answer as text: the saved one if it is under an hour old, else a new one from <paramref name="fetch"/> (null when the
		/// page does not exist). If the fetch fails and an older copy exists, the older copy is used.
		/// </summary>
		internal static string Text(string name, Func<string> fetch)
		{
			string file = Path.Combine(Dir, name);
			if (Fresh(file))
			{
				Used(File.GetLastWriteTimeUtc(file));
				return File.ReadAllText(file);
			}
			try
			{
				string text = fetch();
				if (text != null)
				{
					File.WriteAllText(file, text);
					Used(DateTime.UtcNow);
				}
				return text;
			}
			catch (PlainException e) when (File.Exists(file))
			{
				Log.Write("Using the saved " + name + " (" + e.Message + ")");
				Used(File.GetLastWriteTimeUtc(file));
				return File.ReadAllText(file);
			}
		}

		/// <summary>Throws away saved files nobody has used for a week, so the cache never grows.</summary>
		internal static void Sweep()
		{
			try
			{
				// The first version kept its cache in LocalAppData. It was only the installer's own copy of GitHub's answers.
				string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HalfGrip-Mods-Installer");
				if (RootOverride == "" && Directory.Exists(old))
				{
					Directory.Delete(old, true);
				}
				foreach (string file in Directory.GetFiles(Dir))
				{
					if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromDays(7))
					{
						File.Delete(file);
					}
				}
			}
			catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
			{
				// A cache that cannot be cleaned is no reason to stop.
			}
		}
	}
}
