using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace HalfGripModsInstaller
{
	internal enum ButtonLook
	{
		/// <summary>The game's red menu button.</summary>
		Red,

		/// <summary>The same button in grey, for the quiet actions.</summary>
		Grey
	}

	/// <summary>
	/// How the window looks: the colours of the in-game mod manager (HalfGripTools ModManagerUI) and of the game's menu buttons
	/// (red face, grey bezel, dark pixel text), a pixel font, and the drawing code both share. Nothing from the game itself is used.
	/// </summary>
	internal static class Theme
	{
		internal static readonly Color Bg = Color.FromArgb(10, 10, 12);
		internal static readonly Color Panel = Color.FromArgb(20, 20, 25);
		internal static readonly Color Row = Color.FromArgb(31, 31, 38);
		internal static readonly Color RowHot = Color.FromArgb(40, 40, 49);
		internal static readonly Color Line = Color.FromArgb(52, 52, 62);
		internal static readonly Color Bezel = Color.FromArgb(67, 67, 67);
		internal static readonly Color Red = Color.FromArgb(209, 41, 41);
		internal static readonly Color RedLight = Color.FromArgb(228, 78, 78);
		internal static readonly Color RedDark = Color.FromArgb(142, 27, 27);
		internal static readonly Color RedDeep = Color.FromArgb(104, 22, 22);
		internal static readonly Color Ink = Color.FromArgb(208, 208, 208);
		internal static readonly Color White = Color.FromArgb(242, 242, 246);
		internal static readonly Color Soft = Color.FromArgb(158, 158, 173);
		internal static readonly Color Dim = Color.FromArgb(104, 104, 117);
		internal static readonly Color Green = Color.FromArgb(64, 184, 89);
		internal static readonly Color Amber = Color.FromArgb(242, 184, 51);

		/// <summary>The dark text on the game's red buttons.</summary>
		internal static readonly Color ButtonText = Color.FromArgb(28, 28, 28);

		/// <summary>Card colours, the same set the HalfGripMods poster uses. A mod gets the same one every time.</summary>
		private static readonly Color[] Accents =
		{
			Color.FromArgb(210, 214, 224), Color.FromArgb(214, 130, 60), Color.FromArgb(210, 70, 60), Color.FromArgb(110, 214, 255),
			Color.FromArgb(230, 170, 80), Color.FromArgb(80, 200, 120), Color.FromArgb(80, 150, 240), Color.FromArgb(240, 90, 70),
			Color.FromArgb(120, 210, 190), Color.FromArgb(240, 200, 70), Color.FromArgb(190, 120, 240)
		};

		internal static Color AccentFor(string key)
		{
			uint hash = 2166136261;
			foreach (char c in key ?? "")
			{
				hash = (hash ^ c) * 16777619;
			}
			return Accents[hash % Accents.Length];
		}

		internal static Color Mix(Color a, Color b, float amount)
		{
			return Color.FromArgb((int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
		}

		// ---- scale and fonts

		/// <summary>
		/// How much of its full size the window draws at, from 0.7 to 1. The window first reflows (fewer card columns, stacked bars);
		/// only a window smaller than 960x600 also shrinks everything a little. Set by the main window whenever it is resized.
		/// </summary>
		internal static float Scale = 1f;

		internal static float ScaleFor(int width, int height)
		{
			return Math.Max(0.7f, Math.Min(1f, Math.Min(width / 960f, height / 600f)));
		}

		private static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
		private static FontFamily pixelFamily;
		private static PrivateFontCollection collection;

		private static FontFamily PixelFamily()
		{
			if (pixelFamily != null)
			{
				return pixelFamily;
			}
			try
			{
				using (Stream stream = typeof(Theme).Assembly.GetManifestResourceStream("VT323-Regular.ttf"))
				{
					byte[] data = new byte[stream.Length];
					int read = 0;
					while (read < data.Length)
					{
						read += stream.Read(data, read, data.Length - read);
					}
					// GDI+ keeps reading the font from this memory, so it is never freed.
					IntPtr memory = Marshal.AllocCoTaskMem(data.Length);
					Marshal.Copy(data, 0, memory, data.Length);
					collection = new PrivateFontCollection();
					collection.AddMemoryFont(memory, data.Length);
					pixelFamily = collection.Families[0];
				}
			}
			catch (Exception)
			{
				pixelFamily = new FontFamily("Consolas");
			}
			return pixelFamily;
		}

		/// <summary>The pixel font (VT323), for titles, names and buttons. Capital letters, digits and plain punctuation only.</summary>
		internal static Font Pixel(float points)
		{
			// The pixel font stays readable down to 9 points.
			float size = (float)Math.Round(Math.Max(9f, points * Scale), 1);
			string key = "p" + size;
			if (!fonts.TryGetValue(key, out Font font))
			{
				fonts[key] = font = new Font(PixelFamily(), size, FontStyle.Regular, GraphicsUnit.Point);
			}
			return font;
		}

		/// <summary>Plain Segoe UI for sentences, where a pixel font would be hard to read.</summary>
		internal static Font Body(float points, bool bold = false)
		{
			float size = (float)Math.Round(Math.Max(7.5f, points * Scale), 1);
			string key = "b" + size + bold;
			if (!fonts.TryGetValue(key, out Font font))
			{
				fonts[key] = font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
			}
			return font;
		}

		// ---- drawing

		internal static void Quality(Graphics g)
		{
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.PixelOffsetMode = PixelOffsetMode.Half;
			g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
		}

		internal static GraphicsPath Round(Rectangle r, int radius)
		{
			GraphicsPath path = new GraphicsPath();
			int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
			path.AddArc(r.X, r.Y, d, d, 180, 90);
			path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
			path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
			path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
			path.CloseFigure();
			return path;
		}

		internal static void FillRound(Graphics g, Rectangle r, int radius, Color fill, Color? border = null, float borderWidth = 1f)
		{
			using (GraphicsPath path = Round(r, radius))
			{
				using (SolidBrush brush = new SolidBrush(fill))
				{
					g.FillPath(brush, path);
				}
				if (border.HasValue)
				{
					using (Pen pen = new Pen(border.Value, borderWidth) { LineJoin = LineJoin.Round })
					{
						g.DrawPath(pen, path);
					}
				}
			}
		}

		/// <summary>Draws one line of text (or wrapped lines, cut with "..." when they do not fit) inside the rectangle.</summary>
		internal static void Write(Graphics g, string text, Font font, Color color, Rectangle r, StringAlignment horizontal = StringAlignment.Near, StringAlignment vertical = StringAlignment.Near, bool wrap = false)
		{
			if (string.IsNullOrEmpty(text) || r.Width <= 0 || r.Height <= 0)
			{
				return;
			}
			using (StringFormat format = new StringFormat(StringFormatFlags.NoClip) { Alignment = horizontal, LineAlignment = vertical, Trimming = wrap ? StringTrimming.EllipsisWord : StringTrimming.EllipsisCharacter })
			using (SolidBrush brush = new SolidBrush(color))
			{
				if (wrap)
				{
					format.FormatFlags |= StringFormatFlags.LineLimit;
				}
				else
				{
					format.FormatFlags |= StringFormatFlags.NoWrap;
				}
				g.DrawString(text, font, brush, r, format);
			}
		}

		internal static int Measure(Graphics g, string text, Font font)
		{
			// Measured with the same format Write draws with, so what is measured is what is shown.
			using (StringFormat format = new StringFormat(StringFormatFlags.NoClip | StringFormatFlags.NoWrap))
			{
				return (int)Math.Ceiling(g.MeasureString(text, font, 4000, format).Width);
			}
		}

		/// <summary>The game's menu button: a grey bezel round a red face with a light top edge and a dark bottom edge, dark pixel text.</summary>
		internal static void GameButton(Graphics g, Rectangle r, string text, ButtonLook look, bool enabled, bool hot, float points)
		{
			int bezel = Math.Max(2, r.Height / 9);
			FillRound(g, r, Math.Max(3, r.Height / 6), Bezel, Color.FromArgb(28, 28, 28));
			Rectangle face = Rectangle.Inflate(r, -bezel, -bezel);
			Color body = look == ButtonLook.Red ? (enabled ? (hot ? RedLight : Red) : RedDeep) : (enabled ? (hot ? Color.FromArgb(120, 120, 120) : Color.FromArgb(96, 96, 96)) : Color.FromArgb(58, 58, 58));
			FillRound(g, face, Math.Max(2, face.Height / 8), body);
			int edge = Math.Max(2, face.Height / 12);
			using (SolidBrush light = new SolidBrush(Mix(body, Color.White, 0.22f)))
			using (SolidBrush shade = new SolidBrush(Mix(body, Color.Black, 0.35f)))
			{
				g.FillRectangle(light, face.X + edge, face.Y + 1, face.Width - 2 * edge, edge);
				g.FillRectangle(shade, face.X + edge, face.Bottom - edge - 1, face.Width - 2 * edge, edge);
			}
			Color ink = enabled ? ButtonText : Mix(body, Color.Black, 0.55f);
			Write(g, text, Pixel(points), ink, new Rectangle(face.X, face.Y - 1, face.Width, face.Height), StringAlignment.Center, StringAlignment.Center);
		}

		/// <summary>A dark button with a coloured outline, for the secondary actions and the filter chips.</summary>
		internal static void Outline(Graphics g, Rectangle r, string text, Color color, bool filled, bool hot, float points)
		{
			Color fill = filled ? Mix(Bg, color, hot ? 0.28f : 0.18f) : (hot ? RowHot : Panel);
			FillRound(g, r, Math.Max(3, r.Height / 5), fill, filled || hot ? color : Mix(Line, color, 0.25f), 1.5f);
			Write(g, text, Pixel(points), filled || hot ? White : Mix(Soft, color, 0.3f), r, StringAlignment.Center, StringAlignment.Center);
		}

		/// <summary>A five-pointed star, for the "star on GitHub" button (the pixel font has no star).</summary>
		internal static void Star(Graphics g, Point centre, int radius, Color color)
		{
			PointF[] points = new PointF[10];
			for (int i = 0; i < 10; i++)
			{
				double angle = -Math.PI / 2 + i * Math.PI / 5;
				float r = i % 2 == 0 ? radius : radius * 0.42f;
				points[i] = new PointF(centre.X + (float)(Math.Cos(angle) * r), centre.Y + (float)(Math.Sin(angle) * r));
			}
			using (SolidBrush brush = new SolidBrush(color))
			{
				g.FillPolygon(brush, points);
			}
		}

		/// <summary>A small tag in a card corner: coloured text on a tinted background.</summary>
		internal static void Chip(Graphics g, Rectangle r, string text, Color color, float points = 11f)
		{
			FillRound(g, r, Math.Max(2, r.Height / 4), Mix(Bg, color, 0.2f), Mix(Bg, color, 0.45f));
			Write(g, text, Pixel(points), color, r, StringAlignment.Center, StringAlignment.Center);
		}

		/// <summary>Pixel art stays sharp: scaled with nearest-neighbour and kept in proportion.</summary>
		internal static void DrawPixels(Graphics g, Image image, Rectangle target)
		{
			InterpolationMode old = g.InterpolationMode;
			PixelOffsetMode offset = g.PixelOffsetMode;
			g.InterpolationMode = InterpolationMode.NearestNeighbor;
			g.PixelOffsetMode = PixelOffsetMode.Half;
			float scale = Math.Min((float)target.Width / image.Width, (float)target.Height / image.Height);
			int w = Math.Max(1, (int)(image.Width * scale));
			int h = Math.Max(1, (int)(image.Height * scale));
			g.DrawImage(image, new Rectangle(target.X + (target.Width - w) / 2, target.Y + (target.Height - h) / 2, w, h), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
			g.InterpolationMode = old;
			g.PixelOffsetMode = offset;
		}

		/// <summary>Stand-in for a mod with no icon: its first letter on a coloured tile.</summary>
		internal static void Placeholder(Graphics g, Rectangle r, string name, Color accent)
		{
			FillRound(g, r, 4, Mix(Bg, accent, 0.25f), Mix(Bg, accent, 0.55f));
			Write(g, string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant(), Pixel(r.Height * 0.55f * 72f / g.DpiY), accent, r, StringAlignment.Center, StringAlignment.Center);
		}

		// ---- icons

		private static readonly Dictionary<string, Image> images = new Dictionary<string, Image>();

		/// <summary>Loads a picture without keeping the file open (the temp folder is deleted later). Null if it cannot be read.</summary>
		internal static Image LoadImage(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}
			if (!images.TryGetValue(path, out Image image))
			{
				try
				{
					using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(path)))
					using (Bitmap loaded = new Bitmap(stream))
					{
						image = Trim(loaded);
					}
				}
				catch (Exception)
				{
					image = null;
				}
				images[path] = image;
			}
			return image;
		}

		private static readonly Dictionary<string, Image> builtIn = new Dictionary<string, Image>();

		/// <summary>A copy cut down to the part that is not transparent, so a picture with an empty margin still fills its tile.</summary>
		private static Bitmap Trim(Bitmap source)
		{
			int left = source.Width, top = source.Height, right = -1, bottom = -1;
			for (int y = 0; y < source.Height; y++)
			{
				for (int x = 0; x < source.Width; x++)
				{
					if (source.GetPixel(x, y).A > 8)
					{
						left = Math.Min(left, x);
						right = Math.Max(right, x);
						top = Math.Min(top, y);
						bottom = Math.Max(bottom, y);
					}
				}
			}
			if (right < left || bottom < top)
			{
				return new Bitmap(source);
			}
			return source.Clone(new Rectangle(left, top, right - left + 1, bottom - top + 1), PixelFormat.Format32bppArgb);
		}

		/// <summary>A picture built into the program ("bepinex", "tools", "installer"), used until, or instead of, the one from the config.</summary>
		internal static Image BuiltIn(string name)
		{
			if (!builtIn.TryGetValue(name, out Image image))
			{
				try
				{
					using (Stream stream = typeof(Theme).Assembly.GetManifestResourceStream(name + ".png"))
					using (Bitmap loaded = new Bitmap(stream))
					{
						image = Trim(loaded);
					}
				}
				catch (Exception)
				{
					image = null;
				}
				builtIn[name] = image;
			}
			return image;
		}

		/// <summary>A square button with a drawn symbol ("menu", "plus", "close", "back", "search", "star"), for the controls that need no words.</summary>
		internal static void GlyphButton(Graphics g, Rectangle r, string glyph, Color color, bool hot, bool active = false)
		{
			FillRound(g, r, Math.Max(3, r.Height / 6), active ? Mix(Bg, color, 0.22f) : (hot ? RowHot : Panel), active || hot ? color : Mix(Line, color, 0.2f), 1.5f);
			Glyph(g, r, glyph, hot || active ? White : Mix(Soft, color, 0.35f));
		}

		internal static void Glyph(Graphics g, Rectangle r, string glyph, Color color)
		{
			int cx = r.X + r.Width / 2;
			int cy = r.Y + r.Height / 2;
			int u = Math.Max(2, Math.Min(r.Width, r.Height) / 10);
			using (SolidBrush brush = new SolidBrush(color))
			using (Pen pen = new Pen(color, Math.Max(2, u)) { StartCap = LineCap.Square, EndCap = LineCap.Square, LineJoin = LineJoin.Miter })
			{
				switch (glyph)
				{
					case "menu":
						for (int i = -1; i <= 1; i++)
						{
							g.FillRectangle(brush, cx - u * 3, cy + i * u * 2 - u / 2, u * 6, Math.Max(2, u));
						}
						break;
					case "plus":
						g.FillRectangle(brush, cx - u * 3, cy - u / 2, u * 6, Math.Max(2, u));
						g.FillRectangle(brush, cx - u / 2, cy - u * 3, Math.Max(2, u), u * 6);
						break;
					case "close":
						g.DrawLine(pen, cx - u * 2, cy - u * 2, cx + u * 2, cy + u * 2);
						g.DrawLine(pen, cx - u * 2, cy + u * 2, cx + u * 2, cy - u * 2);
						break;
					case "back":
						g.DrawLines(pen, new[] { new Point(cx + u, cy - u * 3), new Point(cx - u * 2, cy), new Point(cx + u, cy + u * 3) });
						break;
					case "search":
						g.DrawEllipse(pen, cx - u * 3, cy - u * 3, u * 4, u * 4);
						g.DrawLine(pen, cx, cy, cx + u * 3, cy + u * 3);
						break;
					case "star":
						Star(g, new Point(cx, cy + 1), u * 4, color);
						break;
				}
			}
		}

		internal static string Fingerprint(string text)
		{
			using (SHA256 sha = SHA256.Create())
			{
				StringBuilder hex = new StringBuilder();
				foreach (byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(text)))
				{
					hex.Append(b.ToString("x2"));
				}
				return hex.ToString().Substring(0, 12);
			}
		}

		// ---- native bits

		[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
		private static extern int SetWindowTheme(IntPtr window, string appName, string idList);

		/// <summary>Dark scroll bars on Windows 10 (1809) and 11. Older Windows keeps the light ones.</summary>
		internal static void DarkScrollbars(IntPtr handle)
		{
			try
			{
				SetWindowTheme(handle, "DarkMode_Explorer", null);
			}
			catch (Exception)
			{
				// Cosmetic only.
			}
		}

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);

		/// <summary>The grey hint inside an empty text box.</summary>
		internal static void CueBanner(IntPtr handle, string text)
		{
			SendMessage(handle, 0x1501, IntPtr.Zero, text);
		}
	}
}
