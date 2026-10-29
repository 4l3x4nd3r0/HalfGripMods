using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HalfGripModsInstaller
{
	/// <summary>Writes what the installer does to installer.log in the temp folder (and to the console in headless mode), for bug reports.</summary>
	internal static class Log
	{
		internal static readonly string LogFile = Path.Combine(Path.GetTempPath(), "HalfGrip-Mods-Installer.log");

		internal static bool ToConsole;

		internal static void Write(string line)
		{
			try
			{
				File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
			}
			catch (IOException)
			{
				// The log is a help, never a reason to stop.
			}
			if (ToConsole)
			{
				Console.WriteLine(line);
			}
		}
	}

	internal static class Program
	{
		[DllImport("kernel32.dll")]
		private static extern bool AttachConsole(int processId);

		/// <summary>
		/// No arguments: the window. For testing, and for people who like a command line:
		///   --game-dir &lt;folder&gt;   --add &lt;link&gt; (repeat)   --now (ask GitHub again)   --config &lt;file or address&gt;   --check   --install   (the last two run without a window)
		/// Only for trying things out: --data-dir &lt;folder&gt; (keep config and cache there instead of in LocalLow), --mods-dir &lt;folder&gt; (take the
		/// official mods from a folder on this PC), --render &lt;png&gt; --size WxH [--page &lt;card key|how|about&gt;] (draw the window into a picture).
		/// </summary>
		[STAThread]
		private static int Main(string[] args)
		{
			string gameDir = null;
			List<string> links = new List<string>();
			bool check = false;
			bool install = false;
			string render = null;
			Size size = new Size(1100, 720);
			string page = "";
			for (int i = 0; i < args.Length; i++)
			{
				switch (args[i])
				{
					case "--game-dir" when i + 1 < args.Length:
						gameDir = args[++i];
						break;
					case "--add" when i + 1 < args.Length:
						links.Add(args[++i]);
						break;
					case "--config" when i + 1 < args.Length:
						Config.Override = args[++i];
						break;
					case "--data-dir" when i + 1 < args.Length:
						Cache.RootOverride = Path.GetFullPath(args[++i]);
						break;
					case "--mods-dir" when i + 1 < args.Length:
						Source.OfficialLocalDir = Path.GetFullPath(args[++i]);
						break;
					case "--render" when i + 1 < args.Length:
						render = Path.GetFullPath(args[++i]);
						break;
					case "--size" when i + 1 < args.Length:
						string[] wh = args[++i].ToLowerInvariant().Split('x');
						size = new Size(int.Parse(wh[0]), int.Parse(wh[1]));
						break;
					case "--page" when i + 1 < args.Length:
						page = args[++i];
						break;
					case "--now":
						Cache.Force = true;
						break;
					case "--check":
						check = true;
						break;
					case "--install":
						install = true;
						break;
				}
			}
			if (check || install)
			{
				AttachConsole(-1);
				Log.ToConsole = true;
				return Headless(gameDir, links, install);
			}
			Cache.Sweep();
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			if (render != null)
			{
				return Render(gameDir, links, render, size, page);
			}
			Application.Run(new MainForm(gameDir, links));
			return 0;
		}

		/// <summary>
		/// Draws the window into a PNG without showing it to anyone: the form is placed far off screen and painted with
		/// DrawToBitmap, so nothing is captured from the desktop. For looking at the layout at different sizes.
		/// </summary>
		private static int Render(string gameDir, List<string> links, string path, Size size, string page)
		{
			using (MainForm form = new MainForm(gameDir, links))
			{
				form.StartPosition = FormStartPosition.Manual;
				form.Location = new Point(-32000, -32000);
				form.ShowInTaskbar = false;
				form.Show();
				DateTime until = DateTime.UtcNow.AddSeconds(90);
				while (!form.Settled && DateTime.UtcNow < until)
				{
					Application.DoEvents();
					System.Threading.Thread.Sleep(30);
				}
				form.PrepareRender(size, page);
				for (int i = 0; i < 12; i++)
				{
					Application.DoEvents();
					System.Threading.Thread.Sleep(20);
				}
				using (Bitmap picture = form.Render())
				{
					picture.Save(path, ImageFormat.Png);
				}
				Console.WriteLine("wrote " + path);
				form.Close();
			}
			return 0;
		}

		private static int Headless(string gameDir, List<string> links, bool install)
		{
			string work = Path.Combine(Path.GetTempPath(), "HalfGripInstaller-" + Guid.NewGuid().ToString("N"));
			try
			{
				gameDir = gameDir ?? GameFolder.Find();
				if (!GameFolder.IsGame(gameDir))
				{
					throw new PlainException("HalfGrip.exe was not found in '" + gameDir + "'. Use --game-dir <folder with HalfGrip.exe>.");
				}
				gameDir = Path.GetFullPath(gameDir);
				Log.Write("Game folder: " + gameDir);
				Config.Refresh();
				List<Source> sources = new List<Source> { Source.Official };
				foreach (string link in links)
				{
					try
					{
						sources.Add(Source.Parse(link));
					}
					catch (PlainException e)
					{
						Log.Write("Link not used: " + link + " - " + e.Message);
					}
				}
				Directory.CreateDirectory(work);
				Cache.StartCheck();
				List<Source> own = sources.ToList();
				sources.AddRange(Recommendations.Load(own));
				CheckResult result = Checker.Run(gameDir, sources, work, Log.Write);
				Print(result);
				if (!install)
				{
					return 0;
				}
				if (!result.CanInstall)
				{
					throw new PlainException(result.ToolsError ?? "Nothing can be installed.");
				}
				// Like "Install all" in the window: suggestions are not installed unless someone picks them.
				List<ModCandidate> mods = result.OwnMods.Where(m => m.Problem == null).ToList();
				foreach (string line in Setup.Install(gameDir, result.BepInExTarget, result.BepInExNeeded, result.Tools, result.ToolsNeeded, mods, own.Skip(1).ToList(), work, Log.Write))
				{
					Log.Write(line);
				}
				Log.Write("Done. " + mods.Count + " mod(s) installed.");
				return 0;
			}
			catch (Exception e)
			{
				Log.Write("Stopped: " + Setup.Friendly(e));
				return 1;
			}
			finally
			{
				if (Directory.Exists(work))
				{
					Directory.Delete(work, true);
				}
			}
		}

		private static void Print(CheckResult result)
		{
			Log.Write("BepInEx: " + (result.BepInExVersion ?? "none") + " -> " + result.BepInExTarget.Version + (result.BepInExTarget.FromFile ? " (from config.json)" : " (built in)") + (result.BepInExNeeded ? ", will be " + (result.BepInEx ? "updated" : "installed") : ", current"));
			Log.Write("HalfGripTools: " + (result.Tools != null ? (result.ToolsInstalled ?? "none") + " -> " + result.Tools.Tag : "ERROR " + result.ToolsError));
			foreach (SourceResult source in result.Sources)
			{
				Log.Write("== " + (source.Source.Recommended ? "RECOMMENDED " + source.Source.Label + " - " : "") + source.Source.Name + (source.From != "" ? " (" + source.From + ")" : ""));
				if (source.Error != null)
				{
					Log.Write("   FAILED: " + source.Error);
				}
				foreach (ModCandidate mod in source.Mods)
				{
					Log.Write("   [" + (mod.Problem == null ? "x" : " ") + "] " + mod.Name + " " + mod.VersionText + " by " + mod.Author + (mod.IconPath != null ? " [icon]" : "") + " - " + mod.Status);
				}
			}
		}
	}
}
