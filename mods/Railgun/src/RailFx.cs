using System.Collections.Generic;
using UnityEngine;

namespace HalfGripRailgun
{
	/// <summary>Where things are on the gun's pictures. These must match the numbers printed by <c>art\make_railgun_sprites.py</c>.</summary>
	internal static class RailLayout
	{
		public const int Width = 258;

		public const int Height = 48;

		public const int CoilWidth = 9;

		public const int Coils = 6;

		/// <summary>Left edge of each ignition coil; the spark plug's tip is four pixels in, on the top row.</summary>
		public static readonly int[] CoilX = { 24, 44, 64, 84, 104, 124 };

		/// <summary>The pressure gauge's centre in the pump's picture, in pixels from its top left.</summary>
		public static readonly Vector2 Gauge = new Vector2(10.5f, 4.5f);

		public const int PumpHeight = 15;

		/// <summary>The window on the receiver that shows the shots loaded: the top left pixel of its 3x5 digit.</summary>
		public const int DigitX = 152;

		public const int DigitRow = 13;

	}

	/// <summary>Pixel work on the gun's pictures: laying the lit coils over the body and drawing the arcs between the spark plugs.</summary>
	internal static class RailPixels
	{
		private static readonly Color32 Core = new Color32(226, 248, 255, 255);

		private static readonly Color32 Glow = new Color32(110, 214, 255, 200);

		/// <summary>The six lit-coil layers, or null if a file is missing or the wrong size.</summary>
		public static Color32[][] LoadCoils()
		{
			var coils = new Color32[RailLayout.Coils][];
			for (int i = 0; i < coils.Length; i++)
			{
				Texture2D texture = Mod.Ctx.Sprites.LoadTexture("railgun_coil_" + i + ".png");
				if (texture == null || texture.width != RailLayout.Width || texture.height != RailLayout.Height)
				{
					Mod.Log.LogWarning("railgun_coil_" + i + ".png is missing or the wrong size: the coils will not light");
					return null;
				}
				coils[i] = texture.GetPixels32();
			}
			return coils;
		}

		/// <summary>Lays one picture over another (the picture on top keeps its own transparency).</summary>
		public static void Over(Color32[] under, Color32[] over)
		{
			for (int i = 0; i < under.Length; i++)
			{
				Color32 s = over[i];
				if (s.a == 0)
				{
					continue;
				}
				Color32 d = under[i];
				float a = s.a / 255f;
				under[i] = new Color32(
					(byte)(s.r * a + d.r * (1f - a)),
					(byte)(s.g * a + d.g * (1f - a)),
					(byte)(s.b * a + d.b * (1f - a)),
					(byte)Mathf.Max(d.a, s.a));
			}
		}

		/// <summary>The first <paramref name="lit"/> coils, lit, on a clear picture.</summary>
		public static Color32[] Lit(Color32[][] coils, int lit)
		{
			var pixels = new Color32[RailLayout.Width * RailLayout.Height];
			for (int i = 0; i < lit && i < coils.Length; i++)
			{
				Over(pixels, coils[i]);
			}
			return pixels;
		}

		/// <summary>
		/// Jagged arcs from one lit spark plug to the next. A variant is a fixed shape (so they can be drawn once and swapped between
		/// to flicker); the arcs stay out of the coils' own bodies.
		/// </summary>
		public static void Arcs(Color32[] pixels, int lit, int variant)
		{
			if (variant <= 0)
			{
				return;
			}
			for (int g = 0; g + 1 < lit && g + 1 < RailLayout.Coils; g++)
			{
				var random = new System.Random(variant * 977 + g * 31);
				int xa = RailLayout.CoilX[g] + 4;
				int xb = RailLayout.CoilX[g + 1] + 4;
				int previous = 0;
				for (int x = xa; x <= xb; x++)
				{
					float t = (x - xa) / (float)(xb - xa);
					float envelope = Mathf.Sin(t * Mathf.PI);
					int y = Mathf.Clamp(Mathf.RoundToInt(envelope * 4f + (float)(random.NextDouble() - 0.5) * 3f * envelope), 0, 6);
					if (InCoil(x, g) || InCoil(x, g + 1))
					{
						y = Mathf.Min(y, 2);
					}
					int from = Mathf.Min(previous, y);
					int to = Mathf.Max(previous, y);
					for (int row = from; row <= to; row++)
					{
						Set(pixels, x, row, Core);
					}
					if (!InCoil(x, g) && !InCoil(x, g + 1))
					{
						Set(pixels, x, to + 1, Glow, true);
					}
					previous = y;
				}
			}
		}

