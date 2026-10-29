using System;
using System.Drawing;
using System.Windows.Forms;

namespace HalfGripModsInstaller
{
	/// <summary>
	/// A page that replaces the grid: the details of one mod (a big picture, its text, where it comes from and its button), or a plain
	/// text page for "How to use" and "About". Back, Esc or the close button return to the grid. It only shows what it is given.
	/// </summary>
	internal sealed class DetailsPanel : Surface
	{
		internal event Action Closed;

		/// <summary>The button on a mod's page was pressed.</summary>
		internal event Action<Face> ButtonClicked;

		internal event Action<string> OpenUrlRequested;

		private Face face;
		private string infoTitle = "";
		private string infoText = "";
		private bool busy;

		/// <summary>The key of the card being shown, so the page can follow a refresh. Empty for a text page.</summary>
		internal string ShownKey => face?.Key ?? "";

		internal bool IsInfo => face == null;

		internal void ShowFace(Face value, bool isBusy)
		{
			face = value;
			busy = isBusy;
			Invalidate();
		}

		internal void ShowInfo(string title, string text)
		{
			face = null;
			infoTitle = title;
			infoText = text;
			Invalidate();
		}

		internal override void Refit()
		{
			Invalidate();
		}

		protected override void Draw(Graphics g)
		{
			int pad = S(18);
			Rectangle back = new Rectangle(pad, S(14), S(96), S(34));
			Add(back, () => Closed?.Invoke(), "Back to the mods");
			Theme.Outline(g, back, "", Theme.Soft, false, Over(back), 15);
			Theme.Glyph(g, new Rectangle(back.X + S(2), back.Y, S(28), back.Height), "back", Over(back) ? Theme.White : Theme.Soft);
			Theme.Write(g, "BACK", Theme.Pixel(15), Over(back) ? Theme.White : Theme.Soft, new Rectangle(back.X + S(32), back.Y, back.Width - S(34), back.Height), StringAlignment.Near, StringAlignment.Center);

			if (face == null)
			{
				DrawInfo(g, pad, back);
				return;
			}
			DrawMod(g, pad, back);
		}

		private void DrawInfo(Graphics g, int pad, Rectangle back)
		{
			Theme.Write(g, infoTitle, Theme.Pixel(30), Theme.White, new Rectangle(pad, back.Bottom + S(14), Width - 2 * pad, S(40)));
			Theme.Write(g, infoText, Theme.Body(10f), Theme.Soft, new Rectangle(pad, back.Bottom + S(62), Math.Min(Width - 2 * pad, S(760)), Height - back.Bottom - S(76)), StringAlignment.Near, StringAlignment.Near, true);
		}

		private void DrawMod(Graphics g, int pad, Rectangle back)
		{
			Face f = face;
			int top = back.Bottom + S(16);
			int tileSize = Math.Min(Math.Max(S(96), Math.Min(S(190), Height - top - S(70))), Math.Max(S(96), Width / 4));
			Rectangle tile = new Rectangle(pad, top, tileSize, tileSize);
			Theme.FillRound(g, tile, S(8), Color.FromArgb(14, 14, 18), Theme.Mix(Theme.Line, f.Accent, 0.4f), 1.5f);
			Image picture = HubCanvas.PictureOf(f);
			Rectangle inner = Rectangle.Inflate(tile, -S(14), -S(14));
			if (picture != null)
			{
				Theme.DrawPixels(g, picture, inner);
			}
			else
			{
				Theme.Placeholder(g, inner, f.Name, f.Accent);
			}

			int x = tile.Right + S(24);
			int w = Width - x - pad;
			int nameH = Theme.Pixel(32).Height;
			Theme.Write(g, f.Name, Theme.Pixel(32), Theme.White, new Rectangle(x, top - S(6), w, nameH));
			int line2 = top - S(6) + nameH + S(4);
			int stateW = Theme.Measure(g, f.State, Theme.Pixel(12)) + S(16);
			Theme.Chip(g, new Rectangle(x, line2, stateW, S(20)), f.State, f.StateColor, 12);
			string sub = f.Version + (f.Author.Length > 0 ? "   by " + f.Author : "");
			Theme.Write(g, sub, Theme.Body(9.5f, true), f.Accent, new Rectangle(x + stateW + S(10), line2 - S(2), Math.Max(0, w - stateW - S(10)), S(24)), StringAlignment.Near, StringAlignment.Center);

			int bottom = Height - S(16);
			int buttonW = Math.Min(S(220), w);
			Rectangle button = new Rectangle(x, bottom - S(40), buttonW, S(40));
			bool canClick = f.ButtonEnabled && !busy;
			Add(button, canClick ? () => ButtonClicked?.Invoke(f) : (Action)null, f.ButtonTip);
			Theme.GameButton(g, button, f.Button, f.Grey ? ButtonLook.Grey : ButtonLook.Red, canClick, canClick && Over(button), 18);
			if (f.Url.Length > 0)
			{
				Rectangle open = new Rectangle(button.Right + S(10), button.Y + S(3), Math.Min(S(150), Math.Max(0, x + w - button.Right - S(10))), S(34));
				if (open.Width > S(90))
				{
					Add(open, () => OpenUrlRequested?.Invoke(f.Url), "Open the GitHub page");
					Theme.Outline(g, open, "ON GITHUB", Theme.Soft, false, Over(open), 14);
				}
			}

			int textTop = line2 + S(34);
			Rectangle text = new Rectangle(x, textTop, w, Math.Max(S(20), button.Y - S(34) - textTop));
			Theme.Write(g, f.Text.Length > 0 ? f.Text : "No description.", Theme.Body(10f), f.Text.Length > 0 ? f.TextColor : Theme.Dim, text, StringAlignment.Near, StringAlignment.Near, true);
			int line = button.Y - S(30);
			if (f.Warning.Length > 0)
			{
				Theme.Write(g, f.Warning, Theme.Body(9f), Theme.Amber, new Rectangle(x, line - S(18), w, S(18)));
			}
			if (f.From.Length > 0)
			{
				Theme.Write(g, "From " + f.From, Theme.Body(8.5f), Theme.Dim, new Rectangle(x, line, w, S(20)));
			}
		}
	}
}
