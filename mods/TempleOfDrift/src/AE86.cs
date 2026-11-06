using System.Collections.Generic;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>
	/// A white and black three-door hatchback in the shape of the 1980s Toyota AE86 Sprinter Trueno (the tofu delivery car of the manga and
	/// anime this map is a tribute to): a long low bonnet over closed pop-up headlights, a short cabin with a raked windscreen, a steep
	/// hatch and a spoiler lip, a black lower body and bumpers, a white upper body. It is lofted: the body is a smooth skin over cross
	/// sections every 10 cm (heights and widths run through splines), so it reads as one curved car and not as boxes. Original geometry,
	/// no manufacturer badges. Measurements are in metres and are scaled to the game's car (about 14 units long).
	/// </summary>
	internal static partial class Landmarks
	{
		// x from the nose, half width, floor height, body top height (hood, cowl, beltline, deck)
		private static readonly float[][] Body =
		{
			new[] { 0.00f, 0.60f, 0.34f, 0.50f },
			new[] { 0.07f, 0.70f, 0.26f, 0.57f },
			new[] { 0.28f, 0.77f, 0.20f, 0.64f },
			new[] { 0.80f, 0.80f, 0.19f, 0.78f },
			new[] { 1.30f, 0.81f, 0.19f, 0.89f },
			new[] { 1.90f, 0.815f, 0.19f, 0.935f },
			new[] { 2.80f, 0.815f, 0.19f, 0.935f },
			new[] { 3.50f, 0.805f, 0.20f, 0.94f },
			new[] { 3.95f, 0.79f, 0.22f, 0.90f },
			new[] { 4.12f, 0.74f, 0.28f, 0.80f },
			new[] { 4.20f, 0.64f, 0.38f, 0.62f }
		};

		private const float CarLength = 4.2f;

		/// <summary>Catmull-Rom through the key rows, channel 1 = half width, 2 = floor, 3 = top.</summary>
		private static float Spline(float x, int channel)
		{
			int n = Body.Length;
			x = Mathf.Clamp(x, 0f, CarLength);
			int i = 0;
			while (i < n - 2 && x > Body[i + 1][0])
			{
				i++;
			}
			float x0 = Body[i][0], x1 = Body[i + 1][0];
			float t = (x - x0) / Mathf.Max(1e-5f, x1 - x0);
			float p1 = Body[i][channel], p2 = Body[i + 1][channel];
			float p0 = i > 0 ? Body[i - 1][channel] : p1 - (p2 - p1), p3 = i + 2 < n ? Body[i + 2][channel] : p2 + (p2 - p1);
			float t2 = t * t, t3 = t2 * t;
			return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
		}

		private static float SmoothStep(float t)
		{
			t = Mathf.Clamp01(t);
			return t * t * (3f - 2f * t);
		}

		private static Batch Shiny(string name, Color c, bool collide, float smooth, float glow = 0f)
		{
			string key = name + (collide ? "#c" : "#n") + "#s";
			Batch b;
			if (!pool.TryGetValue(key, out b))
			{
				b = new Batch(name, Tex.White, c, collide) { CastShadows = false, Smooth = smooth };
				if (glow > 0f)
				{
					b.Emission = c * glow;
				}
				pool[key] = b;
				into.Add(b);
			}
			return b;
		}

		/// <summary>
		/// The car, nose pointing along <paramref name="forward"/>, standing on the ground at <paramref name="at"/>. <paramref name="scale"/> 1
		/// is a regular car of the race.
		/// </summary>
		internal static void Ae86(Vector3 at, Vector3 forward, float scale)
		{
			float K = scale * 14.2f / CarLength;
			Quaternion rot = Quaternion.LookRotation(forward, Vector3.up);
			System.Func<float, float, float, Vector3> P = (lat, y, xm) => at + rot * new Vector3(lat * K, y * K, (CarLength * 0.5f - xm) * K);

			Batch white = Shiny("ae86 white", new Color(0.93f, 0.94f, 0.95f), true, 0.62f);
			Batch black = Shiny("ae86 black", new Color(0.035f, 0.035f, 0.04f), true, 0.5f);
			Batch glass = Shiny("ae86 glass", new Color(0.05f, 0.08f, 0.11f), false, 0.92f);
			Batch trim = Shiny("ae86 trim", new Color(0.55f, 0.56f, 0.6f), false, 0.8f);
			Batch red = Shiny("ae86 tail lamp", new Color(0.85f, 0.04f, 0.03f), false, 0.8f, 1.3f);
			Batch lamp = Shiny("ae86 head lamp", new Color(1f, 0.92f, 0.7f), false, 0.8f, 1.0f);

			// ---- the body: rings of 13 points round each cross section, every 10 cm along the car ----
			int stations = Mathf.RoundToInt(CarLength / 0.1f);
			Vector3[][] ring = new Vector3[stations + 1][];
			float[] xs = new float[stations + 1];
			for (int i = 0; i <= stations; i++)
			{
				float x = CarLength * i / stations;
				xs[i] = x;
				float w = Spline(x, 1), yb = Spline(x, 2), top = Spline(x, 3);
				float split = yb + 0.30f, shoulder = top - 0.10f;
				float[][] half =
				{
					new[] { 0f, yb }, new[] { 0.93f * w, yb }, new[] { w, yb + 0.10f }, new[] { w, split }, new[] { w, shoulder }, new[] { 0.84f * w, top - 0.012f }, new[] { 0f, top + 0.012f }
				};
				Vector3[] r = new Vector3[13];
				for (int j = 0; j < 7; j++)
				{
					r[6 + j] = P(half[j][0], half[j][1], x);          // centre line, then the right side upward...
					r[6 - j] = P(-half[j][0], half[j][1], x);
				}
				ring[i] = r;
			}
			for (int i = 0; i < stations; i++)
			{
				float xm = (xs[i] + xs[i + 1]) * 0.5f;
				bool hood = xm < 1.32f;
				bool bumper = xm < 0.34f || xm > 3.95f;
				Vector3 centre = P(0f, 0.5f, xm);
				for (int j = 0; j < 12; j++)
				{
					int seg = j < 6 ? 5 - j : j - 6;   // 0 = underside next to the centre line ... 5 = the top next to the centre line
					Batch b;
					if (seg <= 2)
					{
						b = black;                      // the underside and the lower body
					}
					else if (seg == 3)
					{
						b = bumper ? black : white;
					}
					else
					{
						b = hood ? black : (bumper ? black : white);
					}
					if (seg == 3 && !bumper)
					{
						b = white;
					}
					Vector3 a = ring[i][j], bb = ring[i][j + 1], c = ring[i + 1][j + 1], d = ring[i + 1][j];
					Vector3 mid = (a + bb + c + d) * 0.25f;
					Batch target = b == black ? black : white;
					target.Quad(a, bb, c, d, mid - centre - Vector3.up * 0.0f, 20f);
				}
			}
			// the ends: the nose and the tail
			for (int end = 0; end < 2; end++)
			{
				int i = end == 0 ? 0 : stations;
				Vector3 outward = end == 0 ? rot * Vector3.forward : -(rot * Vector3.forward);
				Vector3 cc = P(0f, 0.45f, xs[i]);
				for (int j = 0; j < 12; j++)
				{
					black.Tri(ring[i][j], ring[i][j + 1], cc, outward, 20f);
				}
			}

			// ---- the cabin: a smaller loft on top of the body; glass all round, white roof ----
			float xA = 1.34f, xB = 3.95f, xRoof0 = 2.05f, xRoof1 = 2.80f, roofY = 1.315f;
			int cs = Mathf.RoundToInt((xB - xA) / 0.1f);
			Vector3[][] cab = new Vector3[cs + 1][];
			float[] cx = new float[cs + 1];
			for (int i = 0; i <= cs; i++)
			{
				float x = xA + (xB - xA) * i / cs;
				cx[i] = x;
				float y0 = Spline(x, 3) + 0.012f, wb = Spline(x, 1) - 0.075f;
				float ry;
				if (x < xRoof0)
				{
					ry = Mathf.Lerp(y0 + 0.005f, roofY, SmoothStep((x - xA) / (xRoof0 - xA)));
				}
				else if (x <= xRoof1)
				{
					ry = roofY + 0.012f * Mathf.Sin(Mathf.PI * (x - xRoof0) / (xRoof1 - xRoof0));
				}
				else
				{
					ry = Mathf.Lerp(roofY, y0 + 0.005f, SmoothStep((x - xRoof1) / (xB - xRoof1)));
				}
				float wr = Mathf.Lerp(wb, wb * 0.80f, Mathf.Clamp01((ry - y0) / 0.15f));
				float[][] half =
				{
					new[] { wb, y0 }, new[] { Mathf.Lerp(wb, wr, 0.45f), Mathf.Lerp(y0, ry, 0.45f) }, new[] { wr, ry - 0.03f }, new[] { wr * 0.55f, ry - 0.004f }, new[] { 0f, ry + 0.006f }
				};
				Vector3[] r = new Vector3[9];
				for (int j = 0; j < 5; j++)
				{
					r[4 + j] = P(half[j][0], half[j][1], x);
					r[4 - j] = P(-half[j][0], half[j][1], x);
				}
				cab[i] = r;
			}
			for (int i = 0; i < cs; i++)
			{
				float xm = (cx[i] + cx[i + 1]) * 0.5f;
				bool roof = xm >= xRoof0 && xm <= xRoof1;
				Vector3 centre = P(0f, 0.95f, xm);
				for (int j = 0; j < 8; j++)
				{
					int seg = j < 4 ? 3 - j : j - 4;   // 0 = the lower side window ... 3 = next to the centre line on top
					Vector3 a = cab[i][j], bb = cab[i][j + 1], c = cab[i + 1][j + 1], d = cab[i + 1][j];
					Vector3 mid = (a + bb + c + d) * 0.25f;
					Batch target = (seg >= 2 && roof) ? white : glass;
					target.Quad(a, bb, c, d, mid - centre, 20f);
				}
			}
			// the glass is closed at both ends by the body; pillars and the spoiler lip
			foreach (float side in new[] { -1f, 1f })
			{
				float wA = Spline(xA, 1) - 0.075f, wRoof = (Spline(xRoof0, 1) - 0.075f) * 0.80f;
				white.Cone(P(side * wA, Spline(xA, 3) + 0.01f, xA), 0.03f * K, P(side * wRoof, roofY - 0.03f, xRoof0), 0.03f * K, 5, 8f);                       // A pillar
				float wC = (Spline(xRoof1, 1) - 0.075f) * 0.80f, wD = Spline(xB, 1) - 0.075f;
				white.Cone(P(side * wC, roofY - 0.03f, xRoof1), 0.06f * K, P(side * wD, Spline(xB, 3) + 0.01f, xB), 0.05f * K, 5, 8f);                            // C pillar
			}
			Vector3 lip = P(0f, 0.985f, 3.93f);
			white.Box(lip, new Vector3(1.28f * K, 0.025f * K, 0.16f * K), rot * Quaternion.Euler(-10f, 0f, 0f), 8f);

			// ---- wheels: tyre, rim, and a dark arch behind them on the body ----
			foreach (float xw in new[] { 0.72f, 3.42f })
			{
				foreach (float side in new[] { -1f, 1f })
				{
					Vector3 inner = P(side * 0.58f, 0.31f, xw), outer = P(side * 0.835f, 0.31f, xw);
					black.Cone(inner, 0.305f * K, outer, 0.305f * K, 16, 8f, true);
					trim.Cone(P(side * 0.832f, 0.31f, xw), 0.19f * K, P(side * 0.842f, 0.31f, xw), 0.19f * K, 12, 8f, true);
					black.Cone(P(side * 0.795f, 0.31f, xw), 0.375f * K, P(side * 0.803f, 0.31f, xw), 0.375f * K, 16, 8f, true);
				}
			}

			// ---- lights ----
			foreach (float side in new[] { -1f, 1f })
			{
				// the pop-up headlights, closed: two dark panels on the nose, and the small lamps low in the bumper
				black.Box(P(side * 0.47f, Spline(0.48f, 3) + 0.012f, 0.48f), new Vector3(0.34f * K, 0.03f * K, 0.26f * K), rot * Quaternion.Euler(14f, 0f, 0f), 8f);
				lamp.Cone(P(side * 0.55f, 0.43f, 0.015f), 0.07f * K, P(side * 0.55f, 0.43f, -0.02f), 0.07f * K, 8, 8f, true);
				// the tail lamps: a red band each side of the black garnish
				red.Box(P(side * 0.45f, 0.70f, 4.15f), new Vector3(0.52f * K, 0.11f * K, 0.03f * K), rot, 8f);
			}
			black.Box(P(0f, 0.70f, 4.15f), new Vector3(0.42f * K, 0.13f * K, 0.035f * K), rot, 8f);
			trim.Box(P(0f, 0.40f, -0.005f), new Vector3(0.36f * K, 0.1f * K, 0.02f * K), rot, 8f);

			// ---- the shop name on both doors (white upper body, black lettering) ----
			foreach (float side in new[] { -1f, 1f })
			{
				labels.Add(new Label
				{
					Pos = P(side * 0.828f, 0.66f, 2.18f),
					Facing = rot * new Vector3(side, 0f, 0f),
					Text = "KAZE TOFU",
					Color = new Color(0.04f, 0.04f, 0.05f),
					Size = new Vector2(1.05f * K, 0.16f * K)
				});
			}
		}
	}
}