		/// <summary>
		/// How full a capacitor looks for how far through its charging it is (0..1): it fills slowly for the first nine tenths of the time
		/// (up to a little over half), then the last tenth runs in a rush.
		/// </summary>
		public static float Ease(float progress)
		{
			const float knee = 0.9f;
			const float reached = 0.55f;
			progress = Mathf.Clamp01(progress);
			return progress < knee ? reached * Mathf.Pow(progress / knee, 1.2f) : Mathf.Lerp(reached, 1f, (progress - knee) / (1f - knee));
		}

		/// <summary>A whole number for how the coils look at this charge (capacitors 0..6), to tell when the picture needs drawing again.</summary>
		public static int Step(float capacitors)
		{
			int full = Mathf.Min(RailLayout.Coils, (int)(capacitors + 0.0001f));
			return full * 19 + (full < RailLayout.Coils ? Mathf.RoundToInt(Ease(capacitors - full) * 18f) : 0);
		}

		/// <summary>
		/// The lit coils for a charge of <paramref name="capacitors"/> (0..6): every full one lit, the one filling lit from its bottom
		/// up as far as it has got, and arcs between the full ones.
		/// </summary>
		public static Color32[] Fill(Color32[][] coils, float capacitors, int variant)
		{
			int full = Mathf.Min(RailLayout.Coils, (int)(capacitors + 0.0001f));
			Color32[] pixels = Lit(coils, full);
			if (full < RailLayout.Coils)
			{
				float fill = Ease(capacitors - full);
				if (fill > 0.01f)
				{
					var part = new Color32[pixels.Length];
					int x0 = RailLayout.CoilX[full];
					for (int row = 3; row <= 8; row++)
					{
						// the body is six rows; the bottom row (8) is the first to light
						float amount = Mathf.Clamp01(fill * 6f - (8 - row));
						if (amount <= 0f)
						{
							continue;
						}
						for (int x = x0 - 1; x <= x0 + RailLayout.CoilWidth; x++)
						{
							int index = (RailLayout.Height - 1 - row) * RailLayout.Width + x;
							Color32 c = coils[full][index];
							if (c.a > 0)
							{
								c.a = (byte)(c.a * amount);
								part[index] = c;
							}
						}
					}
					Over(pixels, part);
				}
			}
			Arcs(pixels, full, variant);
			return pixels;
		}

		// A 3x5 pixel font for the shots-loaded digit.
		private static readonly string[][] Font =
		{
			new[] { "111", "101", "101", "101", "111" },
			new[] { "010", "110", "010", "010", "111" },
			new[] { "111", "001", "111", "100", "111" },
			new[] { "111", "001", "111", "001", "111" },
			new[] { "101", "101", "111", "001", "001" },
			new[] { "111", "100", "111", "001", "111" },
			new[] { "111", "100", "111", "101", "111" },
			new[] { "111", "001", "010", "010", "010" },
			new[] { "111", "101", "111", "101", "111" },
			new[] { "111", "101", "111", "001", "111" }
		};

		private static readonly Color32[][] digitPixels = new Color32[10][];

		/// <summary>The number of shots loaded as a glowing digit for the receiver's window, on a clear picture the size of the body.</summary>
		public static Color32[] Digit(int shots)
		{
			int n = Mathf.Clamp(shots, 0, 9);
			if (digitPixels[n] == null)
			{
				var pixels = new Color32[RailLayout.Width * RailLayout.Height];
				// Empty is a dull red zero, anything loaded is a bright amber digit.
				Color32 colour = n == 0 ? new Color32(190, 56, 44, 255) : new Color32(255, 190, 70, 255);
				for (int y = 0; y < 5; y++)
				{
					for (int x = 0; x < 3; x++)
					{
						if (Font[n][y][x] == '1')
						{
							Set(pixels, RailLayout.DigitX + x, RailLayout.DigitRow + y, colour);
						}
					}
				}
				digitPixels[n] = pixels;
			}
			return digitPixels[n];
		}

		private static readonly Color32[][] lampPixels = new Color32[2][];

		/// <summary>The ammo lamp, green or red, as a layer to lay over the body (the files <c>railgun_lamp_green.png</c> and <c>railgun_lamp_red.png</c>).</summary>
		public static Color32[] Lamp(bool loaded)
		{
			int i = loaded ? 1 : 0;
			if (lampPixels[i] == null)
			{
				Texture2D texture = Mod.Ctx.Sprites.LoadTexture(loaded ? "railgun_lamp_green.png" : "railgun_lamp_red.png");
				lampPixels[i] = texture != null && texture.width == RailLayout.Width && texture.height == RailLayout.Height ? texture.GetPixels32() : new Color32[RailLayout.Width * RailLayout.Height];
			}
			return lampPixels[i];
		}

