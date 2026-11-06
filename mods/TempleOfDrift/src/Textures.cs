using System;
using System.Collections.Generic;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>
	/// Small point-filtered textures made in code (the game's own look is pixel art: its textures are 32 to 256 px and drawn without
	/// smoothing). Nothing is read from the game's files, and nothing has to ship as a picture.
	/// </summary>
	internal static class Tex
	{
		private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

		private static Texture2D Get(string name, int size, Func<int, int, Color> px)
		{
			Texture2D t;
			if (cache.TryGetValue(name, out t) && t != null)
			{
				return t;
			}
			t = new Texture2D(size, size, TextureFormat.RGBA32, true)
			{
				name = "ToD_" + name,
				wrapMode = TextureWrapMode.Repeat,
				filterMode = FilterMode.Point,
				hideFlags = HideFlags.HideAndDontSave
			};
			Color32[] pixels = new Color32[size * size];
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					pixels[y * size + x] = px(x, y);
				}
			}
			t.SetPixels32(pixels);
			t.Apply(true);
			cache[name] = t;
			return t;
		}

		// ---- noise (tileable) ---------------------------------------------------------------------------------------

		private static float Hash(int x, int y, int seed)
		{
			unchecked
			{
				int h = x * 374761393 + y * 668265263 + seed * 1442695041;
				h = (h ^ (h >> 13)) * 1274126177;
				h ^= h >> 16;
				return (h & 0xFFFF) / 65535f;
			}
		}

		/// <summary>Value noise that repeats every <paramref name="period"/> cells; x and y are in cells.</summary>
		private static float VNoise(float x, float y, int period, int seed)
		{
			int x0 = (int)Mathf.Floor(x), y0 = (int)Mathf.Floor(y);
			float fx = x - x0, fy = y - y0;
			fx = fx * fx * (3f - 2f * fx);
			fy = fy * fy * (3f - 2f * fy);
			int xa = ((x0 % period) + period) % period, xb = (xa + 1) % period;
			int ya = ((y0 % period) + period) % period, yb = (ya + 1) % period;
			float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
			return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
		}

		private static Color Mix(Color c, float amount)
		{
			return new Color(Mathf.Clamp01(c.r + amount), Mathf.Clamp01(c.g + amount), Mathf.Clamp01(c.b + amount), 1f);
		}

		private static Color Tinted(Color c, float r, float g, float b)
		{
			return new Color(Mathf.Clamp01(c.r + r), Mathf.Clamp01(c.g + g), Mathf.Clamp01(c.b + b), 1f);
		}

		// ---- the set -------------------------------------------------------------------------------------------------

		internal static Texture2D White
		{
			get { return Get("white", 4, (x, y) => Color.white); }
		}

		internal static Texture2D Asphalt
		{
			get
			{
				return Get("asphalt", 128, (x, y) =>
				{
					Color c = new Color(0.25f, 0.25f, 0.27f);
					float n = (Hash(x, y, 1) - 0.5f) * 0.07f + (VNoise(x / 16f, y / 16f, 8, 2) - 0.5f) * 0.05f;
					if (Hash(x, y, 3) > 0.985f)
					{
						n += 0.13f;
					}
					return Mix(c, n);
				});
			}
		}

		internal static Texture2D Dirt
		{
			get
			{
				return Get("dirt", 128, (x, y) =>
				{
					Color c = new Color(0.55f, 0.41f, 0.27f);
					float n = (Hash(x, y, 4) - 0.5f) * 0.09f + (VNoise(x / 12f, y / 12f, 10, 5) - 0.5f) * 0.12f;
					float p = Hash(x / 2, y / 2, 6);
					if (p > 0.965f)
					{
						return Mix(new Color(0.74f, 0.64f, 0.52f), n * 0.5f);
					}
					if (p < 0.035f)
					{
						return Mix(new Color(0.33f, 0.24f, 0.16f), n * 0.5f);
					}
					return Mix(c, n);
				});
			}
		}

		internal static Texture2D Grass
		{
			get
			{
				return Get("grass", 128, (x, y) =>
				{
					Color c = new Color(0.27f, 0.43f, 0.20f);
					float n = (Hash(x, y, 7) - 0.5f) * 0.07f + (VNoise(x / 10f, y / 10f, 12, 8) - 0.5f) * 0.16f;
					Color o = Tinted(c, n * 0.8f, n, n * 0.5f);
					if (Hash(x, y, 9) > 0.93f)
					{
						o = Tinted(o, 0.05f, 0.08f, 0.02f);
					}
					return o;
				});
			}
		}

		/// <summary>Needles and leaf litter under the trees: darker and browner than grass.</summary>
		internal static Texture2D Forest
		{
			get
			{
				return Get("forest", 128, (x, y) =>
				{
					Color c = new Color(0.21f, 0.30f, 0.15f);
					float n = (Hash(x, y, 10) - 0.5f) * 0.08f + (VNoise(x / 9f, y / 9f, 14, 11) - 0.5f) * 0.14f;
					Color o = Tinted(c, n * 0.9f, n, n * 0.4f);
					if (Hash(x, y, 12) > 0.94f)
					{
						o = new Color(0.36f, 0.26f, 0.14f);
					}
					return o;
				});
			}
		}

		internal static Texture2D Rock
		{
			get
			{
				return Get("rock", 128, (x, y) =>
				{
					Color c = new Color(0.40f, 0.38f, 0.34f);
					int row = y / 7;
					float strata = (Hash(row, 0, 13) - 0.5f) * 0.12f;
					float n = (Hash(x, y, 14) - 0.5f) * 0.07f + (VNoise(x / 14f, y / 6f, 9, 15) - 0.5f) * 0.1f;
					float crack = Hash(x / 3, y / 11, 16) > 0.93f ? -0.1f : 0f;
					return Mix(c, strata + n + crack);
				});
			}
		}

		internal static Texture2D CaveRock
		{
			get
			{
				return Get("caverock", 128, (x, y) =>
				{
					Color c = new Color(0.27f, 0.24f, 0.23f);
					int row = y / 9;
					float strata = (Hash(row, 0, 17) - 0.5f) * 0.1f;
					float n = (Hash(x, y, 18) - 0.5f) * 0.06f + (VNoise(x / 12f, y / 8f, 10, 19) - 0.5f) * 0.1f;
					float damp = VNoise(x / 20f, y / 20f, 6, 20);
					Color o = Mix(c, strata + n);
					if (damp > 0.62f)
					{
						o = Tinted(o, -0.05f, -0.03f, 0.02f);
					}
					return o;
				});
			}
		}

		internal static Texture2D Flowstone
		{
			get
			{
				return Get("flowstone", 64, (x, y) =>
				{
					Color c = new Color(0.80f, 0.74f, 0.62f);
					float wob = VNoise(x / 6f, y / 12f, 8, 21);
					float band = Mathf.Sin((x + wob * 9f) * 0.55f) * 0.07f;
					float n = (Hash(x, y, 22) - 0.5f) * 0.04f;
					return Mix(c, band + n - (y % 32 < 3 ? 0.06f : 0f));
				});
			}
		}

		internal static Texture2D Gravel
		{
			get
			{
				return Get("gravel", 128, (x, y) =>
				{
					Color c = new Color(0.36f, 0.32f, 0.28f);
					float n = (Hash(x, y, 23) - 0.5f) * 0.1f + (VNoise(x / 10f, y / 10f, 12, 24) - 0.5f) * 0.08f;
					float p = Hash(x / 2, y / 2, 25);
					if (p > 0.95f)
					{
						n += 0.16f;
					}
					return Mix(c, n);
				});
			}
		}

		internal static Texture2D Moss
		{
			get
			{
				return Get("moss", 64, (x, y) =>
				{
					Color c = new Color(0.20f, 0.33f, 0.17f);
					float n = (Hash(x, y, 26) - 0.5f) * 0.08f + (VNoise(x / 8f, y / 8f, 8, 27) - 0.5f) * 0.1f;
					return Tinted(c, n * 0.6f, n, n * 0.4f);
				});
			}
		}

		/// <summary>Vermilion lacquered wood, vertical planks.</summary>
		internal static Texture2D WoodRed
		{
			get
			{
				return Get("woodred", 64, (x, y) =>
				{
					Color c = new Color(0.74f, 0.15f, 0.10f);
					float grain = (VNoise(x / 2f, y / 10f, 32, 28) - 0.5f) * 0.09f;
					Color o = Tinted(c, grain, grain * 0.3f, grain * 0.2f);
					if (x % 16 == 0)
					{
						o = new Color(0.42f, 0.07f, 0.05f);
					}
					return o;
				});
			}
		}

		internal static Texture2D WoodDark
		{
			get
			{
				return Get("wooddark", 64, (x, y) =>
				{
					Color c = new Color(0.33f, 0.22f, 0.15f);
					float grain = (VNoise(x / 2f, y / 10f, 32, 29) - 0.5f) * 0.1f;
					Color o = Mix(c, grain);
					if (x % 16 == 0)
					{
						o = new Color(0.18f, 0.11f, 0.07f);
					}
					return o;
				});
			}
		}

		/// <summary>Light weathered pine boards, one board per unit of height: the grain runs along the board, a dark seam at its top and bottom edge.</summary>
		internal static Texture2D Planks
		{
			get
			{
				return Get("planks", 64, (x, y) =>
				{
					Color c = new Color(0.80f, 0.62f, 0.40f);
					float streak = (VNoise(y / 2f, 3f + (x / 16) * 0.37f, 32, 41) - 0.5f) * 0.16f;
					float fine = (VNoise(x / 2f, y / 2f, 32, 42) - 0.5f) * 0.05f;
					Color o = Mix(c, streak + fine);
					if (y < 2 || y > 61)
					{
						o = new Color(0.28f, 0.18f, 0.10f);   // the seam between two boards
					}
					else if (y < 5 || y > 58)
					{
						o = Mix(o, -0.07f);   // the board's rounded edge
					}
					return o;
				});
			}
		}

		internal static Texture2D Plaster		{
			get
			{
				return Get("plaster", 64, (x, y) =>
				{
					Color c = new Color(0.91f, 0.88f, 0.79f);
					return Mix(c, (Hash(x, y, 30) - 0.5f) * 0.04f + (VNoise(x / 10f, y / 10f, 6, 31) - 0.5f) * 0.05f);
				});
			}
		}

		/// <summary>Kawara roof tiles: rows of curved tiles, bright on the top edge of each row.</summary>
		internal static Texture2D Tile
		{
			get
			{
				return Get("tile", 64, (x, y) =>
				{
					int row = y / 8;
					int ry = y % 8;
					int col = ((x + (row % 2) * 8) % 16);
					Color c = Color.Lerp(new Color(0.38f, 0.42f, 0.50f), new Color(0.16f, 0.18f, 0.23f), ry / 7f);
					if (col == 0)
					{
						c = new Color(0.08f, 0.09f, 0.12f);
					}
					return Mix(c, (Hash(x, y, 32) - 0.5f) * 0.04f);
				});
			}
		}

		internal static Texture2D Stone
		{
			get
			{
				return Get("stone", 64, (x, y) =>
				{
					int row = y / 8;
					int bx = (x + (row % 2) * 8) / 16;
					Color c = new Color(0.52f, 0.50f, 0.47f);
					float n = (Hash(bx, row, 33) - 0.5f) * 0.1f + (Hash(x, y, 34) - 0.5f) * 0.05f;
					if (y % 8 == 0 || (x + (row % 2) * 8) % 16 == 0)
					{
						return new Color(0.30f, 0.29f, 0.27f);
					}
					return Mix(c, n);
				});
			}
		}

		/// <summary>Touge hairpin paint: red and white bands across the road. v runs along the road, 16 px = 2 bands.</summary>
		internal static Texture2D Stripes
		{
			get
			{
				return Get("stripes", 16, (x, y) =>
				{
					float n = (Hash(x, y, 35) - 0.5f) * 0.05f;
					return y < 8 ? Mix(new Color(0.78f, 0.13f, 0.12f), n) : Mix(new Color(0.92f, 0.92f, 0.90f), n);
				});
			}
		}

		/// <summary>Plain red paint (the outer edge of a hairpin).</summary>
		internal static Texture2D RedPaint
		{
			get
			{
				return Get("redpaint", 32, (x, y) =>
				{
					return Mix(new Color(0.62f, 0.12f, 0.11f), (Hash(x, y, 36) - 0.5f) * 0.06f);
				});
			}
		}

		/// <summary>The gravel of a temple path: pale grey-beige, finely speckled, raked in lines across the texture.</summary>
		internal static Texture2D TempleGravel
		{
			get
			{
				return Get("templegravel", 128, (x, y) =>
				{
					Color c = new Color(0.72f, 0.69f, 0.62f);
					float n = (Hash(x, y, 40) - 0.5f) * 0.12f + (VNoise(x / 9f, y / 9f, 14, 41) - 0.5f) * 0.06f;
					if (y % 10 < 2)
					{
						n -= 0.07f;       // a rake line
					}
					if (Hash(x / 2, y / 2, 42) > 0.93f)
					{
						n += 0.12f;
					}
					return Mix(c, n);
				});
			}
		}

		/// <summary>The cave floor: dark, damp gravel and silt with pale pebbles and wet, darker patches.</summary>
		internal static Texture2D WetFloor
		{
			get
			{
				return Get("wetfloor", 128, (x, y) =>
				{
					Color c = new Color(0.30f, 0.30f, 0.31f);
					float n = (Hash(x, y, 43) - 0.5f) * 0.09f + (VNoise(x / 11f, y / 11f, 12, 44) - 0.5f) * 0.12f;
					float wet = VNoise(x / 22f, y / 22f, 6, 45);
					if (wet > 0.55f)
					{
						n -= 0.07f;
					}
					if (Hash(x / 2, y / 2, 46) > 0.955f)
					{
						n += 0.17f;
					}
					return Tinted(c, n, n * 1.05f, n * 1.2f);
				});
			}
		}

		/// <summary>Neutral leaf mass: tinted per tree (pink, red, yellow, green).</summary>
		internal static Texture2D Leaves
		{
			get
			{
				return Get("leaves", 64, (x, y) =>
				{
					float n = 0.78f + (Hash(x, y, 47) - 0.5f) * 0.28f + (VNoise(x / 6f, y / 6f, 10, 48) - 0.5f) * 0.22f;
					if (Hash(x, y, 49) > 0.9f)
					{
						n += 0.15f;
					}
					return new Color(Mathf.Clamp01(n), Mathf.Clamp01(n), Mathf.Clamp01(n), 1f);
				});
			}
		}

		internal static Texture2D Bark
		{
			get
			{
				return Get("bark", 64, (x, y) =>
				{
					Color c = new Color(0.30f, 0.20f, 0.14f);
					float grain = (VNoise(x / 2f, y / 12f, 32, 50) - 0.5f) * 0.14f;
					if (x % 8 == 0)
					{
						grain -= 0.06f;
					}
					return Mix(c, grain);
				});
			}
		}

		internal static Texture2D Bamboo
		{
			get
			{
				return Get("bamboo", 32, (x, y) =>
				{
					Color c = new Color(0.46f, 0.66f, 0.30f);
					float n = (Hash(x, y, 51) - 0.5f) * 0.05f;
					if (y % 16 < 2)
					{
						return Mix(new Color(0.26f, 0.40f, 0.16f), n);
					}
					if (x % 8 < 2)
					{
						n += 0.08f;
					}
					return Mix(c, n);
				});
			}
		}
	}
}
