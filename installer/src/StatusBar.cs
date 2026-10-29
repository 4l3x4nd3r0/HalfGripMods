using System;
using System.Drawing;
using System.Windows.Forms;

namespace HalfGripModsInstaller
{
	/// <summary>Where the installer is, for the status bar.</summary>
	internal enum Phase
	{
		NoGame,
		Checking,
		Ready,
		Installing,
		Done
	}

	/// <summary>
	/// The slim bar at the bottom: what is going on in a few words, when the information is from, and the one main button. On a narrow
	/// window the button drops under the text. It only shows what MainForm sets and raises events; it decides nothing.
	/// </summary>
	internal sealed class StatusBar : Surface
	{
		internal event Action PrimaryClicked;

		internal event Action CheckNowClicked;

		internal Phase Phase = Phase.NoGame;
		internal string Headline = "";
		internal string Detail = "";
		internal string Primary = "";
		internal bool PrimaryEnabled;

		/// <summary>"Updated 12 min ago", beside CHECK NOW.</summary>
		internal string CheckedText = "";

		/// <summary>True when "Check now" makes sense (a game folder is set).</summary>
		internal bool CanCheck;

		private readonly Timer animation = new Timer { Interval = 40 };
		private int tick;
		private Rectangle rPrimary, rText;
		private bool stacked;

		internal StatusBar()
		{
			animation.Tick += (s, e) =>
			{
				tick++;
				Invalidate(new Rectangle(0, 0, Width, S(4)));
			};
		}

		/// <summary>Call after changing any of the fields above.</summary>
		internal void Apply()
		{
			animation.Enabled = Phase == Phase.Checking || Phase == Phase.Installing;
			Refit();
		}

		internal override void Refit()
		{
			if (Width <= 0)
			{
				return;
			}
			stacked = Width < S(720);
			int pad = S(18);
			int height = stacked ? S(112) : S(68);
			if (Height != height)
			{
				Height = height;
			}
			int buttonW = stacked ? Width - 2 * pad : Math.Min(S(240), Width / 3);
			int buttonH = S(40);
			if (stacked)
			{
				rText = new Rectangle(pad, S(12), Width - 2 * pad, S(44));
				rPrimary = new Rectangle(pad, Height - S(14) - buttonH, buttonW, buttonH);
			}
			else
			{
				rPrimary = new Rectangle(Width - pad - buttonW, (Height - buttonH) / 2, buttonW, buttonH);
				rText = new Rectangle(pad, S(10), rPrimary.X - pad - S(16), Height - S(16));
			}
			Invalidate();
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

		protected override void Draw(Graphics g)
		{
			using (Pen line = new Pen(Theme.Line))
			{
				g.DrawLine(line, 0, 0, Width, 0);
			}
			Color headColor = Phase == Phase.Done ? Theme.Green : (Phase == Phase.NoGame ? Theme.Amber : Theme.White);
			int lineH = rText.Height / 2;
			Theme.Write(g, Headline, Theme.Pixel(20), headColor, new Rectangle(rText.X, rText.Y, rText.Width, lineH), StringAlignment.Near, StringAlignment.Far);
			Rectangle second = new Rectangle(rText.X, rText.Y + lineH + S(2), rText.Width, lineH - S(2));

			if ((Phase == Phase.Ready || Phase == Phase.Done) && CanCheck)
			{
				int checkW = Theme.Measure(g, "CHECK NOW", Theme.Pixel(14)) + S(6);
				int ageW = Theme.Measure(g, CheckedText, Theme.Body(8.5f)) + S(8);
				Rectangle now = new Rectangle(second.X + ageW + (ageW > S(8) ? S(6) : 0), second.Y, checkW, second.Height);
				Theme.Write(g, CheckedText, Theme.Body(8.5f), Theme.Dim, new Rectangle(second.X, second.Y, ageW, second.Height), StringAlignment.Near, StringAlignment.Near);
				Add(now, () => CheckNowClicked?.Invoke(), "Ask GitHub for the newest mods now");
				Theme.Write(g, "CHECK NOW", Theme.Pixel(14), Over(now) ? Theme.White : Theme.Soft, now, StringAlignment.Near, StringAlignment.Near);
				// A short note (for example "Start the game from Steam.") goes after it, if there is room.
				int rest = now.Right + S(14);
				Theme.Write(g, Detail, Theme.Body(8.5f), Theme.Soft, new Rectangle(rest, second.Y, Math.Max(0, second.Right - rest), second.Height), StringAlignment.Near, StringAlignment.Near);
			}
			else
			{
				Theme.Write(g, Detail, Theme.Body(8.5f), Theme.Soft, second, StringAlignment.Near, StringAlignment.Near);
			}

			Add(rPrimary, PrimaryEnabled ? () => PrimaryClicked?.Invoke() : (Action)null);
			Theme.GameButton(g, rPrimary, Primary, ButtonLook.Red, PrimaryEnabled, PrimaryEnabled && Over(rPrimary), 18);

			if (Phase == Phase.Checking || Phase == Phase.Installing)
			{
				Rectangle track = new Rectangle(0, 0, Width, S(3));
				using (SolidBrush back = new SolidBrush(Theme.Line))
				{
					g.FillRectangle(back, track);
				}
				int seg = Math.Max(S(60), track.Width / 4);
				int pos = (tick * S(9)) % (track.Width + seg) - seg;
				Rectangle bar = Rectangle.Intersect(track, new Rectangle(pos, 0, seg, track.Height));
				using (SolidBrush fill = new SolidBrush(Theme.Red))
				{
					g.FillRectangle(fill, bar);
				}
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				animation.Dispose();
			}
			base.Dispose(disposing);
		}
	}
}