		private static bool InCoil(int x, int coil)
		{
			return x >= RailLayout.CoilX[coil] && x < RailLayout.CoilX[coil] + RailLayout.CoilWidth;
		}

		// Rows are counted from the top of the picture, the pixel array from the bottom.
		private static void Set(Color32[] pixels, int x, int row, Color32 colour, bool onlyIfEmpty = false)
		{
			if (x < 0 || x >= RailLayout.Width || row < 0 || row >= RailLayout.Height)
			{
				return;
			}
			int index = (RailLayout.Height - 1 - row) * RailLayout.Width + x;
			if (!onlyIfEmpty || pixels[index].a == 0)
			{
				pixels[index] = colour;
			}
		}

		public static Texture2D Texture(Color32[] pixels, string name)
		{
			var texture = new Texture2D(RailLayout.Width, RailLayout.Height, TextureFormat.RGBA32, false)
			{
				name = name,
				filterMode = FilterMode.Point,
				hideFlags = HideFlags.HideAndDontSave
			};
			texture.SetPixels32(pixels);
			texture.Apply();
			return texture;
		}
	}

	/// <summary>The gun's sounds, made in code: the shot's crack and zap, the arcs' crackle and the pump's hiss.</summary>
	internal static class RailAudio
	{
		private const int Rate = 44100;

		private static AudioClip zap;

		private static AudioClip tick;

		private static AudioClip hiss;

		public static AudioClip Zap => zap != null ? zap : zap = MakeZap();

		public static AudioClip Tick => tick != null ? tick : tick = MakeTick();

		public static AudioClip Hiss => hiss != null ? hiss : hiss = MakeHiss();

		private static AudioClip Make(string name, float[] data)
		{
			var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
			clip.SetData(data, 0);
			clip.hideFlags = HideFlags.HideAndDontSave;
			return clip;
		}

		// A sharp noise crack, a falling electric zap and a low thump under both.
		private static AudioClip MakeZap()
		{
			var random = new System.Random(77);
			var data = new float[(int)(Rate * 0.5f)];
			float phase = 0f;
			for (int i = 0; i < data.Length; i++)
			{
				float t = i / (float)Rate;
				float noise = (float)(random.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 55f);
				float frequency = Mathf.Lerp(2400f, 160f, Mathf.Clamp01(t / 0.3f));
				phase += 6.2831853f * frequency / Rate;
				float tone = Mathf.Sin(phase) * Mathf.Exp(-t * 8f);
				float buzz = Mathf.Sign(Mathf.Sin(phase * 0.5f)) * 0.25f * Mathf.Exp(-t * 14f);
				float thump = Mathf.Sin(6.2831853f * 58f * t) * Mathf.Exp(-t * 12f);
				data[i] = Mathf.Clamp((noise * 0.8f + tone * 0.45f + buzz + thump * 0.7f) * 0.7f, -1f, 1f);
			}
			return Make("railgun zap", data);
		}

		private static AudioClip MakeTick()
		{
			var random = new System.Random(33);
			var data = new float[(int)(Rate * 0.05f)];
			float last = 0f;
			for (int i = 0; i < data.Length; i++)
			{
				float t = i / (float)Rate;
				float noise = (float)(random.NextDouble() * 2.0 - 1.0);
				float high = noise - last * 0.6f;
				last = noise;
				data[i] = high * Mathf.Exp(-t * 110f) * 0.6f;
			}
			return Make("railgun crackle", data);
		}

		// Air let out of a pump: noise with the lows taken off, swelling and fading over a third of a second.
		private static AudioClip MakeHiss()
		{
			var random = new System.Random(55);
			var data = new float[(int)(Rate * 0.32f)];
			float last = 0f;
			for (int i = 0; i < data.Length; i++)
			{
				float t = i / (float)Rate;
				float noise = (float)(random.NextDouble() * 2.0 - 1.0);
				float high = noise - last * 0.85f;
				last = noise;
				float envelope = Mathf.Sin(Mathf.Clamp01(t / 0.32f) * Mathf.PI);
				data[i] = high * envelope * 0.5f;
			}
			return Make("railgun hiss", data);
		}
	}

	/// <summary>
	/// What the gun shows in the cockpit while it hangs on the roof lining: the coils lit in steps (the one filling from the bottom up),
	/// arcs flickering between the spark plugs, the ammo lamp, and the pressure gauge's needle on the pump. All of it is drawn into the
	/// body's and the pump's own pictures: each sprite gets a live texture that is redrawn when something changes. (Extra sprites laid
	/// over the gun as separate objects were hidden under it, whatever sorting they were given.) The game hides the gun's sprites while
	/// it is raised; the held model shows the same things then.
	/// </summary>
	internal class CockpitFx
	{
		private readonly SpriteRenderer body;

