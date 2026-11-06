using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>One point of the lap, 8 units apart, in race order. Point 0 is the start line, heading +X.</summary>
	internal sealed class Pt
	{
		public Vector3 P;
		public float W;
		public int Surface;
		public int Section;
		public bool Hairpin;
	}

	/// <summary>
	/// The map's data, written by <c>art\make_route.py</c> and <c>art\make_terrain.py</c> (so the layout and the rock over the cave were
	/// checked offline): the lap, the short cave branch, and the mountain's height grid. Heights are shifted so the start line is at y 0
	/// (the grid cars spawn at y 1).
	/// </summary>
	internal static class Data
	{
		internal const int Asphalt = 0, Dirt = 1, Cave = 2, Gravel = 3;
		internal const int SecStart = 0, SecTouge = 1, SecValley = 2, SecTemple = 3, SecShrine = 4, SecForest = 5, SecCave = 6, SecFork = 7, SecRidge = 8;

		internal static readonly List<Pt> Main = new List<Pt>();
		internal static readonly List<Pt> Branch = new List<Pt>();
		internal static readonly Dictionary<string, float> Meta = new Dictionary<string, float>();

		internal static float[] Heights;
		internal static int Nx, Nz;
		internal static float X0, Z0, Cell;
		internal static bool Loaded;

		internal static int Index(string key)
		{
			return (int)Meta[key];
		}

		internal static float Lap
		{
			get { return Meta["lap"]; }
		}

		internal static bool Load(string folder)
		{
			try
			{
				Main.Clear();
				Branch.Clear();
				Meta.Clear();
				string dir = Path.Combine(folder, "data");
				for (int i = 0; i < RouteData.MetaKeys.Length; i++)
				{
					Meta[RouteData.MetaKeys[i]] = RouteData.MetaValues[i];
				}
				float drop = -Meta["H"];
				Fill(Main, drop, RouteData.MainX, RouteData.MainY, RouteData.MainZ, RouteData.MainW, RouteData.MainSurface, RouteData.MainSection, RouteData.MainHairpin);
				Fill(Branch, drop, RouteData.BranchX, RouteData.BranchY, RouteData.BranchZ, RouteData.BranchW, RouteData.BranchSurface, RouteData.BranchSection, RouteData.BranchHairpin);
				BlendBranchHeight();

				using (BinaryReader r = new BinaryReader(File.OpenRead(Path.Combine(dir, "terrain.bin"))))
				{
					Nx = r.ReadInt32();
					Nz = r.ReadInt32();
					X0 = r.ReadSingle();
					Z0 = r.ReadSingle();
					Cell = r.ReadSingle();
					Heights = new float[Nx * Nz];
					for (int i = 0; i < Heights.Length; i++)
					{
						Heights[i] = r.ReadSingle() + drop;
					}
				}
				FlattenMouths();
				Loaded = Main.Count > 100 && Heights.Length > 100;
				Mod.Log.LogInfo("Temple of Drift data: " + Main.Count + " lap points (" + Lap.ToString("0") + " units), " + Branch.Count + " branch points, terrain " + Nx + "x" + Nz);
				return Loaded;
			}
			catch (Exception e)
			{
				Mod.Log.LogError("Could not read the map data: " + e.Message);
				return false;
			}
		}

		/// <summary>
		/// The short way leaves and rejoins the long road at the same points and heights, but climbs at its own grade, so a few units on the two
		/// heights differ while the two floors still overlap sideways (the long floor is 25 wide each side, the short one 14): two floors at
		/// different heights, a step between them, and cracks. Where the short road is within 40 units of the long one its height is blended
		/// to the long road's, easing back to its own grade by 40 units apart.
		/// </summary>
		private static void BlendBranchHeight()
		{
			int f = (int)Meta["fork"], j = (int)Meta["join"];
			float worst = 0f;
			for (int i = 1; i < Branch.Count - 1; i++)
			{
				Vector3 b = Branch[i].P;
				float best = float.MaxValue, yMain = b.y;
				for (int k = f; k <= j && k < Main.Count; k++)
				{
					float dx = Main[k].P.x - b.x, dz = Main[k].P.z - b.z;
					float d2 = dx * dx + dz * dz;
					if (d2 < best)
					{
						best = d2;
						yMain = Main[k].P.y;
					}
				}
				float t = Mathf.Clamp01((Mathf.Sqrt(best) - 26f) / 34f);   // the same height as the long road while the two floors overlap (within 26 of its axis), then easing to its own
				t = t * t * (3f - 2f * t);
				float y = Mathf.Lerp(yMain, b.y, t);
				worst = Mathf.Max(worst, Mathf.Abs(y - b.y));
				Branch[i].P = new Vector3(b.x, y, b.z);
			}
			Mod.Log.LogInfo("Short way height blended to the long road near the fork and the join (largest change " + worst.ToString("0.0") + " units)");
		}

		/// <summary>
		/// The height grid has a one-cell-wide wall beside each cave portal (vertices at road level next to vertices 60 units higher), so the
		/// terrain ramps from the road up to the ridge right through the mouth, with spikes of 62 inside the road's width at the exit. In front
		/// of each portal, from 20 units before it to 9.5 units in and 44 each side, the grid vertices are set to 1 under the road, so the
		/// terrain there is flat and below the floor; the cliff is then only where the mouth's hole is cut (Cave.Holes).
		/// </summary>
		/// <summary>Where each mouth's hole starts and ends (entrance, exit), in the mouth's own frame: worked out from the grid in <see cref="FlattenMouths"/>.</summary>
		internal static float[] MouthFrom = { -8f, -8f }, MouthTo = { 20f, 20f };

		private static void FlattenMouths()
		{
			int changed = 0;
			int mouth = 0;
			foreach (bool entrance in new[] { true, false })
			{
				int idx = Index(entrance ? "cave_in" : "cave_out");
				int sign = entrance ? 1 : -1;
				Vector3 p = Main[idx].P;
				Vector3 f = Main[idx + 1].P - Main[idx - 1].P;
				f.y = 0f;
				f.Normalize();
				Vector2 into = new Vector2(f.x, f.z) * sign;
				Vector2 side = new Vector2(into.y, -into.x);
				int gx0 = Mathf.Clamp(Mathf.FloorToInt((p.x - 90f - X0) / Cell), 0, Nx - 1), gx1 = Mathf.Clamp(Mathf.CeilToInt((p.x + 90f - X0) / Cell), 0, Nx - 1);
				int gz0 = Mathf.Clamp(Mathf.FloorToInt((p.z - 90f - Z0) / Cell), 0, Nz - 1), gz1 = Mathf.Clamp(Mathf.CeilToInt((p.z + 90f - Z0) / Cell), 0, Nz - 1);
				float highU = 1e9f;
				for (int gz = gz0; gz <= gz1; gz++)
				{
					for (int gx = gx0; gx <= gx1; gx++)
					{
						float rx = X0 + gx * Cell - p.x, rz = Z0 + gz * Cell - p.z;
						float u = rx * into.x + rz * into.y, l = rx * side.x + rz * side.y;
						int ri = Mathf.Clamp(idx + sign * Mathf.RoundToInt(u / 8f), 1, Main.Count - 2);
						if (u >= -20f && u <= 9.5f && Mathf.Abs(l) <= 44f)
						{
							Heights[gz * Nx + gx] = Main[ri].P.y - 1f;
							changed++;
						}
						else if (u > 0f && Mathf.Abs(l) <= 44f && Heights[gz * Nx + gx] - Main[ri].P.y > 8f)
						{
							highU = Mathf.Min(highU, u);   // the first vertex of the ridge: the ramp up to it starts at most one cell diagonal (20) before it
						}
					}
				}
				if (highU < 1e8f)
				{
					MouthFrom[mouth] = Mathf.Max(-8f, highU - 20f);
					MouthTo[mouth] = Mathf.Min(40f, highU + 10f);
				}
				Mod.Log.LogInfo("Mouth " + (entrance ? "entrance" : "exit") + ": the ridge's first grid vertex is " + highU.ToString("0") + " units in, so its hole runs from " + MouthFrom[mouth].ToString("0") + " to " + MouthTo[mouth].ToString("0"));
				mouth++;
			}
			Mod.Log.LogInfo("Terrain grid flattened in front of the two portals: " + changed + " vertices");
		}

		private static void Fill(List<Pt> into, float dropY, float[] x, float[] y, float[] z, float[] w, byte[] surface, byte[] section, byte[] hairpin)
		{
			for (int i = 0; i < x.Length; i++)
			{
				into.Add(new Pt
				{
					P = new Vector3(x[i], y[i] + dropY, z[i]),
					W = w[i],
					Surface = surface[i],
					Section = section[i],
					Hairpin = hairpin[i] > 0
				});
			}
		}

		/// <summary>Bilinear height of the mountain at a point.</summary>
		internal static float HeightAt(float x, float z)
		{
			float fx = (x - X0) / Cell;
			float fz = (z - Z0) / Cell;
			int ix = Mathf.Clamp((int)Mathf.Floor(fx), 0, Nx - 2);
			int iz = Mathf.Clamp((int)Mathf.Floor(fz), 0, Nz - 2);
			float tx = Mathf.Clamp01(fx - ix);
			float tz = Mathf.Clamp01(fz - iz);
			float a = Heights[iz * Nx + ix];
			float b = Heights[iz * Nx + ix + 1];
			float c = Heights[(iz + 1) * Nx + ix];
			float d = Heights[(iz + 1) * Nx + ix + 1];
			return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
		}

		// ---- frames along a polyline ----------------------------------------------------------------------------------

		internal static Vector3 Forward(IList<Pt> p, int i, bool loop)
		{
			int n = p.Count;
			Vector3 a = loop ? p[(i - 1 + n) % n].P : p[Mathf.Max(i - 1, 0)].P;
			Vector3 b = loop ? p[(i + 1) % n].P : p[Mathf.Min(i + 1, n - 1)].P;
			Vector3 f = b - a;
			f.y = 0f;
			return f.sqrMagnitude < 1e-6f ? Vector3.right : f.normalized;
		}

		/// <summary>Right of the direction of travel: for a road heading +X that is -Z.</summary>
		internal static Vector3 Right(IList<Pt> p, int i, bool loop)
		{
			Vector3 f = Forward(p, i, loop);
			return new Vector3(f.z, 0f, -f.x);
		}

		/// <summary>Signed turn between this point's and the next one's heading, positive = left.</summary>
		internal static float Turn(IList<Pt> p, int i, bool loop)
		{
			Vector3 a = Forward(p, i, loop);
			Vector3 b = Forward(p, loop ? (i + 1) % p.Count : Mathf.Min(i + 1, p.Count - 1), loop);
			float cross = a.x * b.z - a.z * b.x;
			return Mathf.Asin(Mathf.Clamp(cross, -1f, 1f));
		}
	}
}
