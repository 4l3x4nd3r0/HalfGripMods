using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace HalfGripModsInstaller
{
	/// <summary>
	/// Everything above the cards: the title with its three small buttons (add, star, menu), the tabs, the search box, the "add mods"
	/// panel and the note about a newer installer. It lays itself out in rows that flow with the window: the search moves under the title
	/// when there is no room beside it. It only shows what MainForm sets and raises events; it decides nothing.
	/// </summary>
	internal sealed class TopBar : Surface
	{
		private static readonly string[] FilterKeys = { "all", "new", "updates", "installed", "problems", "recommended" };
		private static readonly string[] FilterLabels = { "ALL", "NEW", "UPDATES", "INSTALLED", "UNAVAILABLE", "SUGGESTED" };

		internal event Action<string> FilterChanged;

		internal event Action SearchChanged;

		internal event Action<string> LinkSubmitted;

		internal event Action<Source> LinkRemoved;

		internal event Action<Point> MenuClicked;

		internal event Action StarClicked;

		internal event Action UpdateClicked;

		/// <summary>Set when a newer installer exists, e.g. "1.0.1": a thin strip with a link appears.</summary>
		internal string UpdateNote = "";
		internal string Filter = "all";
		internal string LinkMessage = "";
		internal bool AddOpen;

		/// <summary>How many mods each tab holds. A tab with none is not shown (except ALL).</summary>
		internal readonly int[] Counts = new int[6];
		internal List<Source> Sources = new List<Source>();

		internal readonly TextBox SearchBox = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Row, ForeColor = Theme.White };
		internal readonly TextBox LinkBox = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Row, ForeColor = Theme.White, Visible = false };

		private Rectangle rLogo, rMenu, rSearch, rStar, rUpdate, rAdd, rAddPanel, rLinkField, rLinkButton, rHint, rLine;
		private readonly Rectangle[] rTabs = new Rectangle[6];
		private bool starLabel, addLabel;

		internal TopBar()
		{
			Controls.Add(SearchBox);
			Controls.Add(LinkBox);
			SearchBox.TextChanged += (s, e) => SearchChanged?.Invoke();
			LinkBox.KeyDown += (s, e) =>
			{
				if (e.KeyCode == Keys.Enter)
				{
					e.SuppressKeyPress = true;
					LinkSubmitted?.Invoke(LinkBox.Text);
				}
			};
			// The grey hint inside an empty box can only be set once the box has its window.
			SearchBox.HandleCreated += (s, e) => Theme.CueBanner(SearchBox.Handle, "Search");
			LinkBox.HandleCreated += (s, e) => Theme.CueBanner(LinkBox.Handle, "https://github.com/owner/repo");
		}

		internal string SearchText => SearchBox.Text;

		/// <summary>Call after changing any of the fields above.</summary>
		internal void Apply()
		{
			LinkBox.Visible = AddOpen;
			Refit();
		}

		internal void ClearLink()
		{
			LinkBox.Text = "";
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			if (Width != laidOutWidth)
			{
				laidOutWidth = Width;
				Refit();
			}
		}

		private int laidOutWidth;

		private bool Shows(int i)
		{
			return i == 0 || Counts[i] > 0;
		}

		internal override void Refit()
		{
			if (Width <= 0)
			{
				return;
			}
			SearchBox.Font = Theme.Body(10f);
			LinkBox.Font = Theme.Body(10f);
			int pad = S(4);
			int y = S(8);
			int rowH = S(40);
			rLogo = new Rectangle(pad, y + (rowH - S(34)) / 2, S(34), S(34));

			// right side of the first row: menu, star, add. The star and add buttons carry words only when there is plenty of room.
			bool plenty = Width >= S(900);
			starLabel = plenty;
			addLabel = Width >= S(780);
			int btn = S(34);
			rMenu = new Rectangle(Width - pad - btn, y + (rowH - btn) / 2, btn, btn);
			int starW = btn;
			if (starLabel)
			{
				using (Graphics g = CreateGraphics())
				{
					starW = Theme.Measure(g, "STAR", Theme.Pixel(15)) + S(46);
				}
			}
			rStar = new Rectangle(rMenu.X - S(8) - starW, rMenu.Y, starW, btn);
			int addW = addLabel ? S(78) : btn;
			rAdd = new Rectangle(rStar.X - S(8) - addW, rMenu.Y, addW, btn);

			// the search box sits on the first row when it fits, else it takes the second row with the tabs, else a row of its own
			int searchW = Math.Min(S(220), Math.Max(S(140), Width / 5));
			int titleRight = S(6) + S(34) + S(10) + TitleWidth();
			int tabsW = 0;
			for (int i = 0; i < FilterKeys.Length; i++)
			{
				if (Shows(i))
				{
					tabsW += TabWidth(i) + S(4);
				}
			}
			int rowY = y + rowH + S(4);
			int tabH = S(32);
			if (rAdd.X - titleRight - S(16) >= searchW)
			{
				rSearch = new Rectangle(rAdd.X - S(12) - searchW, y + (rowH - S(32)) / 2, searchW, S(32));
				PlaceTabs(pad, rowY, tabH, Width - pad);
				rowY += tabH;
			}
			else if (tabsW + searchW + S(24) <= Width - 2 * pad)
			{
				rSearch = new Rectangle(Width - pad - searchW, rowY + (tabH - S(32)) / 2, searchW, S(32));
				PlaceTabs(pad, rowY, tabH, rSearch.X - S(12));
				rowY += tabH;
			}
			else
			{
				PlaceTabs(pad, rowY, tabH, Width - pad);
				rowY += tabH + S(4);
				rSearch = new Rectangle(pad, rowY, Width - 2 * pad, S(32));
				rowY += S(32);
			}
			SearchBox.Location = new Point(rSearch.X + S(34), rSearch.Y + (rSearch.Height - SearchBox.Height) / 2);
			SearchBox.Width = Math.Max(10, rSearch.Width - S(34) - S(10));
			rLine = new Rectangle(0, rowY + S(2), Width, 1);
			int bottom = rowY + S(8);

			if (UpdateNote.Length > 0)
			{
				rUpdate = new Rectangle(pad, bottom, Width - 2 * pad, S(28));
				bottom += S(34);
			}
			else
			{
				rUpdate = Rectangle.Empty;
			}

			if (AddOpen)
			{
				rAddPanel = new Rectangle(pad, bottom, Width - 2 * pad, S(112));
				rHint = new Rectangle(rAddPanel.X + S(14), rAddPanel.Y + S(8), rAddPanel.Width - S(28), S(20));
				rLinkButton = new Rectangle(rAddPanel.Right - S(14) - S(84), rAddPanel.Y + S(34), S(84), S(32));
				rLinkField = new Rectangle(rAddPanel.X + S(14), rAddPanel.Y + S(34), rLinkButton.X - S(8) - (rAddPanel.X + S(14)), S(32));
				LinkBox.Location = new Point(rLinkField.X + S(10), rLinkField.Y + (rLinkField.Height - LinkBox.Height) / 2);
				LinkBox.Width = Math.Max(10, rLinkField.Width - S(20));
				bottom = rAddPanel.Bottom + S(8);
			}
			else
			{
				rAddPanel = Rectangle.Empty;
			}

			int height = bottom + S(2);
			if (Height != height)
			{
				Height = height;
			}
			Invalidate();
		}

		private int TitleWidth()
		{
			using (Graphics g = CreateGraphics())
			{
				return Theme.Measure(g, "HALF GRIP MODS", Theme.Pixel(28)) + S(4);
			}
		}

		private int TabWidth(int i)
		{
			using (Graphics g = CreateGraphics())
			{
				return Theme.Measure(g, FilterLabels[i], Theme.Pixel(15)) + S(22);
			}
		}

		private void PlaceTabs(int x, int y, int h, int right)
		{
			for (int i = 0; i < FilterKeys.Length; i++)
			{
				if (!Shows(i))
				{
					rTabs[i] = Rectangle.Empty;
					continue;
				}
				int w = TabWidth(i);
				rTabs[i] = new Rectangle(x, y, w, h);
				x += w + S(4);
			}
		}

		protected override void Draw(Graphics g)
		{
			DrawTitleRow(g);
			DrawTabs(g);

			if (!rUpdate.IsEmpty)
			{
				Add(rUpdate, () => UpdateClicked?.Invoke(), "Open the download page");
				Theme.FillRound(g, rUpdate, S(5), Theme.Mix(Theme.Bg, Theme.Amber, Over(rUpdate) ? 0.22f : 0.12f), Theme.Mix(Theme.Line, Theme.Amber, 0.6f));
				Theme.Write(g, "A newer installer is out (" + UpdateNote + "). Download it", Theme.Body(9f, true), Theme.Amber,
					new Rectangle(rUpdate.X + S(12), rUpdate.Y, rUpdate.Width - S(24), rUpdate.Height), StringAlignment.Near, StringAlignment.Center);
			}
			if (AddOpen)
			{
				DrawAddPanel(g);
			}
		}

		private void DrawTitleRow(Graphics g)
		{
			Image logo = Theme.BuiltIn("installer");
			if (logo != null)
			{
				Theme.DrawPixels(g, logo, rLogo);
			}
			Font title = Theme.Pixel(28);
			int firstW = Theme.Measure(g, "HALF GRIP", title);
			Rectangle at = new Rectangle(rLogo.Right + S(10), rLogo.Y - S(4), TitleWidth() + S(20), rLogo.Height + S(8));
			Theme.Write(g, "HALF GRIP", title, Theme.Ink, at, StringAlignment.Near, StringAlignment.Center);
			Theme.Write(g, "MODS", title, Theme.Red, new Rectangle(at.X + firstW + S(8), at.Y, at.Width, at.Height), StringAlignment.Near, StringAlignment.Center);

			Add(rAdd, () =>
			{
				AddOpen = !AddOpen;
				LinkMessage = "";
				Apply();
				if (AddOpen)
				{
					LinkBox.Focus();
				}
			}, AddOpen ? "Close" : "Add mods from a GitHub link");
			if (addLabel)
			{
				Theme.Outline(g, rAdd, AddOpen ? "CLOSE" : "+ ADD", Theme.Green, AddOpen, Over(rAdd), 15);
			}
			else
			{
				Theme.GlyphButton(g, rAdd, AddOpen ? "close" : "plus", Theme.Green, Over(rAdd), AddOpen);
			}

			Add(rStar, () => StarClicked?.Invoke(), "Opens the GitHub page. Press Star there if you like the mods");
			if (starLabel)
			{
				Theme.Outline(g, rStar, "", Theme.Amber, false, Over(rStar), 15);
				Theme.Star(g, new Point(rStar.X + S(20), rStar.Y + rStar.Height / 2), S(8), Theme.Amber);
				Theme.Write(g, "STAR", Theme.Pixel(15), Over(rStar) ? Theme.White : Theme.Amber, new Rectangle(rStar.X + S(34), rStar.Y, rStar.Width - S(36), rStar.Height), StringAlignment.Near, StringAlignment.Center);
			}
			else
			{
				Theme.GlyphButton(g, rStar, "star", Theme.Amber, Over(rStar));
			}

			Add(rMenu, () => MenuClicked?.Invoke(new Point(rMenu.Right - S(220), rMenu.Bottom + S(2))), "Menu");
			Theme.GlyphButton(g, rMenu, "menu", Theme.Soft, Over(rMenu));

			Theme.FillRound(g, rSearch, S(6), Theme.Row, Over(rSearch) ? Theme.Mix(Theme.Line, Theme.Soft, 0.3f) : Theme.Line);
			Theme.Glyph(g, new Rectangle(rSearch.X + S(6), rSearch.Y, S(24), rSearch.Height), "search", Theme.Dim);
		}

		private void DrawTabs(Graphics g)
		{
			for (int i = 0; i < FilterKeys.Length; i++)
			{
				if (rTabs[i].IsEmpty)
				{
					continue;
				}
				string key = FilterKeys[i];
				Rectangle tab = rTabs[i];
				bool on = Filter == key;
				Add(tab, () =>
				{
					Filter = key;
					FilterChanged?.Invoke(key);
					Invalidate();
				});
				Color color = on ? Theme.White : (Over(tab) ? Theme.Ink : (key == "recommended" ? Theme.Amber : Theme.Soft));
				Theme.Write(g, FilterLabels[i], Theme.Pixel(15), color, tab, StringAlignment.Center, StringAlignment.Center);
				if (on)
				{
					using (Pen underline = new Pen(Theme.Red, Math.Max(2, S(2))))
					{
						g.DrawLine(underline, tab.X + S(6), tab.Bottom - 1, tab.Right - S(6), tab.Bottom - 1);
					}
				}
			}
			using (Pen line = new Pen(Theme.Line))
			{
				g.DrawLine(line, rLine.X, rLine.Y, rLine.Right, rLine.Y);
			}
		}

		private void DrawAddPanel(Graphics g)
		{
			Theme.FillRound(g, rAddPanel, S(8), Theme.Panel, Theme.Line);
			Theme.Write(g, "Paste a link to someone's mods (a GitHub page or a .zip). Only add links from people you trust.", Theme.Body(9f), Theme.Soft, rHint);
			Theme.FillRound(g, rLinkField, S(6), Theme.Row, Theme.Line);
			Add(rLinkButton, () => LinkSubmitted?.Invoke(LinkBox.Text));
			Theme.GameButton(g, rLinkButton, "ADD", ButtonLook.Red, true, Over(rLinkButton), 15);

			Rectangle line = new Rectangle(rAddPanel.X + S(14), rAddPanel.Y + S(76), rAddPanel.Width - S(28), S(24));
			if (LinkMessage.Length > 0)
			{
				Theme.Write(g, LinkMessage, Theme.Body(9f), Color.FromArgb(235, 110, 110), line, StringAlignment.Near, StringAlignment.Center);
				return;
			}
			if (Sources.Count == 0)
			{
				Theme.Write(g, "No links added yet.", Theme.Body(9f), Theme.Dim, line, StringAlignment.Near, StringAlignment.Center);
				return;
			}
			int x = line.X;
			for (int i = 0; i < Sources.Count; i++)
			{
				Source source = Sources[i];
				int w = Math.Min(S(260), Theme.Measure(g, source.Name, Theme.Body(8.5f)) + S(44));
				if (x + w > line.Right)
				{
					Theme.Write(g, "+" + (Sources.Count - i) + " more", Theme.Body(8.5f), Theme.Dim, new Rectangle(x, line.Y, S(80), line.Height), StringAlignment.Near, StringAlignment.Center);
					break;
				}
				Rectangle chip = new Rectangle(x, line.Y, w, line.Height);
				Rectangle cross = new Rectangle(chip.Right - S(26), chip.Y, S(26), chip.Height);
				Theme.FillRound(g, chip, S(5), Theme.Row, Theme.Line);
				Theme.Write(g, source.Name, Theme.Body(8.5f), Theme.Ink, new Rectangle(chip.X + S(10), chip.Y, w - S(38), chip.Height), StringAlignment.Near, StringAlignment.Center);
				Add(cross, () => LinkRemoved?.Invoke(source), "Take this link out of the list");
				Theme.Glyph(g, cross, "close", Over(cross) ? Theme.Red : Theme.Soft);
				x += w + S(8);
			}
		}
	}
}