		private readonly SpriteRenderer pump;

		private readonly Color32[] bodyPlain;

		private readonly Color32[] pumpPlain;

		private readonly Color32[][] coils;

		private readonly Texture2D bodyLive;

		private readonly Texture2D pumpLive;

		private int bodyKey = -1;

		private int pumpKey = int.MinValue;

		public CockpitFx(SpriteRenderer bodyRenderer, SpriteRenderer pumpRenderer)
		{
			coils = RailPixels.LoadCoils();
			Texture2D bodyArt = Mod.Ctx.Sprites.LoadTexture("railgun_body.png");
			if (bodyRenderer != null && bodyRenderer.sprite != null && bodyArt != null && bodyArt.width == RailLayout.Width && bodyArt.height == RailLayout.Height)
			{
				body = bodyRenderer;
				bodyPlain = bodyArt.GetPixels32();
				bodyLive = Live(bodyPlain, RailLayout.Width, RailLayout.Height, "railgun body live");
				body.sprite = SpriteLike(body.sprite, bodyLive);
			}
			Texture2D pumpArt = Mod.Ctx.Sprites.LoadTexture("railgun_pump.png");
			if (pumpRenderer != null && pumpRenderer.sprite != null && pumpArt != null && pumpArt.width == 33 && pumpArt.height == RailLayout.PumpHeight)
			{
				pump = pumpRenderer;
				pumpPlain = pumpArt.GetPixels32();
				pumpLive = Live(pumpPlain, 33, RailLayout.PumpHeight, "railgun pump live");
				pump.sprite = SpriteLike(pump.sprite, pumpLive);
			}
			Mod.Log.LogInfo($"Railgun cockpit art drawn live: body {(body != null ? "yes" : "NO")}, pump {(pump != null ? "yes" : "NO")}, coils {(coils != null ? "yes" : "NO")}");
		}

		private static Texture2D Live(Color32[] pixels, int width, int height, string name)
		{
			var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
			texture.SetPixels32(pixels);
			texture.Apply();
			return texture;
		}

		// A sprite of the live texture that sits and scales exactly like the one it replaces.
		private static Sprite SpriteLike(Sprite like, Texture2D texture)
		{
			// Moving the pivot down by some pixels draws the picture that many pixels higher: the gun is lifted on the roof lining so its
			// battery and cable clear the mirror. The body and the pump get the same lift, so they still line up.
			Vector2 size = like.rect.size;
			return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(like.pivot.x / size.x, (like.pivot.y - Mod.Lift.Value) / size.y), like.pixelsPerUnit);
		}

		/// <summary>Draws the gun for this charge (0..6 capacitors), the arcs' shape (0 = none), the gauge needle's kick and the ammo lamp.</summary>
		public void Show(float capacitors, int variant, float needleKick, bool loaded, int shots)
		{
			int full = Mathf.Min(RailLayout.Coils, (int)(capacitors + 0.0001f));
			if (body != null)
			{
				int key = ((RailPixels.Step(capacitors) * 4 + (full >= 2 ? variant : 0)) * 2 + (loaded ? 1 : 0)) * 10 + Mathf.Clamp(shots, 0, 9);
				if (key != bodyKey)
				{
					bodyKey = key;
					Color32[] pixels = (Color32[])bodyPlain.Clone();
					if (coils != null)
					{
						RailPixels.Over(pixels, RailPixels.Fill(coils, capacitors, variant));
					}
					RailPixels.Over(pixels, RailPixels.Lamp(loaded));
					RailPixels.Over(pixels, RailPixels.Digit(shots));
					bodyLive.SetPixels32(pixels);
					bodyLive.Apply();
				}
			}
			if (pump != null)
			{
				// Swings from the left to the right across the top of the dial with the charge; a pump stroke kicks it.
				float angle = Mathf.Lerp(60f, -60f, Mathf.Clamp01(capacitors / RailLayout.Coils)) + needleKick;
				int step = Mathf.RoundToInt(angle / 6f);
				if (step != pumpKey)
				{
					pumpKey = step;
					Color32[] pixels = (Color32[])pumpPlain.Clone();
					Needle(pixels, step * 6f);
					pumpLive.SetPixels32(pixels);
					pumpLive.Apply();
				}
			}
		}

