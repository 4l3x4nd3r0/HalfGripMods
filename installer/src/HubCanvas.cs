using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace HalfGripModsInstaller
{
	/// <summary>
	/// Everything a card shows and does. The grid draws its cards from these, and the details page draws the same one bigger, so a card
	/// and its details page can never disagree.
	/// </summary>
	internal sealed class Face
	{
		/// <summary>Identifies the card across refreshes ("bepinex", "tools", or the mod's source key and id).</summary>
		internal string Key = "";
		internal string State = "";
		internal string Name = "";
		internal string Version = "";
		internal string Author = "";

		/// <summary>The full text. A card shows its first line, the details page all of it.</summary>
		internal string Text = "";

		internal string Warning = "";
		internal string IconPath;

		/// <summary>The picture built into the installer, used when there is no file ("bepinex", "tools").</summary>
		internal string BuiltIn;

		/// <summary>Where it comes from, for the details page ("owner/repo, release v1.0").</summary>
		internal string From = "";

		/// <summary>The GitHub page of the source, opened by "Open on GitHub". Empty when there is none.</summary>
		internal string Url = "";

		internal string Button = "";
		internal string ButtonTip;
		internal Color Accent;
		internal Color StateColor;
		internal Color TextColor = Theme.Soft;
		internal bool ButtonEnabled;
		internal bool Grey;
		internal Action OnButton;
	}

	/// <summary>
	/// The grid of mod cards, one section per source. It is as tall as its content and sits in a scrolling panel. A card is mostly a
	/// picture, then the name, version and one line about the mod, and its own button; a click on the card opens its details page.
	/// The number of columns follows the width of the window.
	/// </summary>
	internal sealed class HubCanvas : Surface
	{
		private enum Kind
		{
			Title,
			Base,
			Header,
			Card,
			Error,
			Note
		}

		private sealed class Placed
		{
			internal Kind Kind;
			internal Rectangle Bounds;
			internal ModCandidate Mod;
			internal SourceResult Source;
			internal string Text = "";
			internal string Extra = "";
		}

		internal event Action<ModCandidate> InstallRequested;

		internal event Action<SourceResult> RemoveRequested;

		/// <summary>The button of a required card ("bepinex" or "tools").</summary>
		internal event Action<string> BaseRequested;

		/// <summary>A card was clicked: show its details.</summary>
		internal event Action<Face> Opened;

		private readonly List<Placed> placed = new List<Placed>();
		private CheckResult result;
		private string filter = "all";
		private string search = "";
		private bool busy;
		private string note = "";

		/// <summary>The mods a filter lets through. Used for the counts above the grid too, so both always agree.</summary>
		internal static bool Matches(ModCandidate mod, string filter)
		{
			// A suggestion only counts as one while it is installable and not installed; otherwise it is not shown at all.
			if (mod.Recommended)
			{
				return mod.Problem == null && mod.Installed == null && (filter == "all" || filter == "recommended");
			}
			switch (filter)
			{
				case "new": return mod.Problem == null && mod.Installed == null;
				case "updates": return mod.Problem == null && mod.Installed != null && mod.Installed != mod.Version;
				case "installed": return mod.Installed != null;
				case "problems": return mod.Problem != null;
				case "recommended": return false;
				default: return true;
			}
		}

		internal void SetData(CheckResult data, string filterName, string searchText, bool isBusy, string emptyNote)
		{
			result = data;
			filter = filterName;
			search = (searchText ?? "").Trim();
			busy = isBusy;
			note = emptyNote ?? "";
			Relayout();
		}

		/// <summary>The face of the card with this key, as it is now. Null if there is no such card any more.</summary>
		internal Face FindFace(string key)
		{
			if (result == null)
			{
				return null;
			}
			if (key == "bepinex")
			{
				return BepInExFace();
			}
			if (key == "tools")
			{
				return ToolsFace();
			}
			ModCandidate mod = result.AllMods.FirstOrDefault(m => KeyOf(m) == key);
			return mod == null ? null : FaceOf(mod, result.Sources.FirstOrDefault(s => s.Mods.Contains(mod)));
		}

		private static string KeyOf(ModCandidate m)
		{
			return m.SourceKey + "|" + m.Id;
		}

		private bool Shown(ModCandidate mod)
		{
			if (!Matches(mod, filter))
			{
				return false;
			}
			if (search.Length == 0)
			{
				return true;
			}
			return new[] { mod.Name, mod.Id, mod.Author, mod.Description }.Any(t => (t ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
		}

		internal override void Refit()
		{
			Relayout();
		}

		/// <summary>Works out where everything goes for the current width, and sets the height to fit.</summary>
		internal void Relayout()
		{
			placed.Clear();
			int pad = S(6);
			int gap = S(12);
			int minCard = S(200);
			int cardH = S(248);
			int cols = Math.Max(1, Math.Min(6, (Width - 2 * pad + gap) / (minCard + gap)));
			int cardW = (Width - 2 * pad - gap * (cols - 1)) / cols;
			int y = pad;
			if (result != null && filter == "all" && search.Length == 0)
			{
				placed.Add(new Placed { Kind = Kind.Title, Bounds = new Rectangle(pad, y, Width - 2 * pad, S(28)), Text = "REQUIRED", Extra = "Needed by every mod" });
				y += S(36);
				for (int i = 0; i < 2; i++)
				{
					int col = i % cols;
					if (col == 0 && i > 0)
					{
						y += cardH + gap;
					}
					placed.Add(new Placed { Kind = Kind.Base, Bounds = new Rectangle(pad + col * (cardW + gap), y, cardW, cardH), Text = i == 0 ? "bepinex" : "tools" });
				}
				y += cardH + gap + S(8);
			}
			if (result != null)
			{
				// Links that failed come first, so nobody has to scroll to find out why theirs is missing.
				// Suggestions go last, under the player's own mods.
				foreach (SourceResult source in result.Sources.OrderBy(s => s.Error == null).ThenBy(s => s.Source.Recommended))
				{
					List<ModCandidate> mods = source.Mods.Where(Shown).ToList();
					bool showError = source.Error != null && !source.Source.Recommended && (filter == "all" || filter == "problems") && search.Length == 0;
					if (mods.Count == 0 && !showError)
					{
						continue;
					}
					placed.Add(new Placed { Kind = Kind.Header, Bounds = new Rectangle(pad, y, Width - 2 * pad, S(28)), Source = source, Text = SectionTitle(source) });
					y += S(36);
					if (showError)
					{
						placed.Add(new Placed { Kind = Kind.Error, Bounds = new Rectangle(pad, y, Width - 2 * pad, S(84)), Source = source });
						y += S(84) + gap;
					}
					for (int i = 0; i < mods.Count; i++)
					{
						int col = i % cols;
						if (col == 0 && i > 0)
						{
							y += cardH + gap;
						}
						placed.Add(new Placed { Kind = Kind.Card, Bounds = new Rectangle(pad + col * (cardW + gap), y, cardW, cardH), Mod = mods[i], Source = source });
					}
					if (mods.Count > 0)
					{
						y += cardH + gap;
					}
					y += S(8);
				}
			}
			if (placed.Count == 0)
			{
				placed.Add(new Placed { Kind = Kind.Note, Bounds = new Rectangle(pad, S(40), Width - 2 * pad, S(60)), Text = note.Length > 0 ? note : "No mods match." });
				y = S(120);
			}
			Height = Math.Max(y + pad, 10);
			Invalidate();
		}

		private static string SectionTitle(SourceResult source)
		{
			if (source.Source.Recommended)
			{
				return "SUGGESTED  " + source.Source.Name.ToUpperInvariant();
			}
			return source.Source.Key == Source.Official.Key ? "HALF GRIP MODS" : source.Source.Name.ToUpperInvariant();
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			// Setting the height calls this again; only a new width changes how the cards flow.
			if (Width != laidOutWidth)
			{
				laidOutWidth = Width;
				Relayout();
			}
		}

		private int laidOutWidth;

		protected override void Draw(Graphics g)
		{
			Rectangle clip = Rectangle.Ceiling(g.ClipBounds);
			foreach (Placed p in placed)
			{
				bool visible = p.Bounds.IntersectsWith(clip);
				switch (p.Kind)
				{
					case Kind.Title:
						if (visible)
						{
							DrawHeading(g, p.Bounds, p.Text, p.Extra, Theme.Red);
						}
						break;
					case Kind.Base:
						DrawFace(g, p.Bounds, visible, p.Text == "bepinex" ? BepInExFace() : ToolsFace());
						break;
					case Kind.Header:
						if (visible)
						{
							DrawHeading(g, p.Bounds, p.Text, p.Source.Source.Recommended ? p.Source.Source.Note : "", p.Source.Source.Recommended ? Theme.Amber : Theme.Red);
						}
						break;
					case Kind.Card:
						DrawFace(g, p.Bounds, visible, FaceOf(p.Mod, p.Source));
						break;
					case Kind.Error:
						DrawError(g, p, visible);
						break;
					case Kind.Note:
						Theme.Write(g, p.Text, Theme.Pixel(18), Theme.Soft, p.Bounds, StringAlignment.Center, StringAlignment.Near);
						break;
				}
			}
		}

		/// <summary>A section title with a thin line under it, the first part of the line in colour. Small grey text sits after the title.</summary>
		private void DrawHeading(Graphics g, Rectangle r, string title, string small, Color accent)
		{
			int w = Theme.Measure(g, title, Theme.Pixel(16)) + S(6);
			Theme.Write(g, title, Theme.Pixel(16), Theme.Ink, new Rectangle(r.X + S(2), r.Y, w, r.Height), StringAlignment.Near, StringAlignment.Center);
			Theme.Write(g, small, Theme.Body(8.5f), Theme.Dim, new Rectangle(r.X + S(2) + w + S(8), r.Y + S(2), Math.Max(0, r.Width - w - S(16)), r.Height), StringAlignment.Near, StringAlignment.Center);
			using (Pen pen = new Pen(Theme.Line))
			{
				g.DrawLine(pen, r.X, r.Bottom, r.Right, r.Bottom);
			}
			using (Pen pen = new Pen(accent, 2))
			{
				g.DrawLine(pen, r.X, r.Bottom, r.X + w + S(8), r.Bottom);
			}
		}

		private void DrawError(Graphics g, Placed p, bool visible)
		{
			Rectangle r = p.Bounds;
			SourceResult source = p.Source;
			bool official = source.Source.Key == Source.Official.Key;
			Rectangle button = new Rectangle(r.Right - S(112), r.Y + (r.Height - S(30)) / 2, S(98), S(30));
			if (!official)
			{
				Add(button, () => RemoveRequested?.Invoke(source), "Take this link out of the list");
			}
			if (!visible)
			{
				return;
			}
			Theme.FillRound(g, r, S(8), Color.FromArgb(30, 16, 16), Color.FromArgb(120, 38, 38), 1.5f);
			Rectangle sign = new Rectangle(r.X + S(14), r.Y + (r.Height - S(36)) / 2, S(36), S(36));
			Theme.FillRound(g, sign, S(5), Theme.Red);
			Theme.Write(g, "!", Theme.Pixel(24), Theme.ButtonText, sign, StringAlignment.Center, StringAlignment.Center);
			int x = sign.Right + S(14);
			int w = (official ? r.Right - S(16) : button.X - S(12)) - x;
			Theme.Write(g, source.Source.Text, Theme.Pixel(15), Theme.White, new Rectangle(x, r.Y + S(8), w, S(24)));
			Theme.Write(g, source.Error, Theme.Body(8.5f), Theme.Soft, new Rectangle(x, r.Y + S(33), w, r.Height - S(38)), StringAlignment.Near, StringAlignment.Near, true);
			if (!official)
			{
				Theme.Outline(g, button, "REMOVE", Theme.Red, false, Over(button), 14);
			}
		}

		private Face FaceOf(ModCandidate m, SourceResult from)
		{
			bool blocked = m.Problem != null;
			string where = from == null ? m.SourceName : m.SourceName + (from.From.Length > 0 ? ", " + from.From : "");
			return new Face
			{
				Key = KeyOf(m),
				Accent = Theme.AccentFor(m.Id),
				State = blocked ? "NO" : (m.Recommended ? "SUGGESTED" : (m.Installed == null ? "NEW" : (m.Installed != m.Version ? "UPDATE" : "INSTALLED"))),
				StateColor = blocked ? Theme.Red : (m.Recommended ? Theme.Amber : (m.Installed == null ? Theme.Green : (m.Installed != m.Version ? Theme.Amber : Theme.Dim))),
				Name = m.Name,
				Version = m.Installed == null || m.Installed == m.Version ? m.Version : m.Installed + " → " + m.Version,
				Author = m.Author.Length > 0 ? m.Author : "",
				Text = blocked ? "Cannot be installed: " + m.Problem : m.Description,
				TextColor = blocked ? Color.FromArgb(235, 110, 110) : Theme.Soft,
				Warning = !blocked && m.Warnings.Count > 0 ? string.Join(" ", m.Warnings) : "",
				IconPath = m.IconPath,
				From = where,
				Url = m.Source != null && m.Source.DirectZip == "" && m.Source.Owner != "" ? "https://github.com/" + m.Source.Owner + "/" + m.Source.Repo : "",
				Button = blocked ? "UNAVAILABLE" : (m.Installed == null ? "INSTALL" : (m.Installed != m.Version ? "UPDATE" : "REINSTALL")),
				ButtonEnabled = !blocked,
				Grey = !blocked && m.Installed != null && m.Installed == m.Version,
				OnButton = () => InstallRequested?.Invoke(m),
				ButtonTip = blocked ? m.Problem : null
			};
		}

		private Face BepInExFace()
		{
			string have = result.BepInExVersion;
			string want = result.BepInExTarget.Version;
			bool missing = have == null;
			bool needed = result.BepInExNeeded;
			return new Face
			{
				Key = "bepinex",
				Accent = Color.FromArgb(214, 130, 60),
				State = missing ? "MISSING" : (needed ? "UPDATE" : "INSTALLED"),
				StateColor = missing ? Theme.Red : (needed ? Theme.Amber : Theme.Dim),
				Name = "BepInEx",
				Version = missing ? want : (needed ? have + " → " + want : have),
				Author = "the BepInEx team",
				Text = "The base every Half Grip mod loader runs on. Taken from BepInEx's own GitHub release and checked against a fixed checksum.",
				IconPath = result.BepInExIcon,
				BuiltIn = "bepinex",
				From = "github.com/BepInEx/BepInEx",
				Url = "https://github.com/BepInEx/BepInEx",
				Button = missing ? "INSTALL" : (needed ? "UPDATE" : "REINSTALL"),
				ButtonEnabled = true,
				Grey = !missing && !needed,
				OnButton = () => BaseRequested?.Invoke("bepinex")
			};
		}

		private Face ToolsFace()
		{
			ToolsPlan plan = result.Tools;
			string have = result.ToolsInstalled;
			bool missing = have == null;
			bool needed = result.ToolsNeeded;
			Face face = new Face
			{
				Key = "tools",
				Accent = Color.FromArgb(255, 176, 40),
				Name = "HalfGripTools",
				Author = "4l3x4nd3r",
				IconPath = result.ToolsIcon,
				BuiltIn = "tools",
				From = Config.Current.ToolsRepo,
				Url = Links.ToolsRepo
			};
			if (plan == null)
			{
				face.State = "NO";
				face.StateColor = Theme.Red;
				face.Version = have ?? "";
				face.Text = "The newest release could not be looked up: " + result.ToolsError;
				face.TextColor = Color.FromArgb(235, 110, 110);
				face.Button = "UNAVAILABLE";
				return face;
			}
			face.State = missing ? "MISSING" : (needed ? "UPDATE" : "INSTALLED");
			face.StateColor = missing ? Theme.Red : (needed ? Theme.Amber : Theme.Dim);
			face.Version = missing ? plan.Tag : (needed ? have + " → " + plan.Tag : have);
			face.Text = "Loads the mods, compiles them when the game starts, and adds the mod manager to the main menu.";
			face.Button = missing ? "INSTALL" : (needed ? "UPDATE" : "REINSTALL");
			face.ButtonEnabled = true;
			face.Grey = !missing && !needed;
			face.OnButton = () => BaseRequested?.Invoke("tools");
			return face;
		}

		/// <summary>The picture of a face: its file, else the one built into the installer, else null (the caller draws a letter).</summary>
		internal static Image PictureOf(Face f)
		{
			return Theme.LoadImage(f.IconPath) ?? (f.BuiltIn != null ? Theme.BuiltIn(f.BuiltIn) : null);
		}

		private void DrawFace(Graphics g, Rectangle r, bool visible, Face f)
		{
			int pad = S(10);
			Rectangle button = new Rectangle(r.X + pad, r.Bottom - pad - S(32), r.Width - 2 * pad, S(32));
			bool canClick = f.ButtonEnabled && !busy;
			Add(r, () => Opened?.Invoke(f), f.Text.Length > 0 ? f.Text : null);
			Add(button, canClick ? f.OnButton : null, f.ButtonTip);
			if (!visible)
			{
				return;
			}

			bool hover = Over(r);
			Color face = hover ? Theme.RowHot : Theme.Row;
			Theme.FillRound(g, r, S(8), face, Theme.Mix(Theme.Line, f.Accent, hover ? 0.55f : 0.2f), 1.2f);

			// the picture, on a dark tile, with the state in its corner
			Rectangle tile = new Rectangle(r.X + pad, r.Y + pad, r.Width - 2 * pad, S(104));
			Theme.FillRound(g, tile, S(6), Color.FromArgb(14, 14, 18), Theme.Mix(Theme.Line, f.Accent, 0.25f));
			Image picture = PictureOf(f);
			Rectangle inner = Rectangle.Inflate(tile, -S(22), -S(16));
			if (picture != null)
			{
				Theme.DrawPixels(g, picture, inner);
			}
			else
			{
				Theme.Placeholder(g, new Rectangle(inner.X + (inner.Width - inner.Height) / 2, inner.Y, inner.Height, inner.Height), f.Name, f.Accent);
			}
			int stateW = Theme.Measure(g, f.State, Theme.Pixel(11)) + S(14);
			Theme.Chip(g, new Rectangle(tile.Right - S(6) - stateW, tile.Y + S(6), stateW, S(18)), f.State, f.StateColor);

			// name, version and who made it, one line about it
			int y = tile.Bottom + S(8);
			Theme.Write(g, f.Name, Theme.Pixel(17), Theme.White, new Rectangle(r.X + pad, y, r.Width - 2 * pad, S(24)));
			string sub = f.Version + (f.Author.Length > 0 ? "  ·  " + f.Author : "");
			Theme.Write(g, sub, Theme.Body(8f), Theme.Mix(Theme.Dim, f.Accent, 0.5f), new Rectangle(r.X + pad, y + S(25), r.Width - 2 * pad, S(16)));
			string line = f.Warning.Length > 0 ? f.Warning : f.Text;
			Theme.Write(g, line, Theme.Body(8.5f), f.Warning.Length > 0 ? Theme.Amber : f.TextColor, new Rectangle(r.X + pad, y + S(44), r.Width - 2 * pad, S(18)));

			Theme.GameButton(g, button, f.Button, f.Grey ? ButtonLook.Grey : ButtonLook.Red, canClick, canClick && Over(button), 15);
		}
	}
}
