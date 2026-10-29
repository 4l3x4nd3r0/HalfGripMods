using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace HalfGripModsInstaller
{
	/// <summary>A clickable rectangle on a Surface. No Click means it only shows a tip (a disabled button).</summary>
	internal sealed class Area
	{
		internal Rectangle Bounds;

		internal Action Click;

		internal string Tip;
	}

	/// <summary>
	/// A control that draws itself, in the dark style. While it paints, it registers the rectangles that react to the mouse, so
	/// what you see and what you can click always agree. Later registrations sit on top of earlier ones.
	/// </summary>
	internal abstract class Surface : Control
	{
		private readonly ToolTip tip = new ToolTip { InitialDelay = 350, ReshowDelay = 100 };
		private readonly List<Area> areas = new List<Area>();
		private Area hot;
		private string shownTip;

		protected Surface()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			BackColor = Theme.Bg;
		}

		/// <summary>Pixels at the current screen scale (the window is drawn for 96 dpi and scaled) and the current window scale.</summary>
		protected int S(float value)
		{
			return (int)Math.Round(value * DeviceDpi / 96f * Theme.Scale);
		}

		/// <summary>Called when the window scale or size changed: work out where everything goes again.</summary>
		internal virtual void Refit()
		{
			Invalidate();
		}

		protected Area Add(Rectangle bounds, Action click, string tipText = null)
		{
			Area area = new Area { Bounds = bounds, Click = click, Tip = tipText };
			areas.Add(area);
			return area;
		}

		/// <summary>True while the mouse is on the rectangle (or on something inside it).</summary>
		protected bool Over(Rectangle bounds)
		{
			return hot != null && bounds.Contains(hot.Bounds);
		}

		private Area HitTest(Point point)
		{
			return areas.LastOrDefault(a => a.Bounds.Contains(point));
		}

		protected sealed override void OnPaint(PaintEventArgs e)
		{
			areas.Clear();
			Theme.Quality(e.Graphics);
			e.Graphics.Clear(BackColor);
			Draw(e.Graphics);
		}

		protected abstract void Draw(Graphics g);

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			Area now = HitTest(e.Location);
			bool changed = (now == null) != (hot == null) || (now != null && now.Bounds != hot.Bounds);
			hot = now;
			Cursor = now != null && now.Click != null ? Cursors.Hand : Cursors.Default;
			string text = now?.Tip;
			if (text != shownTip)
			{
				shownTip = text;
				tip.SetToolTip(this, text);
			}
			if (changed)
			{
				Invalidate();
			}
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			base.OnMouseLeave(e);
			if (hot != null)
			{
				hot = null;
				Invalidate();
			}
		}

		protected override void OnMouseClick(MouseEventArgs e)
		{
			base.OnMouseClick(e);
			if (e.Button == MouseButtons.Left)
			{
				HitTest(e.Location)?.Click?.Invoke();
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				tip.Dispose();
			}
			base.Dispose(disposing);
		}
	}
}