		// Three pixels from the gauge's centre, turned by the angle (0 = straight up, positive = to the left).
		private static void Needle(Color32[] pixels, float degrees)
		{
			float a = degrees * Mathf.Deg2Rad;
			float dx = -Mathf.Sin(a);
			float dy = -Mathf.Cos(a);
			Color32[] colours = { new Color32(150, 30, 28, 255), new Color32(214, 48, 40, 255), new Color32(255, 150, 120, 255) };
			for (int i = 0; i < 3; i++)
			{
				float t = 1.5f + i;
				int x = Mathf.FloorToInt(RailLayout.Gauge.x + dx * t);
				int row = Mathf.FloorToInt(RailLayout.Gauge.y + dy * t);
				if (x >= 0 && x < 33 && row >= 0 && row < RailLayout.PumpHeight)
				{
					pixels[(RailLayout.PumpHeight - 1 - row) * 33 + x] = colours[i];
				}
			}
		}

		public void Destroy()
		{
			if (bodyLive != null)
			{
				Object.Destroy(bodyLive);
			}
			if (pumpLive != null)
			{
				Object.Destroy(pumpLive);
			}
		}
	}

	/// <summary>A burst of sparks at the muzzle of the held gun when it fires: little bright squares thrown forward that fall and die.</summary>
	internal class RailSparks : MonoBehaviour
	{
		private class Spark
		{
			public Transform Body;

			public Vector3 Velocity;

			public float Life;
		}

		private const int Count = 14;

		private readonly List<Spark> sparks = new List<Spark>();

		private Material white;

		private Material blue;

		private Camera eye;

		private float size = 0.05f;

		public static RailSparks On(GameObject host)
		{
			RailSparks existing = host.GetComponent<RailSparks>();
			return existing != null ? existing : host.AddComponent<RailSparks>();
		}

		private void Make(int layer)
		{
			Shader shader = Shader.Find("Sprites/Default");
			if (shader == null)
			{
				return;
			}
			white = new Material(shader) { color = new Color(0.95f, 0.98f, 1f, 1f), hideFlags = HideFlags.HideAndDontSave };
			blue = new Material(shader) { color = new Color(0.45f, 0.75f, 1f, 1f), hideFlags = HideFlags.HideAndDontSave };
			for (int i = 0; i < Count; i++)
			{
				GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
				Destroy(quad.GetComponent<Collider>());
				quad.name = "railgun spark";
				quad.layer = layer;
				quad.GetComponent<MeshRenderer>().sharedMaterial = i % 3 == 0 ? white : blue;
				quad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				quad.SetActive(false);
				sparks.Add(new Spark { Body = quad.transform });
			}
		}

		/// <summary>Throws sparks from the muzzle along the aim; <paramref name="length"/> is the gun's length in world units.</summary>
		public void Burst(Vector3 from, Vector3 forward, float length, int layer, float charge)
		{
			if (sparks.Count == 0)
			{
				Make(layer);
			}
			size = Mathf.Max(0.002f, length * 0.025f);
			int amount = Mathf.RoundToInt(Mathf.Lerp(5f, Count, charge));
			for (int i = 0; i < sparks.Count && amount > 0; i++)
			{
				Spark spark = sparks[i];
				if (spark.Life > 0f)
				{
					continue;
				}
				amount--;
				Vector3 jitter = Random.insideUnitSphere * 0.7f;
				spark.Velocity = (forward + jitter).normalized * length * Random.Range(0.8f, 2.4f);
				spark.Life = Random.Range(0.12f, 0.3f);
				spark.Body.position = from;
				spark.Body.localScale = Vector3.one * size * Random.Range(0.6f, 1.4f);
				spark.Body.gameObject.SetActive(true);
			}
		}

		private void Update()
		{
			if (eye == null)
			{
				GameObject found = GameObject.Find("InteriorCamera");
				eye = found != null ? found.GetComponent<Camera>() : null;
			}
			foreach (Spark spark in sparks)
			{
				if (spark.Life <= 0f)
				{
					continue;
				}
				spark.Life -= Time.deltaTime;
				if (spark.Life <= 0f)
				{
					spark.Body.gameObject.SetActive(false);
					continue;
				}
				spark.Velocity += Vector3.down * size * 30f * Time.deltaTime;
				spark.Body.position += spark.Velocity * Time.deltaTime;
				if (eye != null)
				{
					spark.Body.rotation = Quaternion.LookRotation(spark.Body.position - eye.transform.position);
				}
			}
		}

		private void OnDestroy()
		{
			foreach (Spark spark in sparks)
			{
				if (spark.Body != null)
				{
					Destroy(spark.Body.gameObject);
				}
			}
		}
	}
}
