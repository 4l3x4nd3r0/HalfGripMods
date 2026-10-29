using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HalfGripModsInstaller
{
	/// <summary>
	/// The window. It decides what happens and tells TopBar (title, tabs, search, add box), HubCanvas (the cards), StatusBar and
	/// DetailsPanel what to show; the rules themselves live in Checker and Setup. It checks by itself when it opens, so there is nothing
	/// to press first. The window can be any size from 640x360 up (the sizes the game itself runs windowed at): the views reflow, and
	/// below 960x600 the whole thing also shrinks a little.
	/// </summary>
	internal sealed class MainForm : Form, IMessageFilter
	{
		private sealed class ScrollHost : Panel
		{
			internal ScrollHost()
			{
				AutoScroll = true;
				BackColor = Theme.Bg;
				DoubleBuffered = true;
			}

			protected override void OnHandleCreated(EventArgs e)
			{
				base.OnHandleCreated(e);
				Theme.DarkScrollbars(Handle);
			}
		}

		private sealed class DarkColors : ProfessionalColorTable
		{
			public override Color MenuItemSelected => Theme.RowHot;
			public override Color MenuItemSelectedGradientBegin => Theme.RowHot;
			public override Color MenuItemSelectedGradientEnd => Theme.RowHot;
			public override Color MenuItemBorder => Theme.Line;
			public override Color MenuBorder => Theme.Line;
			public override Color ToolStripDropDownBackground => Theme.Panel;
			public override Color ImageMarginGradientBegin => Theme.Panel;
			public override Color ImageMarginGradientMiddle => Theme.Panel;
			public override Color ImageMarginGradientEnd => Theme.Panel;
			public override Color SeparatorDark => Theme.Line;
			public override Color SeparatorLight => Theme.Line;
		}

		private const int MinWidth = 640;
		private const int MinHeight = 360;

		private readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(14, 4, 14, 0) };
		private readonly TopBar top = new TopBar();
		private readonly ScrollHost host = new ScrollHost();
		private readonly HubCanvas hub = new HubCanvas();
		private readonly DetailsPanel details = new DetailsPanel();
		private readonly StatusBar bar = new StatusBar();
		private readonly ContextMenuStrip menu = new ContextMenuStrip();
		private readonly List<Source> extra = new List<Source>();

		private string gameDir;
		private CheckResult last;
		private string work;
		private bool busy;
		private string problemText = "";
		private readonly Timer clock = new Timer { Interval = 60000 };
		private DateTime lastAutoCheck = DateTime.UtcNow;
		private bool recheck;
		private string newerInstaller;

		internal MainForm(string gameDirArg, IEnumerable<string> links)
		{
			Text = "Half Grip mods installer";
			Font = Theme.Body(9f);
			BackColor = Theme.Bg;
			ForeColor = Theme.Ink;
			AutoScaleMode = AutoScaleMode.Dpi;
			StartPosition = FormStartPosition.CenterScreen;
			try
			{
				Icon = new Icon(typeof(MainForm).Assembly.GetManifestResourceStream("installer.ico"));
			}
			catch (Exception)
			{
				// The window works without its icon.
			}
			Rectangle area = Screen.PrimaryScreen.WorkingArea;
			float dpi = DeviceDpi / 96f;
			ClientSize = new Size(Math.Min((int)(1100 * dpi), area.Width * 9 / 10), Math.Min((int)(720 * dpi), area.Height * 9 / 10));
			Size chrome = Size - ClientSize;
			MinimumSize = new Size((int)(MinWidth * dpi) + chrome.Width, (int)(MinHeight * dpi) + chrome.Height);

			top.Dock = DockStyle.Top;
			host.Dock = DockStyle.Fill;
			host.Controls.Add(hub);
			details.Dock = DockStyle.Fill;
			details.Visible = false;
			// Fill controls go in first: the one added first is drawn on top, and docking leaves them what the edge controls do not use.
			content.Controls.Add(details);
			content.Controls.Add(host);
			content.Controls.Add(top);
			bar.Dock = DockStyle.Bottom;
			Controls.Add(content);
			Controls.Add(bar);

			menu.Renderer = new ToolStripProfessionalRenderer(new DarkColors()) { RoundedEdges = false };
			menu.BackColor = Theme.Panel;
			menu.ForeColor = Theme.Ink;
			menu.ShowImageMargin = false;

			host.Resize += (s, e) => FitCanvas();
			top.FilterChanged += key => ShowCards(true);
			top.SearchChanged += () => ShowCards(true);
			top.LinkSubmitted += AddLink;
			top.LinkRemoved += source => RemoveLink(source.Text);
			top.MenuClicked += point => ShowMenu(point);
			top.StarClicked += () => OpenUrl(Links.Repo);
			top.UpdateClicked += () => OpenUrl(Links.Releases);
			bar.PrimaryClicked += OnPrimary;
			bar.CheckNowClicked += CheckNow;
			clock.Tick += (s, e) => OnClock();
			clock.Start();
			hub.InstallRequested += mod => _ = RunInstall(new List<ModCandidate> { mod });
			hub.BaseRequested += which => _ = RunInstallCore(new List<ModCandidate>(), which == "bepinex", which == "tools");
			hub.RemoveRequested += source => RemoveLink(source.Source.Text);
			hub.Opened += face => OpenDetails(face);
			details.Closed += CloseDetails;
			details.ButtonClicked += face =>
			{
				CloseDetails();
				face.OnButton?.Invoke();
			};
			details.OpenUrlRequested += OpenUrl;

			gameDir = gameDirArg ?? GameFolder.Find();
			foreach (string link in links)
			{
				AddQuietly(link);
			}
			Load += (s, e) => Application.AddMessageFilter(this);
			Shown += (s, e) => SetGame(GameFolder.IsGame(gameDir) ? gameDir : null);
			Resize += (s, e) => Rescale();
			FormClosing += (s, e) =>
			{
				if (busy)
				{
					e.Cancel = true;
					MessageBox.Show(this, "Please wait until it has finished.", Text);
					return;
				}
				Application.RemoveMessageFilter(this);
				clock.Stop();
				CleanWork();
			};
			Rescale();
		}

		/// <summary>The window got another size: work out the scale (see Theme.ScaleFor) and lay every view out again.</summary>
		private void Rescale()
		{
			float dpi = DeviceDpi / 96f;
			float scale = Theme.ScaleFor((int)(ClientSize.Width / dpi), (int)(ClientSize.Height / dpi));
			if (Math.Abs(scale - Theme.Scale) < 0.001f)
			{
				return;
			}
			Theme.Scale = scale;
			menu.Font = Theme.Body(10f);
			top.Refit();
			bar.Refit();
			FitCanvas();
			hub.Relayout();
			details.Refit();
		}

		private void ShowMenu(Point at)
		{
			menu.Items.Clear();
			AddMenuItem("How to use", ShowHowTo);
			AddMenuItem("Check now", CheckNow);
			string path = gameDir ?? "not set";
			if (path.Length > 34)
			{
				path = "..." + path.Substring(path.Length - 31);
			}
			AddMenuItem("Game folder: " + path, PickFolder);
			menu.Items.Add(new ToolStripSeparator());
			AddMenuItem("Star HalfGripTools on GitHub", () => OpenUrl(Links.ToolsRepo));
			AddMenuItem("Remove the mods from the game...", () => _ = RunRemove());
			AddMenuItem("Open the log file", OpenLog);
			menu.Items.Add(new ToolStripSeparator());
			AddMenuItem("About", ShowAbout);
			menu.Show(top, at);
		}

		private void AddMenuItem(string text, Action action)
		{
			ToolStripMenuItem item = new ToolStripMenuItem(text) { ForeColor = Theme.Ink, BackColor = Theme.Panel, Padding = new Padding(4) };
			item.Click += (s, e) => action();
			menu.Items.Add(item);
		}

		private void FitCanvas()
		{
			// Room for the scroll bar is always kept, so showing it never changes how the cards flow.
			hub.Width = Math.Max(300, host.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
		}

		/// <summary>The mouse wheel goes to the cards whenever the pointer is over them, even while the search box has the focus.</summary>
		public bool PreFilterMessage(ref Message m)
		{
			if (m.Msg != 0x020A || !Visible || !host.Visible || ActiveForm != this || !host.RectangleToScreen(host.ClientRectangle).Contains(MousePosition))
			{
				return false;
			}
			int delta = (short)((long)m.WParam >> 16);
			host.AutoScrollPosition = new Point(0, Math.Max(0, -host.AutoScrollPosition.Y - delta / 120 * (int)(70 * DeviceDpi / 96f)));
			return true;
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (keyData == Keys.Escape && details.Visible)
			{
				CloseDetails();
				return true;
			}
			return base.ProcessCmdKey(ref msg, keyData);
		}

		// ---- the details page

		private void SetPage(bool detailsOpen)
		{
			details.Visible = detailsOpen;
			host.Visible = !detailsOpen;
			top.Visible = !detailsOpen;
		}

		private void OpenDetails(Face face)
		{
			details.ShowFace(face, busy);
			SetPage(true);
		}

		private void OpenInfo(string title, string text)
		{
			details.ShowInfo(title, text);
			SetPage(true);
		}

		private void CloseDetails()
		{
			SetPage(false);
		}

		// ---- the game and the links

		private void PickFolder()
		{
			if (busy)
			{
				return;
			}
			using (FolderBrowserDialog dialog = new FolderBrowserDialog())
			{
				dialog.Description = "Pick the Half Grip Demo folder (the one with HalfGrip.exe). In Steam: right click the game, Manage, Browse local files.";
				if (dialog.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}
				if (!GameFolder.IsGame(dialog.SelectedPath))
				{
					MessageBox.Show(this, "HalfGrip.exe is not in that folder. Pick the folder that has HalfGrip.exe in it.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
					return;
				}
				SetGame(dialog.SelectedPath);
			}
		}

		private void SetGame(string folder)
		{
			gameDir = folder;
			last = null;
			if (folder == null)
			{
				bar.Phase = Phase.NoGame;
				UpdateUi();
				return;
			}
			foreach (string link in Memory.Load(folder).Links)
			{
				AddQuietly(link);
			}
			_ = RunCheck(false, false, false);
		}

		/// <summary>For links from the command line and from last time: one that is wrong or already there is simply skipped.</summary>
		private void AddQuietly(string text)
		{
			try
			{
				TryAdd(text);
			}
			catch (PlainException e)
			{
				Log.Write("Link not used: " + text + " - " + e.Message);
			}
		}

		private void TryAdd(string text)
		{
			Source source = Source.Parse(text);
			if (source.Key == Source.Official.Key && source.SubPath == "" && source.Ref == "")
			{
				throw new PlainException("The official HalfGripMods mods are always included.");
			}
			if (extra.Any(e => e.Identity == source.Identity))
			{
				throw new PlainException("That link is already in the list.");
			}
			extra.Add(source);
		}

		private void AddLink(string text)
		{
			try
			{
				TryAdd(text);
			}
			catch (PlainException e)
			{
				top.LinkMessage = e.Message;
				top.Apply();
				return;
			}
			top.LinkMessage = "";
			top.ClearLink();
			if (gameDir == null)
			{
				UpdateUi();
				return;
			}
			_ = RunCheck(false, false, false);
		}

		private void RemoveLink(string text)
		{
			extra.RemoveAll(s => s.Text == text);
			if (gameDir != null)
			{
				_ = RunCheck(false, false, false);
			}
		}

		// ---- check, install, remove

		private string EnsureWork()
		{
			if (work == null || !Directory.Exists(work))
			{
				work = Path.Combine(Path.GetTempPath(), "HalfGripInstaller-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(work);
			}
			return work;
		}

		private void CleanWork()
		{
			try
			{
				if (work != null && Directory.Exists(work))
				{
					Directory.Delete(work, true);
				}
			}
			catch (IOException)
			{
				// Temp files; Windows clears them later.
			}
			work = null;
		}

		private void Status(string text)
		{
			Log.Write(text);
			BeginInvoke(new Action(() =>
			{
				// After an install the bar keeps its "installed" message while the list quietly refreshes.
				if (bar.Phase != Phase.Done)
				{
					bar.Detail = text;
					bar.Invalidate();
				}
			}));
		}

		/// <summary>"Check now": ask GitHub again whatever the age of what is saved, and unpack everything anew.</summary>
		private void CheckNow()
		{
			if (!busy && gameDir != null)
			{
				_ = RunCheck(true, true, false);
			}
		}

		/// <param name="clean">start from an empty work folder</param>
		/// <param name="force">ask GitHub again even if what is saved is under an hour old</param>
		private async Task RunCheck(bool clean, bool force, bool keepDone)
		{
			if (gameDir == null)
			{
				return;
			}
			if (busy)
			{
				// A link was added (or removed) while something was running: look again as soon as it is done.
				recheck = true;
				return;
			}
			if (clean)
			{
				CleanWork();
			}
			Cache.StartCheck();
			Cache.Force = force;
			lastAutoCheck = DateTime.UtcNow;
			string folder = EnsureWork();
			string dir = gameDir;
			busy = true;
			problemText = "";
			if (!keepDone)
			{
				bar.Phase = Phase.Checking;
				bar.Detail = "";
			}
			UpdateUi();
			try
			{
				// The config (BepInEx, the repositories, the suggestions) is looked at first: it is a small file, asked for at most once an hour.
				await Task.Run(() => Config.Refresh());
				List<Source> sources = new List<Source> { Source.Official };
				sources.AddRange(extra);
				List<Source> own = sources.ToList();
				// The author's suggestions are links in that config. A link the player has added is never shown twice.
				sources.AddRange(Recommendations.Load(own));
				newerInstaller = await Task.Run(() => SelfUpdate.NewerVersion());
				last = await Task.Run(() => Checker.Run(dir, sources, folder, Status));
			}
			catch (Exception e)
			{
				Log.Write(e.ToString());
				last = null;
				problemText = Setup.Friendly(e);
			}
			Cache.Force = false;
			busy = false;
			if (bar.Phase != Phase.Done || !keepDone)
			{
				bar.Phase = Phase.Ready;
			}
			UpdateUi();
			if (recheck)
			{
				recheck = false;
				await RunCheck(false, false, false);
			}
		}

		/// <summary>Installs mods. Whatever is required and missing or old (BepInEx, HalfGripTools) goes in with them.</summary>
		private Task RunInstall(List<ModCandidate> mods)
		{
			if (last == null || last.Tools == null)
			{
				return Task.CompletedTask;
			}
			return RunInstallCore(mods, last.BepInExNeeded, last.ToolsNeeded);
		}

		private async Task RunInstallCore(List<ModCandidate> mods, bool bepinex, bool tools)
		{
			if (busy || last == null || (tools && last.Tools == null))
			{
				return;
			}
			if (GameFolder.IsRunning())
			{
				MessageBox.Show(this, "Half Grip is running. Close the game and try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			string dir = gameDir;
			string folder = EnsureWork();
			CheckResult result = last;
			// Installing a suggested mod is the player's choice, so from now on its link is one of their own.
			foreach (ModCandidate suggested in mods.Where(m => m.Recommended))
			{
				AddQuietly(suggested.Source.Text);
			}
			List<Source> links = extra.ToList();
			busy = true;
			bar.Phase = Phase.Installing;
			bar.Detail = "";
			UpdateUi();
			bool ok = false;
			try
			{
				await Task.Run(() => Setup.Install(dir, result.BepInExTarget, bepinex, result.Tools, tools, mods, links, folder, Status));
				ok = true;
			}
			catch (Exception e)
			{
				Log.Write(e.ToString());
				MessageBox.Show(this, Setup.Friendly(e) + "\n\nYour saves were not touched. Fix this and try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			busy = false;
			bar.Phase = ok ? Phase.Done : Phase.Ready;
			await RunCheck(false, false, ok);
		}

		private async Task RunRemove()
		{
			if (busy || gameDir == null)
			{
				return;
			}
			if (GameFolder.IsRunning())
			{
				MessageBox.Show(this, "Half Grip is running. Close the game and try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			string question = Setup.Describe(gameDir);
			if (question == null)
			{
				MessageBox.Show(this, "No HalfGripTools and no mods found, nothing to remove.", Text);
				return;
			}
			if (MessageBox.Show(this, question + "\n\nRemove them?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
			{
				return;
			}
			string[] others = Setup.OtherPlugins(gameDir);
			bool bepinex = Directory.Exists(Path.Combine(gameDir, "BepInEx")) && MessageBox.Show(this,
				"BepInEx is the base that mod loaders run on. Remove it only if you want the game fully vanilla." + (others.Length > 0 ? "\n\nStill in BepInEx\\plugins: " + string.Join(", ", others) + ". Those would stop working." : "") + "\n\nRemove BepInEx too?",
				Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
			string dir = gameDir;
			busy = true;
			bar.Phase = Phase.Installing;
			bar.Detail = "Removing ...";
			UpdateUi();
			try
			{
				await Task.Run(() => Setup.Remove(dir, bepinex));
			}
			catch (Exception e)
			{
				Log.Write(e.ToString());
				MessageBox.Show(this, Setup.Friendly(e), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			busy = false;
			await RunCheck(false, false, false);
		}

		private void OpenLog()
		{
			try
			{
				if (File.Exists(Log.LogFile))
				{
					Process.Start(new ProcessStartInfo(Log.LogFile) { UseShellExecute = true });
				}
				else
				{
					MessageBox.Show(this, "There is no log yet.", Text);
				}
			}
			catch (Exception)
			{
				MessageBox.Show(this, "The log is here:\n" + Log.LogFile, Text);
			}
		}

		// ---- once an hour, the how-to and about pages

		/// <summary>"Updated 12 min ago": how old the information on screen is.</summary>
		private string Age()
		{
			TimeSpan age = DateTime.UtcNow - Cache.FetchedUtc;
			if (age < TimeSpan.FromMinutes(1))
			{
				return "Updated just now";
			}
			return age < TimeSpan.FromHours(1) ? "Updated " + (int)age.TotalMinutes + " min ago" : "Updated " + (int)age.TotalHours + " h ago";
		}

		/// <summary>
		/// Once a minute: keep "updated N min ago" honest, and when the window has been open for an hour, look again by itself.
		/// An hour after the last try, not after the last success, so a missing connection is not retried every minute.
		/// </summary>
		private void OnClock()
		{
			if (busy || gameDir == null || bar.Phase != Phase.Ready)
			{
				return;
			}
			if (DateTime.UtcNow - lastAutoCheck >= Cache.MaxAge)
			{
				_ = RunCheck(false, false, false);
				return;
			}
			if (last != null)
			{
				bar.CheckedText = Age();
				bar.Invalidate();
			}
		}

		private void OpenUrl(string address)
		{
			// Only GitHub pages are ever opened, and only ones this program built from a link it had checked.
			if (!address.StartsWith("https://github.com/", StringComparison.Ordinal))
			{
				return;
			}
			try
			{
				Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
			}
			catch (Exception)
			{
				MessageBox.Show(this, "Open this page in your browser:\n" + address, Text);
			}
		}

		private const string HowTo =
			"1.  Close the game, then press INSTALL ALL. It puts in BepInEx, HalfGripTools and every official mod.\n" +
			"2.  Click a card to read about a mod. Its own button installs just that one.\n" +
			"3.  + ADD takes the GitHub link of someone else's mods.\n" +
			"4.  SUGGESTED lists mods the author recommends. Nothing is installed until you press its button.\n" +
			"5.  Start the game from Steam. The first start is a little slower while the mods compile.\n" +
			"6.  Open the installer again any time to update. It asks GitHub once an hour; CHECK NOW asks straight away.\n\n" +
			"Your saves are never touched. MENU, Remove the mods... takes everything out.";

		private void ShowHowTo()
		{
			OpenInfo("HOW TO USE", HowTo);
		}

		private void ShowAbout()
		{
			string version = typeof(MainForm).Assembly.GetName().Version.ToString(3);
			OpenInfo("ABOUT",
				"Half Grip mods installer " + version + ", by 4l3x4nd3r. It puts BepInEx, HalfGripTools and the mods into Half Grip Demo.\n\n" +
				"Mods from other people run on your PC like any program, so add links only from people you trust. Every mod is plain text you can read on its GitHub page, and a mod that carries a program file is refused. Your saves are never touched.\n\n" +
				"It keeps its settings and a copy of what GitHub said in:\n" + Cache.Root + "\n\n" +
				"Font: VT323, by the VT323 Project Authors (SIL Open Font License).");
		}

		// ---- what is shown

		private void OnPrimary()
		{
			switch (bar.Phase)
			{
				case Phase.NoGame:
					PickFolder();
					break;
				case Phase.Done:
					bar.Phase = Phase.Ready;
					UpdateUi();
					break;
				case Phase.Ready:
					if (last == null || last.Tools == null)
					{
						CheckNow();
						return;
					}
					_ = RunInstall(Pending(last));
					break;
			}
		}

		/// <summary>The mods "Install all" takes: the ones that are new or have a newer version.</summary>
		private static List<ModCandidate> Pending(CheckResult result)
		{
			return result.OwnMods.Where(m => m.Problem == null && (m.Installed == null || m.Installed != m.Version)).ToList();
		}

		private void ShowCards(bool backToTop)
		{
			hub.SetData(last, top.Filter, top.SearchText, busy, EmptyNote());
			if (backToTop)
			{
				host.AutoScrollPosition = new Point(0, 0);
			}
		}

		private string EmptyNote()
		{
			if (bar.Phase == Phase.NoGame)
			{
				return "Pick your Half Grip folder to see the mods.";
			}
			if (last == null)
			{
				return busy ? "Looking for mods ..." : (problemText.Length > 0 ? problemText : "Nothing to show.");
			}
			return "No mods match.";
		}

		private void UpdateUi()
		{
			top.Sources = extra.ToList();
			top.UpdateNote = newerInstaller ?? "";
			bar.CheckedText = last == null ? "" : Age();
			bar.CanCheck = gameDir != null;
			if (last != null)
			{
				top.Counts[0] = last.OwnMods.Count();
				string[] keys = { "all", "new", "updates", "installed", "problems", "recommended" };
				for (int i = 1; i < keys.Length; i++)
				{
					top.Counts[i] = last.AllMods.Count(m => HubCanvas.Matches(m, keys[i]));
				}
				top.Counts[4] += last.FailedLinks.Count();
				if (top.Filter != "all" && top.Counts[Array.IndexOf(keys, top.Filter)] == 0)
				{
					top.Filter = "all";
				}
			}
			else
			{
				Array.Clear(top.Counts, 0, top.Counts.Length);
			}

			switch (bar.Phase)
			{
				case Phase.NoGame:
					bar.Headline = "Half Grip was not found";
					bar.Detail = "Choose the folder that has HalfGrip.exe in it.";
					bar.Primary = "CHOOSE FOLDER";
					bar.PrimaryEnabled = true;
					break;
				case Phase.Checking:
					bar.Headline = "Checking ...";
					bar.Primary = "PLEASE WAIT";
					bar.PrimaryEnabled = false;
					break;
				case Phase.Installing:
					bar.Headline = "Installing ...";
					bar.Primary = "PLEASE WAIT";
					bar.PrimaryEnabled = false;
					break;
				case Phase.Done:
					bar.Headline = "Installed";
					bar.Detail = "Start the game from Steam.";
					bar.Primary = "OK";
					bar.PrimaryEnabled = true;
					break;
				default:
					ShowReady();
					break;
			}
			top.Apply();
			bar.Apply();
			ShowCards(false);
			if (details.Visible && !details.IsInfo)
			{
				Face fresh = hub.FindFace(details.ShownKey);
				if (fresh == null)
				{
					CloseDetails();
				}
				else
				{
					details.ShowFace(fresh, busy);
				}
			}
		}

		private void ShowReady()
		{
			if (last == null)
			{
				bar.Headline = "Could not check";
				bar.Detail = problemText;
				bar.Primary = "TRY AGAIN";
				bar.PrimaryEnabled = true;
				return;
			}
			int mods = Pending(last).Count;
			int required = (last.BepInExNeeded ? 1 : 0) + (last.ToolsNeeded ? 1 : 0);
			int total = mods + required;
			int failed = last.FailedLinks.Count();
			bar.Headline = total == 0 ? "Everything is up to date" : total + " to install";
			string note = last.Tools == null ? "HalfGripTools could not be looked up." : "";
			if (failed > 0)
			{
				note = (note.Length > 0 ? note + " " : "") + failed + (failed == 1 ? " link failed." : " links failed.");
			}
			bar.Detail = note;
			if (last.Tools == null)
			{
				bar.Primary = "TRY AGAIN";
				bar.PrimaryEnabled = true;
			}
			else if (mods > 0)
			{
				bar.Primary = "INSTALL ALL (" + total + ")";
				bar.PrimaryEnabled = true;
			}
			else if (required > 0)
			{
				bar.Primary = "INSTALL REQUIRED";
				bar.PrimaryEnabled = true;
			}
			else
			{
				bar.Primary = "ALL UP TO DATE";
				bar.PrimaryEnabled = false;
			}
		}

		// ---- for the picture tests (--render): the window drawn into a file, never a screenshot of the desktop

		/// <summary>True once the first check is over, so a picture of the window shows the real cards.</summary>
		internal bool Settled => !busy && bar.Phase == Phase.Ready && (last != null || problemText.Length > 0);

		/// <summary>Sizes the window like a player would and, if a card key is given, opens its page.</summary>
		internal void PrepareRender(Size client, string openKey)
		{
			ClientSize = client;
			Rescale();
			CloseDetails();
			if (openKey == "how")
			{
				ShowHowTo();
			}
			else if (openKey == "about")
			{
				ShowAbout();
			}
			else if (openKey != "")
			{
				Face face = hub.FindFace(openKey);
				if (face != null)
				{
					OpenDetails(face);
				}
			}
		}

		internal IEnumerable<string> CardKeys()
		{
			return last == null ? new string[0] : new[] { "bepinex", "tools" }.Concat(last.AllMods.Select(m => m.SourceKey + "|" + m.Id));
		}

		internal Bitmap Render()
		{
			Bitmap picture = new Bitmap(ClientSize.Width, ClientSize.Height);
			Point origin = PointToScreen(Point.Empty);
			using (Bitmap whole = new Bitmap(Width, Height))
			{
				DrawToBitmap(whole, new Rectangle(0, 0, Width, Height));
				using (Graphics g = Graphics.FromImage(picture))
				{
					g.DrawImage(whole, new Rectangle(0, 0, picture.Width, picture.Height), new Rectangle(origin.X - Left, origin.Y - Top, picture.Width, picture.Height), GraphicsUnit.Pixel);
				}
			}
			return picture;
		}
	}
}
