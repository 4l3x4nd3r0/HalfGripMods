using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>
	/// The cave: a damp limestone solution cave, as a tube of noisy rings (an arched roof, walls that bulge out past the floor) along the lap
	/// and along the short branch, hung with what such caves grow: stalactites and thin soda straws from the roof, stalagmites and fallen
	/// boulders on the floor's edges, a column where one met the other, wavy draperies, flowstone sheets on the walls, moss near the daylight.
	/// It is dark (see <see cref="CaveAtmosphere"/>), wet (glossy rock, puddles, dripping water) and lit only by patches of glowing fungus, a
	/// shaft of daylight in the hall and two stone lanterns at each mouth. Where the two roads split the tubes overlap; each tube leaves out
	/// the part of its wall that lies inside the other, so the fork is one hall. A trail of lit lanterns marks the long way at the fork.
	/// </summary>
	internal static class Cave
	{
		private const int N = 24;              // segments round the arch
		private const float Step = 4f;         // rings are this far apart
		private const float Shoulder = 6f;     // floor beyond the road, to the foot of the wall
		private const int Chunk = 10;          // rings per mesh chunk, so the camera can skip parts it cannot see

		private sealed class Tube
		{
			public string Name;
			public int Id;
			public readonly List<Vector3> Axis = new List<Vector3>();
			public readonly List<Vector3> Right = new List<Vector3>();
			public readonly List<float> AFloor = new List<float>();
			public readonly List<float> Road = new List<float>();   // half the road width
			public readonly List<float> Bulge = new List<float>();
			public readonly List<float> Height = new List<float>();
			public Vector3[][] Rings;
			/// <summary>The rings that are really this tube's (the short way's passage between its two openings); everything outside is inside the long hall.</summary>
			public int From = 0, To = int.MaxValue;
			/// <summary>The rings between the two mine frames: only here do stalactites, rocks and fungus grow (the walls run a little further, to a ring that lies wholly in the hall).</summary>
			public int DecorFrom = 0, DecorTo = int.MaxValue;
		}

		private static readonly Dictionary<string, Batch> batches = new Dictionary<string, Batch>();
		private static readonly List<Tube> built = new List<Tube>();
		private static readonly List<Vector3> glowSpots = new List<Vector3>();
		private static readonly List<Vector3[]> dripSpots = new List<Vector3[]>();   // {tip, floor}
		private static List<Batch> output;
		private static System.Random rng;
		private static readonly Color Teal = new Color(0.18f, 0.95f, 0.78f);

		// ---- noise --------------------------------------------------------------------------------------------------------

		private static float Hash3(int x, int y, int z)
		{
			unchecked
			{
				int h = x * 374761393 + y * 668265263 + z * 2147483647 + 1274126177;
				h = (h ^ (h >> 13)) * 1103515245;
				h ^= h >> 16;
				return (h & 0xFFFF) / 65535f;
			}
		}

		private static float Smooth(float t)
		{
			t = Mathf.Clamp01(t);
			return t * t * (3f - 2f * t);
		}

		private static float VNoise3(Vector3 p)
		{
			int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
			float fx = Smooth(p.x - x0), fy = Smooth(p.y - y0), fz = Smooth(p.z - z0);
			float c00 = Mathf.Lerp(Hash3(x0, y0, z0), Hash3(x0 + 1, y0, z0), fx);
			float c10 = Mathf.Lerp(Hash3(x0, y0 + 1, z0), Hash3(x0 + 1, y0 + 1, z0), fx);
			float c01 = Mathf.Lerp(Hash3(x0, y0, z0 + 1), Hash3(x0 + 1, y0, z0 + 1), fx);
			float c11 = Mathf.Lerp(Hash3(x0, y0 + 1, z0 + 1), Hash3(x0 + 1, y0 + 1, z0 + 1), fx);
			return Mathf.Lerp(Mathf.Lerp(c00, c10, fy), Mathf.Lerp(c01, c11, fy), fz);
		}

		// ---- portals and holes ------------------------------------------------------------------------------------------------

		/// <summary>Where the cave starts and ends: the route point and the direction into the cave.</summary>
		private static void Portal(bool entrance, out Vector3 pos, out Vector3 into)
		{
			List<Pt> p = Data.Main;
			int i = entrance ? Data.Index("cave_in") : Data.Index("cave_out");
			pos = p[i].P;
			Vector3 f = Data.Forward(p, i, true);
			into = entrance ? f : -f;
		}

		internal static List<HoleRect> Holes()
		{
			List<HoleRect> list = new List<HoleRect>();
			foreach (bool entrance in new[] { true, false })
			{
				Vector3 pos, into;
				Portal(entrance, out pos, out into);
				// the terrain is left out where the tube and the mound are: the tube's first ring is one route point (8 units) outside the portal point, and 2-unit pieces are taken out when their centre is in the hole, so From = -7 starts at the plane of that first ring (a wall of terrain across the opening if later, a slit if earlier); to 16 inside (the grid's cliff is at 8 to 10), 30 wide to 44; every cut edge gets a rock curtain (Mountain.Build), so it does not matter that the mound's foot is not exactly there
				list.Add(new HoleRect { Origin = new Vector2(pos.x, pos.z), Forward = new Vector2(into.x, into.z).normalized, From = Data.MouthFrom[entrance ? 0 : 1], To = Data.MouthTo[entrance ? 0 : 1], Half = 32f, HalfEnd = 44f, FloorY = pos.y });
			}
			return list;
		}

		/// <summary>Points inside the cave for the "is it sealed" ray check: a ring of every few along both tubes, at head height.</summary>
		internal static List<Vector3[]> Probes()
		{
			List<Vector3[]> list = new List<Vector3[]>();
			foreach (Tube t in built)
			{
				for (int k = 4; k + 4 < t.Axis.Count; k += (t.Id == 1 ? 5 : 3))
				{
					Vector3 fwd = new Vector3(-t.Right[k].z, 0f, t.Right[k].x);
					list.Add(new[] { t.Axis[k] + Vector3.up * Mathf.Min(5f, t.Height[k] * 0.35f), fwd });
				}
			}
			// off the axis, at car height and above: the halls where the tubes cross, the whole short way, and the first 60 units of each mouth
			Vector3 hallA = Data.Main[Data.Index("fork")].P, hallB = Data.Main[Data.Index("join")].P;
			foreach (Tube t in built)
			{
				for (int k = 2; k + 2 < t.Axis.Count; k += 2)
				{
					Vector3 a = t.Axis[k];
					bool hall = new Vector2(a.x - hallA.x, a.z - hallA.z).magnitude < 150f || new Vector2(a.x - hallB.x, a.z - hallB.z).magnitude < 150f;
					bool mouth = t.Id == 1 && (k < 16 || k > t.Axis.Count - 17);
					if (t.Id == 1 && !hall && !mouth)
					{
						continue;
					}
					Vector3 fwd = new Vector3(-t.Right[k].z, 0f, t.Right[k].x);
					foreach (float lat in new[] { -0.6f, 0.6f })
					{
						foreach (float h in new[] { 1.5f, 8f })
						{
							list.Add(new[] { a + t.Right[k] * (lat * t.AFloor[k]) + Vector3.up * Mathf.Min(h, t.Height[k] * 0.5f), fwd });
						}
					}
				}
			}
			return list;
		}

		/// <summary>How deep inside the cave a point is: 0 outside (or at a mouth), 1 forty units in or more.</summary>
		internal static float Depth(Vector3 q)
		{
			float best = 0f;
			for (int t = 0; t < built.Count; t++)
			{
				Tube tube = built[t];
				for (int k = 0; k < tube.Axis.Count; k++)
				{
					float dx = tube.Axis[k].x - q.x, dz = tube.Axis[k].z - q.z;
					float reach = tube.AFloor[k] + tube.Bulge[k] + 2f;
					if (dx * dx + dz * dz > reach * reach)
					{
						continue;
					}
					float y = q.y - tube.Axis[k].y;
					if (y < -4f || y > tube.Height[k])
					{
						continue;
					}
					float f = 1f;
					if (tube.Id == 1)
					{
						// daylight gives way over 100 units (25 rings) from each mouth, eased; the short tube starts inside the halls, already dark
						int edge = Mathf.Min(k, tube.Axis.Count - 1 - k);
						f = Smooth(Mathf.Clamp01((edge - 2f) / 24f));
					}
					best = Mathf.Max(best, f);
				}
			}
			return best;
		}

		/// <summary>
		/// Looks for void in the terrain holes: every terrain cell whose centre lies in a hole is left out of the mountain, so a ray dropped from
		/// above on a point of it must meet something (the mound's rock, the road, a shoulder). A point where it meets nothing is a gap you can
		/// see through. Nine points per removed cell; reports how many cells have a gap and where (in the mouth's own frame: u along the way into
		/// the cave, l across it).
		/// </summary>
		internal static string HoleCheck()
		{
			List<HoleRect> holes = Holes();
			StringBuilder sb = new StringBuilder();
			int cellsRemoved = 0, cellsWithGap = 0;
			string[] names = { "entrance", "exit" };
			for (int hi = 0; hi < holes.Count; hi++)
			{
				HoleRect h = holes[hi];
				Vector2 side = new Vector2(h.Forward.y, -h.Forward.x);
				int removed = 0, gap = 0;
				int minU = 999, maxU = -999;
				float gapU0 = 1e9f, gapU1 = -1e9f, gapL0 = 1e9f, gapL1 = -1e9f;
				float reach = h.Half + 30f;
				float cell = Data.Cell;
				int x0 = Mathf.FloorToInt((h.Origin.x - h.Half - 70f - Data.X0) / cell), x1 = Mathf.CeilToInt((h.Origin.x + h.Half + 70f - Data.X0) / cell);
				int z0 = Mathf.FloorToInt((h.Origin.y - h.Half - 70f - Data.Z0) / cell), z1 = Mathf.CeilToInt((h.Origin.y + h.Half + 70f - Data.Z0) / cell);
				for (int iz = z0; iz <= z1; iz++)
				{
					for (int ix = x0; ix <= x1; ix++)
					{
						float cx = Data.X0 + (ix + 0.5f) * cell, cz = Data.Z0 + (iz + 0.5f) * cell;
						if (!h.Contains(cx, cz))
						{
							continue;
						}
						removed++;
						bool cellGap = false;
						for (int sx = 0; sx < 3 && !cellGap; sx++)
						{
							for (int sz = 0; sz < 3 && !cellGap; sz++)
							{
								float px = Data.X0 + (ix + (sx + 0.5f) / 3f) * cell, pz = Data.Z0 + (iz + (sz + 0.5f) / 3f) * cell;
								Vector3 from = new Vector3(px, Data.HeightAt(px, pz) + 90f, pz);
								if (!Physics.Raycast(from, Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore))
								{
									cellGap = true;
									float rx = px - h.Origin.x, rz = pz - h.Origin.y;
									float u = rx * h.Forward.x + rz * h.Forward.y, l = rx * side.x + rz * side.y;
									gapU0 = Mathf.Min(gapU0, u); gapU1 = Mathf.Max(gapU1, u);
									gapL0 = Mathf.Min(gapL0, l); gapL1 = Mathf.Max(gapL1, l);
								}
							}
						}
						if (cellGap)
						{
							gap++;
						}
					}
				}
				cellsRemoved += removed;
				cellsWithGap += gap;
				sb.Append(" " + names[Mathf.Min(hi, 1)] + ": " + removed + " cells removed, " + gap + " with a gap" + (gap > 0 ? " (u " + gapU0.ToString("0") + " to " + gapU1.ToString("0") + ", across " + gapL0.ToString("0") + " to " + gapL1.ToString("0") + ")" : "") + ";");
			}
			return "terrain holes: " + cellsRemoved + " removed cells, " + cellsWithGap + " have a point nothing lies under (see-through)." + sb;
		}

		/// <summary>
		/// Offline check of the rock over the cave: every ring point of both tubes must lie at least 1 unit under the hill's surface. A point
		/// above it is where the cave pokes out of the mountain (from outside you see the cave rock, or the sky through a hole). The first and
		/// last 16 rings of the long way are the mouths, which are meant to open to the air.
		/// </summary>
		internal static string CoverCheck()
		{
			int bad = 0, total = 0;
			float worst = 1e9f;
			Vector3 worstAt = Vector3.zero;
			foreach (Tube t in built)
			{
				for (int k = 0; k < t.Rings.Length; k++)
				{
					if (t.Id == 1 && (k < 16 || k > t.Rings.Length - 17))
					{
						continue;
					}
					for (int j = 1; j < N; j++)
					{
						Vector3 q = t.Rings[k][j];
						float margin = Data.HeightAt(q.x, q.z) - q.y;
						total++;
						if (margin < 1f)
						{
							bad++;
						}
						if (margin < worst)
						{
							worst = margin;
							worstAt = q;
						}
					}
				}
			}
			return "rock cover: " + total + " ring points, " + bad + " lie less than 1 unit under the hill; least cover " + worst.ToString("0.0") + " at " + worstAt.ToString("0");
		}

		/// <summary>
		/// Is there no room for a tree here? Over the cave wherever the rock above the roof is thinner than 16 units, and round the mouths
		/// (their mounds, lanterns and ropes): a tree there would root in the cave or in the mound.
		/// </summary>
		internal static bool BlocksTree(float x, float z, float margin)
		{
			foreach (Vector3[] m in Mouths)
			{
				float dx = m[0].x - x, dz = m[0].z - z;
				float lim = 70f + margin;
				if (dx * dx + dz * dz < lim * lim)
				{
					return true;
				}
			}
			float ground = Data.HeightAt(x, z);
			for (int t = 0; t < built.Count; t++)
			{
				Tube tube = built[t];
				for (int k = 0; k < tube.Axis.Count; k++)
				{
					float dx = tube.Axis[k].x - x, dz = tube.Axis[k].z - z;
					float reach = tube.AFloor[k] + tube.Bulge[k] + margin;
					if (dx * dx + dz * dz > reach * reach)
					{
						continue;
					}
					if (ground - (tube.Axis[k].y + tube.Height[k]) < 16f)
					{
						return true;
					}
				}
			}
			return false;
		}

		// ---- building the tubes ----------------------------------------------------------------------------------------------

		private static Batch Get(string name, Texture2D tex, Color tint, bool collide, bool twoSided = false, float smooth = 0.04f, float glow = 0f)
		{
			Batch b;
			if (!batches.TryGetValue(name, out b))
			{
				b = new Batch(name, tex, tint, collide, false, twoSided) { Smooth = smooth };
				if (glow > 0f)
				{
					b.Emission = tint * glow;
				}
				batches[name] = b;
				output.Add(b);
			}
			return b;
		}

		private static Tube MakeTube(string name, int id, List<Pt> pts, int from, int to, Vector3 hallA, Vector3 hallB)
		{
			Tube t = new Tube { Name = name, Id = id };
			List<Vector3> pos = new List<Vector3>();
			List<float> width = new List<float>();
			for (int i = from; i < to; i++)
			{
				Pt a = pts[((i % pts.Count) + pts.Count) % pts.Count], b = pts[(((i + 1) % pts.Count) + pts.Count) % pts.Count];
				int n = Mathf.Max(1, Mathf.RoundToInt((b.P - a.P).magnitude / Step));
				for (int k = 0; k < n; k++)
				{
					float u = k / (float)n;
					pos.Add(Vector3.Lerp(a.P, b.P, u));
					width.Add(Mathf.Lerp(a.W, b.W, u));
				}
			}
			pos.Add(pts[((to % pts.Count) + pts.Count) % pts.Count].P);
			width.Add(pts[((to % pts.Count) + pts.Count) % pts.Count].W);
			for (int k = 0; k < pos.Count; k++)
			{
				Vector3 prev = pos[Mathf.Max(k - 1, 0)], next = pos[Mathf.Min(k + 1, pos.Count - 1)];
				Vector3 f = next - prev;
				f.y = 0f;
				f = f.sqrMagnitude < 1e-6f ? Vector3.right : f.normalized;
				t.Axis.Add(pos[k]);
				t.Right.Add(new Vector3(f.z, 0f, -f.x));
				t.Road.Add(width[k] * 0.5f);
				t.AFloor.Add(width[k] * 0.5f + (id == 2 ? 4f : Shoulder));
				// the halls where the roads split and meet are bigger
				float dA = new Vector2(pos[k].x - hallA.x, pos[k].z - hallA.z).magnitude;
				float dB = new Vector2(pos[k].x - hallB.x, pos[k].z - hallB.z).magnitude;
				float hall = Mathf.Max(Mathf.Exp(-(dA * dA) / (2f * 70f * 70f)), Mathf.Exp(-(dB * dB) / (2f * 70f * 70f)));
				float slow = VNoise3(new Vector3(pos[k].x * 0.012f, 3.1f * id, pos[k].z * 0.012f));
				if (id == 2)
				{
					// the short way is a narrower natural passage, tall enough to read as a cave and not as a tunnel
					t.Bulge.Add(3.5f + 4.0f * slow + 5f * hall);
					t.Height.Add(18f + 6f * VNoise3(new Vector3(pos[k].x * 0.02f + 40f, 7.7f * id, pos[k].z * 0.02f)) + 5f * hall);
				}
				else
				{
					t.Bulge.Add(6f + 7f * slow + 8f * hall);
					t.Height.Add(24f + 9f * VNoise3(new Vector3(pos[k].x * 0.008f + 40f, 7.7f * id, pos[k].z * 0.008f)) + 8f * hall);
				}
			}
			int count = t.Axis.Count;
			t.Rings = new Vector3[count][];
			for (int k = 0; k < count; k++)
			{
				t.Rings[k] = new Vector3[N + 1];
				for (int j = 0; j <= N; j++)
				{
					t.Rings[k][j] = RingPoint(t, k, j);
				}
			}
			return t;
		}

		private static Vector3 RingPoint(Tube t, int k, int j)
		{
			float phi = Mathf.PI * j / N;
			float s = Mathf.Max(0f, Mathf.Sin(phi));   // sin(pi) is -8.7e-8 in float, and Pow of a negative is NaN
			float x = Mathf.Cos(phi) * (t.AFloor[k] + t.Bulge[k] * s);
			float y = t.Height[k] * Mathf.Pow(s, 0.7f);
			Vector3 p = t.Axis[k] + t.Right[k] * x + Vector3.up * y;
			Vector3 centre = t.Axis[k] + Vector3.up * (t.Height[k] * 0.42f);
			Vector3 dir = (p - centre).normalized;
			float n = (VNoise3(p * 0.06f) - 0.5f) * 2f * 3.4f * Mathf.Pow(s, 0.6f) + (VNoise3(p * 0.23f) - 0.5f) * 2f * 1.3f * s;
			// the foot of the wall (the lowest rows) only ever bulges outward and eases in: noise that pushed it toward the road made ledges
			// the tyres caught on at the edge of the floor
			if (s < 0.5f)
			{
				float ease = Smooth(Mathf.Clamp01(s / 0.5f));
				n = Mathf.Max(n, 0f) * ease;
			}
			return p + dir * n;
		}

		/// <summary>Is a point inside the cave volume of a tube (the nearest ring's arch)?</summary>
		private static bool Inside(Tube t, Vector3 q, float margin)
		{
			int best = -1;
			float bd = 1e9f;
			for (int k = 0; k < t.Axis.Count; k++)
			{
				float dx = t.Axis[k].x - q.x, dz = t.Axis[k].z - q.z;
				float d = dx * dx + dz * dz;
				if (d < bd)
				{
					bd = d;
					best = k;
				}
			}
			if (best < 0 || bd > 60f * 60f)
			{
				return false;
			}
			if (best < t.From || best > t.To)
			{
				return false;   // the part of the short tube that lies in the long hall is not a tube any more
			}
			// a point behind the first ring or past the last one is not inside the tube
			Vector3 along = new Vector3(-t.Right[best].z, 0f, t.Right[best].x);
			float ahead = Vector3.Dot(q - t.Axis[best], along);
			if ((best == 0 && ahead < 0f) || (best == t.Axis.Count - 1 && ahead > 0f))
			{
				return false;
			}
			float y = q.y - t.Axis[best].y;
			float h = t.Height[best];
			if (y < -0.5f || y >= h * 0.98f - margin)
			{
				return false;
			}
			float sinPhi = Mathf.Pow(Mathf.Clamp01(y / h), 1f / 0.7f);
			float cosPhi = Mathf.Sqrt(Mathf.Max(0f, 1f - sinPhi * sinPhi));
			float xmax = cosPhi * (t.AFloor[best] + t.Bulge[best] * sinPhi);
			float x = Vector3.Dot(q - t.Axis[best], t.Right[best]);
			return Mathf.Abs(x) < xmax - margin;
		}

		/// <summary>
		/// Moves a point that lies inside a tube onto the NEAREST wall of that tube (the side the point is on): same height, same place along the
		/// tube, only its sideways position changes, to where the wall's own (noisy) ring points are at that height. <paramref name="moved"/> is
		/// how far that was.
		/// </summary>
		private static Vector3 OntoWall(Tube t, Vector3 q, out float moved)
		{
			int best = 0;
			float bd = 1e9f;
			for (int k = 0; k < t.Axis.Count; k++)
			{
				float dx = t.Axis[k].x - q.x, dz = t.Axis[k].z - q.z;
				float d = dx * dx + dz * dz;
				if (d < bd)
				{
					bd = d;
					best = k;
				}
			}
			Vector3[] ring = t.Rings[best];
			float xq = Vector3.Dot(q - t.Axis[best], t.Right[best]);
			bool left = xq < 0f;
			// a ring runs from the right floor edge (j = 0) over the roof (j = N / 2) to the left floor edge (j = N): take the half the point is on
			int jTop = left ? N / 2 : N / 2, jFoot = left ? N : 0, step = left ? 1 : -1;
			float xWall = Vector3.Dot(ring[jFoot] - t.Axis[best], t.Right[best]);
			for (int j = jTop; j != jFoot; j += step)
			{
				float y0 = ring[j].y, y1 = ring[j + step].y;
				if (q.y <= y0 && q.y >= y1)
				{
					float u = Mathf.Abs(y0 - y1) < 1e-4f ? 0f : (y0 - q.y) / (y0 - y1);
					float x0 = Vector3.Dot(ring[j] - t.Axis[best], t.Right[best]);
					float x1 = Vector3.Dot(ring[j + step] - t.Axis[best], t.Right[best]);
					xWall = Mathf.Lerp(x0, x1, u);
					break;
				}
			}
			moved = Mathf.Abs(xWall - xq);
			return q + t.Right[best] * (xWall - xq);
		}
		// ---- the whole cave ---------------------------------------------------------------------------------------------------

		internal static int Build(List<Batch> into, Transform root, int layer, Material source)
		{
			caveRoot = root;
			caveLayer = layer;
			frameCount = 0;
			Frames.Clear();
			output = into;
			batches.Clear();
			built.Clear();
			glowSpots.Clear();
			dripSpots.Clear();
			Mouths.Clear();
			Landmarks.LitLanterns.Clear();
			rng = new System.Random(4004);
			List<Pt> p = Data.Main;
			int cin = Data.Index("cave_in"), cout = Data.Index("cave_out");
			int fork = Data.Index("fork"), join = Data.Index("join");
			Vector3 hallA = p[fork].P, hallB = p[join].P;

			Tube main = MakeTube("main", 1, p, cin - 1, cout + 1, hallA, hallB);
			Tube branch = MakeTube("short", 2, Data.Branch, 0, Data.Branch.Count - 1, hallA, hallB);
			Tube[] tubes = { main, branch };
			built.Add(main);
			built.Add(branch);
			PassageRange(main, branch);

			int quads = 0;
			foreach (Tube t in tubes)
			{
				Tube other = t == main ? branch : main;
				for (int k = 0; k + 1 < t.Rings.Length; k++)
				{
					if (k < t.From || k + 1 > t.To)
					{
						skippedWalls++;
						continue;
					}
					Vector3 axis = t.Axis[k];
					bool near = (new Vector2(axis.x - hallA.x, axis.z - hallA.z).magnitude < 140f) || (new Vector2(axis.x - hallB.x, axis.z - hallB.z).magnitude < 140f);
					string chunk = t.Name + (k / Chunk);
					bool nearPortal = t == main && (k < 18 || k > t.Rings.Length - 19);
					for (int j = 0; j < N; j++)
					{
						Vector3 a = t.Rings[k][j], b = t.Rings[k][j + 1], c = t.Rings[k + 1][j + 1], d = t.Rings[k + 1][j];
						Vector3 mid = (a + b + c + d) * 0.25f;
						// where the two tubes overlap, a wall that lies inside the other tube is left out, so no wall stands across either road.
						if (near && Inside(other, mid, 5.0f))
						{
							// deep inside the other tube: left out, as always. What stays is the band within 5 units of its wall.
							if (!(t.Id == 2 && j >= N / 2 && k < frameOpen))
							{
								continue;
							}
							// the short way's LEFT wall before its barricade, in that band: a ribbon of it stood in the hall as a flat sheet with a hard
							// end, and cutting it at the wall left that end open to the hall. Its corners that lie inside the long tube are moved onto
							// the NEAREST long wall (same height, same place along, at most 6 units, else the quad is left out: a first version moved
							// them from anywhere in the hall to the left wall and stretched slabs across the long road), so the wall flows round into
							// the long one; a quad wholly inside is the long wall already and is left out.
							Vector3[] cs = { a, b, c, d };
							int inCount = 0;
							bool tooFar = false;
							for (int q = 0; q < 4; q++)
							{
								if (Inside(other, cs[q], 0f))
								{
									float moved;
									cs[q] = OntoWall(other, cs[q], out moved);
									inCount++;
									if (moved > 6f)
									{
										tooFar = true;
									}
								}
							}
							if (inCount == 4 || tooFar)
							{
								continue;
							}
							a = cs[0];
							b = cs[1];
							c = cs[2];
							d = cs[3];
							mid = (a + b + c + d) * 0.25f;
						}
						Vector3 centre = axis + Vector3.up * (t.Height[k] * 0.42f);
						float n = VNoise3(mid * 0.045f + new Vector3(9f, 4f, 2f));
						Batch target;
						if (nearPortal && mid.y - axis.y < 16f && n > 0.45f)
						{
							target = Get("cave moss " + chunk, Tex.Moss, new Color(0.7f, 0.72f, 0.7f), true, false, 0.5f);
						}
						else if (n > 0.66f)
						{
							target = Get("cave flowstone " + chunk, Tex.Flowstone, new Color(0.72f, 0.72f, 0.72f), true, false, 0.7f);
						}
						else
						{
							target = Get("cave rock " + chunk, Tex.CaveRock, new Color(1.0f, 1.0f, 1.05f), true, false, 0.55f);
						}
						target.Quad(a, b, c, d, centre - mid, 26f);
						quads++;
					}
				}
			}
			// an underlay 0.9 below the floor, wider than the floor and out of sight: a hairline crack between floor pieces or at a wall foot
			// shows dark rock instead of the hill's underside. No collider; the mouths (outdoors) are left out.
			foreach (Tube t in tubes)
			{
				for (int k = 0; k + 1 < t.Axis.Count; k++)
				{
					if ((t.Id == 1 && (k < 8 || k > t.Axis.Count - 10)) || k < t.From || k + 1 > t.To)
					{
						continue;
					}
					Batch under = Get("cave underlay " + t.Name + (k / Chunk), Tex.White, new Color(0.09f, 0.11f, 0.12f), false, true, 0.05f);   // floor-like, so a hairline crack shows floor-coloured rock and not a black pit
					float w0 = t.AFloor[k] + 3f, w1 = t.AFloor[k + 1] + 3f;
					Vector3 a0 = t.Axis[k] - t.Right[k] * w0 + Vector3.down * 0.9f, a1 = t.Axis[k] + t.Right[k] * w0 + Vector3.down * 0.9f;
					Vector3 b0 = t.Axis[k + 1] - t.Right[k + 1] * w1 + Vector3.down * 0.9f, b1 = t.Axis[k + 1] + t.Right[k + 1] * w1 + Vector3.down * 0.9f;
					under.Quad(a0, a1, b1, b0, Vector3.up, 30f);
				}
			}
			Mod.Log.LogInfo("Cave: " + main.Rings.Length + " + " + branch.Rings.Length + " rings, " + quads + " wall quads; the short way's passage is rings " + branch.From + " to " + branch.To + ", " + skippedWalls + " ring segments outside it left out");

			foreach (Tube t in tubes)
			{
				Speleothems(t, t == main ? branch : main);
				Fungus(t);
			}
			Entrance(main, true);
			Entrance(main, false);
			Column(main, branch);
			Adit(main, branch);
			ForkLanterns(main);
			GuideWalls(main, branch);
			Mod.Log.LogInfo("Cave: " + glowSpots.Count + " glowing fungus patches, " + dripSpots.Count + " drip spots");
			return MakeLights(main, root, layer);
		}

		// ---- what grows in it ------------------------------------------------------------------------------------------------------

		/// <summary>Does a floor point lie on the other tube's road (its whole floor, with a margin)? Rocks must never be left there.</summary>
		private static bool OnRoadOf(Tube o, Vector3 q, float margin)
		{
			float best = 1e9f;
			int bk = -1;
			for (int k = o.From; k < o.Axis.Count && k <= o.To; k++)
			{
				float dx = o.Axis[k].x - q.x, dz = o.Axis[k].z - q.z;
				float d = dx * dx + dz * dz;
				if (d < best)
				{
					best = d;
					bk = k;
				}
			}
			if (bk < 0)
			{
				return false;
			}
			float lim = o.AFloor[bk] + margin;
			return best < lim * lim && Mathf.Abs(q.y - o.Axis[bk].y) < 9f;
		}

		private static void Speleothems(Tube t, Tube other)
		{
			int total = 0;
			for (int k = 2; k + 2 < t.Rings.Length; k++)
			{
				if (k < t.DecorFrom || k > t.DecorTo)
				{
					skippedDecor++;   // outside the frames, in the long hall: nothing grows here
					continue;
				}
				string chunk = t.Name + (k / (Chunk * 2));
				Batch drip = Get("cave drips " + chunk, Tex.Flowstone, new Color(0.58f, 0.58f, 0.56f), false, false, 0.75f);
				Batch mites = Get("cave stalagmites " + chunk, Tex.Flowstone, new Color(0.55f, 0.55f, 0.52f), false, false, 0.7f);
				Batch rocks = Get("cave boulders " + chunk, Tex.CaveRock, new Color(0.6f, 0.6f, 0.62f), false, false, 0.5f);   // never solid: the guide walls keep the car off them
				float clear = t.Id == 2 ? 4.2f : 1.6f;   // the long way is smooth and its decoration is only to look at; the short way keeps solid rocks, off the line
				Vector3[] ring = t.Rings[k];

				// stalactites and soda straws on the roof and the upper walls
				int stalactites = t.Id == 2 ? rng.Next(0, 2) : rng.Next(0, 3);
				for (int i = 0; i < stalactites; i++)
				{
					int j = rng.Next(4, N - 3);
					Vector3 root = ring[j] + new Vector3(0f, -0.4f, 0f);
					if (Inside(other, root, 0f))
					{
						skippedDecor++;   // this bit of roof is inside the other tube
						continue;
					}
					float len = Mathf.Min(2.5f + (float)rng.NextDouble() * 12f, (root.y - t.Axis[k].y) * 0.5f);
					float rad = 0.7f + (float)rng.NextDouble() * 2.2f;
					Vector3 tip = root + new Vector3((float)(rng.NextDouble() - 0.5) * 1.2f, -len, (float)(rng.NextDouble() - 0.5) * 1.2f);
					drip.Cone(root, rad, tip, 0f, 6, 12f);
					if (total % 6 == 0)
					{
						dripSpots.Add(new[] { tip, new Vector3(tip.x, t.Axis[k].y, tip.z) });
					}
					total++;
				}
				if (rng.NextDouble() < 0.7)
				{
					int j = rng.Next(4, N - 3);
					Vector3 root = ring[j] + new Vector3(0f, -0.3f, 0f);
					int straws = Inside(other, root, 0f) ? 0 : rng.Next(2, 6);
					for (int i = 0; i < straws; i++)
					{
						Vector3 o = new Vector3((float)(rng.NextDouble() - 0.5) * 6f, 0f, (float)(rng.NextDouble() - 0.5) * 3f);
						float len = Mathf.Min(3f + (float)rng.NextDouble() * 9f, (root.y - t.Axis[k].y) * 0.5f);
						drip.Cone(root + o, 0.35f, root + o + Vector3.down * len, 0f, 4, 8f);
						total++;
					}
				}

				// stalagmites and fallen rock on the shoulders of the floor, beside the road, never on it
				if (rng.NextDouble() < 0.3)
				{
					float side = rng.NextDouble() < 0.5 ? -1f : 1f;
					float rad = 1.0f + (float)rng.NextDouble() * (t.Id == 2 ? 0.3f : 1.7f);   // on the short way the floor is only 4 wider than the road, so they are slim
					float roadHalf = t.Road[k];
					float lat = side * Mathf.Min(t.AFloor[k] + 0.4f, t.AFloor[k] - 2f + rad + (float)rng.NextDouble() * 0.3f);   // the inner edge stays outside the guide wall (AFloor - 2)
					Vector3 b = t.Axis[k] + t.Right[k] * lat;
					float len = 2f + (float)rng.NextDouble() * 8f;
					if (!OnRoadOf(other, b, 4f) && !Inside(other, b + Vector3.up * 1f, 0f))
					{
						mites.Cone(b + Vector3.down * 0.3f, rad, b + Vector3.up * len, 0f, 6, 12f);
						total++;
					}
				}
				if (rng.NextDouble() < 0.4)
				{
					float side = rng.NextDouble() < 0.5 ? -1f : 1f;
					float r = 1.2f + (float)rng.NextDouble() * (t.Id == 2 ? 0.4f : 1.8f);
					float roadHalf = t.Road[k];
					float lat = side * Mathf.Min(t.AFloor[k] + 0.4f, t.AFloor[k] - 2f + r * 1.25f + (float)rng.NextDouble() * 0.5f);
					Vector3 b = t.Axis[k] + t.Right[k] * lat;
					if (!OnRoadOf(other, b, 5f) && !Inside(other, b + Vector3.up * 1f, 0f))
					{
						rocks.Sphere(b + Vector3.up * (r * 0.3f), new Vector3(r * 1.2f, r * 0.7f, r), 3, 7, 10f, 0.35f, rng.Next(1000));
						total++;
					}
				}

				// a drapery: a wavy sheet hanging from the roof, along the tube
				if (rng.NextDouble() < 0.06 && k + 5 < t.Rings.Length && !Inside(other, t.Rings[k][3], 0f))
				{
					int j = rng.Next(3, N - 2);
					Batch sheet = Get("cave draperies " + chunk, Tex.Flowstone, new Color(0.62f, 0.6f, 0.58f), false, true, 0.7f);
					float baseLen = 5f + (float)rng.NextDouble() * 8f;
					int m = rng.Next(3, 5);
					for (int i = 0; i < m; i++)
					{
						Vector3 a = t.Rings[k + i][j] + Vector3.down * 0.3f, bb = t.Rings[k + i + 1][j] + Vector3.down * 0.3f;
						float l0 = baseLen * (0.6f + 0.4f * Mathf.Sin(i * 1.9f)), l1 = baseLen * (0.6f + 0.4f * Mathf.Sin((i + 1) * 1.9f));
						sheet.Quad(a, bb, bb + Vector3.down * l1, a + Vector3.down * l0, Vector3.up, 12f);
						total++;
					}
				}
			}
			Mod.Log.LogInfo("Cave '" + t.Name + "': " + total + " speleothems and boulders, " + skippedDecor + " left out because they would hang inside the other tube");
		}

		/// <summary>Patches of glowing fungus low on the walls: the only light in the cave besides the daylight in the hall.</summary>
		private static void Fungus(Tube t)
		{
			Tube other = built[0] == t ? built[1] : built[0];
			for (int k = 5; k + 5 < t.Rings.Length; k++)
			{
				if (rng.NextDouble() > 0.1 || k < t.DecorFrom || k > t.DecorTo)
				{
					continue;
				}
				Batch glow = Get("cave fungus " + t.Name + (k / (Chunk * 4)), Tex.White, Teal, false, false, 0.85f, 1.6f);
				int j = rng.Next(1, 6);
				if (rng.NextDouble() < 0.5)
				{
					j = N - j;
				}
				Vector3 wall = t.Rings[k][j];
				if (Inside(other, wall, 0f))
				{
					continue;
				}
				Vector3 inward = (t.Axis[k] + Vector3.up * 6f - wall).normalized;
				int clump = rng.Next(3, 7);
				for (int i = 0; i < clump; i++)
				{
					Vector3 o = wall + inward * 0.5f + new Vector3((float)(rng.NextDouble() - 0.5) * 5f, (float)rng.NextDouble() * 3.5f, (float)(rng.NextDouble() - 0.5) * 5f);
					float r = 0.45f + (float)rng.NextDouble() * 0.9f;
					glow.Sphere(o, new Vector3(r, r * 0.75f, r), 3, 6, 6f, 0.3f, rng.Next(1000));
				}
				glowSpots.Add(wall + inward * 3f + Vector3.up * 2f);
			}
		}

		/// <summary>The entrance and the exit: a rock mound round the opening (its lip faces the road), two stone lanterns and a shimenawa.</summary>
		private static void Entrance(Tube t, bool entrance)
		{
			int count = t.Rings.Length;
			int span = 13;
			Batch rock = Get("portal rock " + (entrance ? "in" : "out"), Tex.Rock, new Color(0.8f, 0.8f, 0.8f), true, false, 0.3f);
			Batch moss = Get("portal moss " + (entrance ? "in" : "out"), Tex.Moss, new Color(0.85f, 0.9f, 0.85f), false, false, 0.4f);
			Vector3[][] outer = new Vector3[span + 1][];
			for (int m = 0; m <= span; m++)
			{
				int k = entrance ? m : count - 1 - m;
				float s = Mathf.Lerp(1.28f, 2.5f, Smooth(m / (float)span));
				Vector3 c = t.Axis[k] + Vector3.up * (t.Height[k] * 0.4f);
				outer[m] = new Vector3[N + 1];
				for (int j = 0; j <= N; j++)
				{
					Vector3 pt = t.Rings[k][j];
					Vector3 o = c + (pt - c) * s;
					float n = (VNoise3(o * 0.08f) - 0.5f) * 5f;
					o += (o - c).normalized * n;
					if (j == 0 || j == N)
					{
						o.y = t.Axis[k].y - 3f;
					}
					outer[m][j] = o;
				}
			}
			for (int m = 0; m < span; m++)
			{
				int k0 = entrance ? m : count - 1 - m;
				Vector3 c = t.Axis[k0] + Vector3.up * (t.Height[k0] * 0.4f);
				for (int j = 0; j < N; j++)
				{
					Vector3 a = outer[m][j], b = outer[m][j + 1], cc = outer[m + 1][j + 1], d = outer[m + 1][j];
					Vector3 mid = (a + b + cc + d) * 0.25f;
					(VNoise3(mid * 0.05f) > 0.6f && m < 7 ? moss : rock).Quad(a, b, cc, d, mid - c, 30f);
				}
			}
			Vector3 outward = entrance ? -Data.Forward(Data.Main, Data.Index("cave_in"), true) : Data.Forward(Data.Main, Data.Index("cave_out"), true);
			int k00 = entrance ? 0 : count - 1;
			for (int j = 0; j < N; j++)
			{
				rock.Quad(t.Rings[k00][j], t.Rings[k00][j + 1], outer[0][j + 1], outer[0][j], outward, 30f);
			}
			rock.Quad(t.Rings[k00][0], outer[0][0], outer[0][0] + outward * 0.1f + Vector3.down * 0.1f, t.Rings[k00][0] + Vector3.down * 3f, outward, 30f);
			rock.Quad(t.Rings[k00][N], outer[0][N], outer[0][N] + outward * 0.1f + Vector3.down * 0.1f, t.Rings[k00][N] + Vector3.down * 3f, outward, 30f);

			Mouths.Add(new[] { t.Axis[k00] + Vector3.up * (t.Height[k00] * 0.4f), outward });

			// the shimenawa across the mouth: the cave is a sacred place; and a pair of stone lanterns outside it
			Vector3 axis = t.Axis[k00] + outward * 6f;
			Vector3 right = t.Right[k00];
			Batch rope = Get("cave shimenawa", Tex.White, new Color(0.78f, 0.68f, 0.42f), false);
			Batch paper = Get("cave shide", Tex.White, new Color(0.96f, 0.96f, 0.94f), false, true);
			float half = t.AFloor[k00] - 2f;
			float ry = t.Height[k00] * 0.62f;
			int pieces = 12;
			for (int i = 0; i < pieces; i++)
			{
				float x0 = -half + 2f * half * i / pieces, x1 = -half + 2f * half * (i + 1) / pieces;
				float y0 = ry - 3f * (1f - (x0 / half) * (x0 / half)), y1 = ry - 3f * (1f - (x1 / half) * (x1 / half));
				rope.Cone(axis + right * x0 + Vector3.up * y0, 0.9f, axis + right * x1 + Vector3.up * y1, 0.9f, 6, 6f);
			}
			// the rope is tied to two posts and a beam, set on the shoulders outside the road: it hangs from something
			Batch postWood = Get("portal post " + (entrance ? "in" : "out"), Tex.WoodDark, new Color(0.75f, 0.7f, 0.65f), false, false, 0.2f);
			float floorY = t.Axis[k00].y - 1.0f;
			float topY = t.Axis[k00].y + ry + 2.6f;
			foreach (float side in new[] { -1f, 1f })
			{
				Vector3 pb = axis + right * side * (half + 0.9f);
				postWood.Pillar(new Vector3(pb.x, floorY, pb.z), 1.15f, topY - floorY, 8, 6f);
			}
			postWood.Box(new Vector3(axis.x, topY - 0.4f, axis.z), new Vector3(2f * (half + 2.6f), 1.1f, 1.4f), Quaternion.LookRotation(outward, Vector3.up), 6f);
			foreach (float x in new[] { -half * 0.6f, -half * 0.2f, half * 0.2f, half * 0.6f })
			{
				Vector3 top = axis + right * x + Vector3.up * (ry - 3f * (1f - (x / half) * (x / half)));
				paper.Quad(top + right * -0.8f, top + right * 0.8f, top + right * 0.8f + Vector3.down * 4f, top + right * -0.8f + Vector3.down * 4f, outward, 6f);
			}
			foreach (float side in new[] { -1f, 1f })
			{
				Landmarks.Lantern(t.Axis[k00] + outward * 16f + right * side * (t.AFloor[k00] + 5f), true);
			}
		}

		/// <summary>The limestone column where the roads part: a stalagmite and a stalactite that grew together.</summary>
		private static void Column(Tube main, Tube branch)
		{
			List<Pt> p = Data.Main;
			int fork = Data.Index("fork");
			Vector3 F = p[fork].P;
			// Candidates are the midpoints between a ring of the short road and a ring of the long one. The first one that keeps 31 units
			// from every ring of both roads (a road is 19 half-wide at the floor, the column 9.5 at its foot) is where the column stands:
			// it can never be in either road, whatever shape the two make.
			for (int k = 4; k < branch.Axis.Count - 4; k++)
			{
				Vector3 q = branch.Axis[k];
				for (int m = 0; m < main.Axis.Count; m++)
				{
					Vector3 r = main.Axis[m];
					if ((new Vector2(r.x - F.x, r.z - F.z)).magnitude > 260f || Mathf.Abs(r.y - q.y) > 14f)
					{
						continue;
					}
					Vector3 centre = new Vector3((q.x + r.x) * 0.5f, Mathf.Min(q.y, r.y), (q.z + r.z) * 0.5f);
					float clear = 1e9f;
					foreach (Tube t in new[] { main, branch })
					{
						for (int i = 0; i < t.Axis.Count; i++)
						{
							float d = new Vector2(t.Axis[i].x - centre.x, t.Axis[i].z - centre.z).magnitude;
							if (d < clear)
							{
								clear = d;
							}
						}
					}
					if (clear < 31f)
					{
						continue;
					}
					float floorY = centre.y;
					float top = Mathf.Max(branch.Height[k], 36f) + 6f;
					Batch col = Get("cave column", Tex.Flowstone, new Color(0.62f, 0.6f, 0.58f), true, false, 0.75f);
					col.Cone(new Vector3(centre.x, floorY - 0.5f, centre.z), 9.5f, new Vector3(centre.x, floorY + 12f, centre.z), 5.2f, 12, 12f, false);
					col.Cone(new Vector3(centre.x, floorY + 12f, centre.z), 5.2f, new Vector3(centre.x, floorY + top * 0.62f, centre.z), 4.4f, 12, 12f, false);
					col.Cone(new Vector3(centre.x, floorY + top * 0.62f, centre.z), 4.4f, new Vector3(centre.x, floorY + top, centre.z), 8.5f, 12, 12f, false);
					glowSpots.Add(centre + Vector3.up * 8f);
					Mod.Log.LogInfo("Cave column at " + centre + ", " + clear.ToString("0") + " units from both roads");
					return;
				}
			}
			Mod.Log.LogWarning("Cave column: no spot keeps 31 units from both roads.");
		}

		// ---- the short way: a hole in the wall -----------------------------------------------------------------------------------

		/// <summary>
		/// The short way is an old mine adit that water broke through the wall of the long cave (at the left bend, straight ahead of the road).
		/// Someone tried to patch the hole with planks and failed: leaning posts, a header, boards hanging off one side, boards on the floor;
		/// inside, timber frames every 28 units with every third one half collapsed. No sign.
		/// </summary>
		internal static Vector3 AditPos, AditDir, JoinPos, JoinDir;
		/// <summary>The two openings of the cave: {centre of the opening, direction out of the cave}. Debug checks plug them.</summary>
		internal static readonly List<Vector3[]> Mouths = new List<Vector3[]>();

		private static int skippedWalls, skippedDecor, branchOpen, branchExit, frameOpen, frameExit;

		/// <summary>
		/// Where the short way really is a passage: from the first ring that has left the long tube's volume (the opening at the fork) to the
		/// last one before it runs back into it (the opening at the join). Rings outside are inside the long hall: they get no walls, no
		/// stalactites, no fungus, and do not cut the long tube's walls, so no roof or rock continues past the mine frames.
		/// </summary>
		private static void PassageRange(Tube main, Tube branch)
		{
			skippedWalls = 0;
			skippedDecor = 0;
			int count = branch.Axis.Count;
			int open = 4;
			for (int k = 2; k < count - 2; k++)
			{
				if (!Inside(main, branch.Axis[k] + Vector3.up * 5f, 0f))
				{
					open = k;
					break;
				}
			}
			int exit = count - 4;
			for (int k = count - 3; k > open; k--)
			{
				if (!Inside(main, branch.Axis[k] + Vector3.up * 5f, 0f))
				{
					exit = k;
					break;
				}
			}
			branchOpen = open;
			branchExit = exit;
			// the walls go on until a ring whose whole outline lies inside the long tube (so the cut end opens into the hall and not into rock)
			Func<int, bool> ringInside = k =>
			{
				for (int j = 0; j <= N; j++)
				{
					if (!Inside(main, branch.Rings[k][j] + Vector3.up * 0.6f, -0.5f))
					{
						return false;
					}
				}
				return true;
			};
			int from = 0;
			for (int k = open; k >= 0; k--)
			{
				if (ringInside(k))
				{
					from = Mathf.Max(0, k - 1);
					break;
				}
			}
			int to = count - 1;
			for (int k = exit; k < count; k++)
			{
				if (ringInside(k))
				{
					to = Mathf.Min(count - 1, k + 1);
					break;
				}
			}
			branch.From = from;
			branch.To = to;
			branch.DecorFrom = open;
			branch.DecorTo = exit;
			// the barricades stand where the shortcut is at least 42 units from the long road, so their sensors (12.4 each side of the short
			// road) can never reach the long road's floor (25 each side of its axis): a first version stood in the openings and held cars on the
			// long way at the fork and the join
			Func<int, float> apart = k =>
			{
				float best = 1e9f;
				for (int q = 0; q < main.Axis.Count; q++)
				{
					float dx = main.Axis[q].x - branch.Axis[k].x, dz = main.Axis[q].z - branch.Axis[k].z;
					best = Mathf.Min(best, dx * dx + dz * dz);
				}
				return Mathf.Sqrt(best);
			};
			frameOpen = open;
			for (int k = open; k < exit - 12; k++)
			{
				if (apart(k) >= 42f)
				{
					frameOpen = k;
					break;
				}
			}
			frameExit = exit;
			for (int k = exit; k > frameOpen + 12; k--)
			{
				if (apart(k) >= 42f)
				{
					frameExit = k;
					break;
				}
			}
			Mod.Log.LogInfo("Short way: passage rings " + open + " to " + exit + ", barricades at rings " + frameOpen + " and " + frameExit + " (" + apart(frameOpen).ToString("0") + " and " + apart(frameExit).ToString("0") + " units from the long road)");		}

		private static void Adit(Tube main, Tube branch)
		{
			Color tint = new Color(0.55f, 0.5f, 0.46f);
			Batch posts = Get("mine timber", Tex.WoodDark, tint, true, false, 0.45f);
			Batch beams = Get("mine timber beams", Tex.WoodDark, tint, false, false, 0.45f);
			Batch planks = Get("mine planks", Tex.WoodDark, new Color(0.62f, 0.56f, 0.5f), false, false, 0.4f);
			int open = frameOpen;
			int last = frameExit;   // the barricades stand well inside the passage, clear of the long road
			// frames along the passage, starting at the mouth
			// a timber mine entrance at each end of the passage (at the fork and at the join), and nothing low or crowded between them
			MineFrame(branch, open, posts, beams, planks, false, true);
			if (last > open + 12)
			{
				MineFrame(branch, last, posts, beams, planks, true, true);
			}
			Vector3 a = branch.Axis[open], r = branch.Right[open];
			Vector3 f = new Vector3(-r.z, 0f, r.x);
			// the boards that used to lie on the floor in front of the mouth are gone; their 3 draws each from rng are still taken, so everything
			// random that is built after this stays where it was
			for (int i = 0; i < 5; i++)
			{
				rng.NextDouble();
				rng.NextDouble();
				rng.NextDouble();
			}
			dripSpots.Add(new[] { a + Vector3.up * (branch.Height[open] * 0.85f), new Vector3(a.x, a.y, a.z) });
			AditPos = a;
			AditDir = f;
			Vector3 jr = branch.Right[Mathf.Clamp(last, 0, branch.Axis.Count - 1)];
			JoinPos = branch.Axis[Mathf.Clamp(last, 0, branch.Axis.Count - 1)];
			JoinDir = new Vector3(-jr.z, 0f, jr.x);
			Mod.Log.LogInfo("Short way: the hole in the wall is at ring " + open + " of " + branch.Axis.Count);
		}

		private static Transform caveRoot;
		private static int caveLayer, frameCount;

		internal sealed class FrameInfo
		{
			public Vector3 A, F, R;
			public float Half;
			public Breakable B;
		}

		internal static readonly List<FrameInfo> Frames = new List<FrameInfo>();

		/// <summary>Debug: the mid-points of the long road's guide walls for the hit test: {point on the wall, along the road, toward the wall}.</summary>
		internal static Vector3[] WallSample(int ring, float side)
		{
			Tube t = built[0];
			int k = Mathf.Clamp(ring, 3, t.Axis.Count - 4);
			Vector3 f = new Vector3(-t.Right[k].z, 0f, t.Right[k].x);
			return new[] { t.Axis[k] + t.Right[k] * (side * (t.AFloor[k] - 2f)), f, t.Right[k] * side, t.Axis[k] };
		}

		/// <summary>
		/// Check of the guide walls: from the axis of each road every 2 rings, a horizontal ray at 1.5 up to each side must meet the
		/// guide wall first, about where it should be. Left out: where the other road's floor lies (the fork and join openings) and the mouths.
		/// </summary>
		internal static string GuideCheck()
		{
			int rays = 0, good = 0, bad = 0;
			StringBuilder first = new StringBuilder();
			foreach (Tube t in built)
			{
				Tube other = t == built[0] ? built[1] : built[0];
				int n = t.Axis.Count;
				int from = t.Id == 1 ? 6 : Mathf.Max(0, t.DecorFrom);
				int to = t.Id == 1 ? n - 7 : Mathf.Min(n - 1, t.DecorTo);
				for (int k = from; k < to; k += 2)
				{
					foreach (float side in new[] { -1f, 1f })
					{
						Vector3 origin = t.Axis[k] + Vector3.up * 1.5f;
						Vector3 dir = t.Right[k] * side;
						float expect = t.AFloor[k] - 2f;
						if (OnRoadOf(other, origin + dir * expect, 1f))
						{
							continue;
						}
						rays++;
						RaycastHit[] hits = Physics.RaycastAll(origin, dir, t.AFloor[k] + 10f, ~0, QueryTriggerInteraction.Ignore);
						float best = 1e9f;
						string name = "nothing";
						foreach (RaycastHit h in hits)
						{
							if (h.distance < best)
							{
								best = h.distance;
								name = h.collider.gameObject.name;
							}
						}
						if (name == "cave guide wall" && Mathf.Abs(best - expect) < 3f)
						{
							good++;
						}
						else
						{
							bad++;
							if (bad <= 12)
							{
								first.Append(" [" + t.Name + " ring " + k + " side " + side + ": " + name + " at " + best.ToString("0.0") + ", wanted the wall at " + expect.ToString("0.0") + "]");
							}
						}
					}
				}
			}
			return "guide walls: " + rays + " side rays, " + good + " meet the wall where it should be, " + bad + " do not" + first;
		}

		private static Mesh BoxMesh(Vector3 size, float tile)
		{
			Batch b = new Batch("piece", Tex.White, Color.white, false);
			b.Box(Vector3.zero, size, Quaternion.identity, tile);
			return b.ToMesh();
		}

		/// <summary>
		/// A plank barricade across one end of the short way, as an object of its own (<see cref="Breakable"/>): two posts, the header and eight
		/// rows of boards from the floor up across the whole opening, with two diagonal braces. Nothing in it is solid; a car at the dashboard
		/// speed in <c>Timber/EntranceBreakSpeed</c> / <c>ExitBreakSpeed</c> (60) or more bursts through it, a slower one is held just short of it.
		/// </summary>
		private static void MineFrame(Tube t, int k, Batch posts, Batch beams, Batch planks, bool collapsed, bool mouth)
		{
			Vector3 a = t.Axis[k], r = t.Right[k];
			Vector3 f = new Vector3(-r.z, 0f, r.x);
			Quaternion rot = Quaternion.LookRotation(f, Vector3.up);
			float half = t.Road[k] + 1.4f;
			float h = Mathf.Max(7f, t.Height[k] * 0.78f);
			GameObject go = new GameObject("mine frame " + frameCount++) { layer = caveLayer };
			go.transform.SetParent(caveRoot, false);
			Breakable frame = go.AddComponent<Breakable>();
			frame.BreakSpeed = collapsed ? Mod.TimberExitBreakSpeed.Value : Mod.TimberEntranceBreakSpeed.Value;
			frame.SpeedKept = Mod.TimberSpeedKept.Value;
			frame.FloorY = a.y;
			frame.Direction = f;
			go.transform.position = a;
			Frames.Add(new FrameInfo { A = a, F = f, R = r, Half = half, B = frame });
			Material wood = Mats.Get(Tex.WoodDark, new Color(0.8f, 0.72f, 0.65f), false, 0.4f, new Color(0.02f, 0.015f, 0.01f));
			Material boards = Mats.Get(Tex.Planks, Color.white, false, 0.15f, new Color(0.06f, 0.045f, 0.03f));   // light pine, a little self-lit so the barricade can be read in the dark cave
			foreach (float side in new[] { -1f, 1f })
			{
				Vector3 foot = a + r * side * half + Vector3.down * 0.4f;
				// a collapsed frame has one post kicked out, leaning over the road's edge but never into it
				float lean = (collapsed && side > 0f) ? 24f : (mouth ? side * 4f : 0f);
				Quaternion tilt = Quaternion.AngleAxis(lean, f) * Quaternion.identity;
				Vector3 up = tilt * Vector3.up;
				Vector3 size = new Vector3(1.5f, h, 1.5f);
				frame.AddPiece(BoxMesh(size, 8f), wood, foot + up * (h * 0.5f), rot * Quaternion.AngleAxis(lean, Vector3.forward), caveLayer);
			}
			// the header: whole, or broken off with one end dropped
			if (!collapsed)
			{
				Vector3 size = new Vector3(2f * half + 2.4f, 1.3f, 1.7f);
				frame.AddPiece(BoxMesh(size, 8f), wood, a + Vector3.up * (h - 0.3f), rot, caveLayer);
			}
			else
			{
				Vector3 size = new Vector3(half * 1.0f, 1.3f, 1.7f);
				frame.AddPiece(BoxMesh(size, 8f), wood, a - r * half * 0.45f + Vector3.up * (h - 0.9f), rot * Quaternion.AngleAxis(-14f, Vector3.forward), caveLayer);
			}
			// the barricade: rows of boards across the whole opening, each in two uneven pieces, a little crooked, post to post
			float width = 2f * half - 1.0f;
			int rows = 8;
			for (int i = 0; i < rows; i++)
			{
				float y = 0.7f + i * 1.15f;
				float split = 0.32f + (float)rng.NextDouble() * 0.36f;
				float depth = (i % 2 == 0 ? 0.18f : -0.18f);
				float left = width * split, right = width - left;
				float roll0 = (float)(rng.NextDouble() * 8.0 - 4.0), roll1 = (float)(rng.NextDouble() * 8.0 - 4.0);
				Vector3 s0 = new Vector3(left + 0.5f, 1.0f, 0.35f), s1 = new Vector3(right + 0.5f, 1.0f, 0.35f);
				float x0 = -width * 0.5f + left * 0.5f, x1 = width * 0.5f - right * 0.5f;
				frame.AddPiece(BoxMesh(s0, 1f), boards, a + r * x0 + Vector3.up * y + f * depth, rot * Quaternion.AngleAxis(roll0, Vector3.forward), caveLayer);
				frame.AddPiece(BoxMesh(s1, 1f), boards, a + r * x1 + Vector3.up * y + f * depth, rot * Quaternion.AngleAxis(roll1, Vector3.forward), caveLayer);
			}
			float topY = 0.7f + (rows - 1) * 1.15f + 0.5f;
			float diag = Mathf.Sqrt(width * width + topY * topY);
			float ang = Mathf.Atan2(topY, width) * Mathf.Rad2Deg;
			Vector3 bsize = new Vector3(diag, 1.0f, 0.3f);
			frame.AddPiece(BoxMesh(bsize, 1f), boards, a + Vector3.up * (topY * 0.5f + 0.2f) - f * 0.3f, rot * Quaternion.AngleAxis(ang, Vector3.forward), caveLayer);
			frame.AddPiece(BoxMesh(bsize, 1f), boards, a + Vector3.up * (topY * 0.5f + 0.2f) - f * 0.3f, rot * Quaternion.AngleAxis(-ang, Vector3.forward), caveLayer);
			// one sensor over the whole opening: 9 deep, so even a fast car is inside it for several physics steps before it reaches the boards
			frame.AddSensor(a + Vector3.up * 6f, rot, new Vector3(2f * half + 2f, 12f, 9f));
		}
		// ---- the guide walls ----------------------------------------------------------------------------------------------------------

		private static int guideQuads, guideSkipped;

		/// <summary>
		/// Smooth invisible walls along both sides of both roads, 2 units inside the foot of the rock wall: a ribbon from 1.5 under the floor to
		/// 16 above it, one quad per ring step, its lateral offset averaged over 5 rings so it has no kinks, frictionless (a car scrapes along
		/// it) and tagged Wall (the game's wall sound). The rock behind stays solid as a backstop, and nothing solid stands inside: floor
		/// stalagmites and boulders have no colliders and sit outside the wall. A quad that lies on the other road's floor is left out, which
		/// keeps the fork and the join open. The mouths stay open to the dirt road.
		/// </summary>
		private static void GuideWalls(Tube main, Tube branch)
		{
			guideQuads = 0;
			guideSkipped = 0;
			if (!Mod.GuideWalls.Value)
			{
				return;
			}
			Batch wall = new Batch("cave guide wall", Tex.White, Color.white, true, true) { Invisible = true, Slippery = true };
			output.Add(wall);
			foreach (Tube t in new[] { main, branch })
			{
				Tube other = t == main ? branch : main;
				int n = t.Axis.Count;
				int from = t.Id == 1 ? 6 : Mathf.Max(0, t.DecorFrom);
				int to = t.Id == 1 ? n - 7 : Mathf.Min(n - 1, t.DecorTo);
				float[] off = new float[n];
				for (int k = 0; k < n; k++)
				{
					float sum = 0f;
					int cnt = 0;
					for (int d = -2; d <= 2; d++)
					{
						int q = Mathf.Clamp(k + d, 0, n - 1);
						sum += t.AFloor[q] - 2f;
						cnt++;
					}
					off[k] = sum / cnt;
				}
				foreach (float side in new[] { -1f, 1f })
				{
					for (int k = from; k < to; k++)
					{
						Vector3 a = t.Axis[k] + t.Right[k] * (side * off[k]);
						Vector3 b = t.Axis[k + 1] + t.Right[k + 1] * (side * off[k + 1]);
						Vector3 mid = (a + b) * 0.5f + Vector3.up * 3f;
						if (OnRoadOf(other, mid, 1f))
						{
							guideSkipped++;
							continue;
						}
						Vector3 inward = -side * (t.Right[k] + t.Right[k + 1]);
						wall.Quad(a + Vector3.down * 1.5f, b + Vector3.down * 1.5f, b + Vector3.up * 16f, a + Vector3.up * 16f, inward, 10f);
						guideQuads++;
					}
				}
			}
			Mod.Log.LogInfo("Cave: guide walls " + guideQuads + " quads, " + guideSkipped + " left out where the other road is");
		}

		// ---- the lanterns at the fork -------------------------------------------------------------------------------------------------

		/// <summary>
		/// No signs. A few lit stone lanterns (two by default) stand along the left wall of the long way, one at the fork and then every 72 units, so a
		/// driver who looks finds the easy road by its light; the short way stays dark. They stand outside the road and its clearance.
		/// </summary>
		private static void ForkLanterns(Tube main)
		{
			int forkIndex = Data.Index("fork");
			Vector3 F = Data.Main[forkIndex].P;
			int ringF = 0;
			float bestD = 1e9f;
			for (int k = 0; k < main.Axis.Count; k++)
			{
				float d = (main.Axis[k] - F).sqrMagnitude;
				if (d < bestD)
				{
					bestD = d;
					ringF = k;
				}
			}
			int placed = 0;
			for (int i = 0; i < Mathf.Clamp(Mod.ForkLanterns.Value, 0, 6); i++)
			{
				int k = ringF + i * 18;   // 72 units apart
				if (k + 6 >= main.Axis.Count)
				{
					break;
				}
				float lat = -(main.AFloor[k] + 1.2f);   // left wall side, outside the guide wall (AFloor - 2) with the 6.2 wide plinth's inner edge on it
				Vector3 floor = main.Axis[k] + main.Right[k] * lat;
				Landmarks.CaveLantern(floor);
				placed++;
			}
			Mod.Log.LogInfo("Cave: " + placed + " fork lanterns on the long way");
		}
		/// <summary>Labels the cave wants drawn (read by the scene builder after <see cref="Build"/>).</summary>
		internal static readonly List<Label> Signs = new List<Label>();

		// ---- lights and drips -------------------------------------------------------------------------------------------------------

		/// <summary>A few real lights: cool glows at the fungus, warm ones at the gantries, a shaft of daylight in the hall.</summary>
		private static int MakeLights(Tube main, Transform root, int layer)
		{
			int want = Mathf.Max(0, Mod.CaveLights.Value);
			int made = 0;
			if (want == 0)
			{
				return 0;
			}
			if (glowSpots.Count > 0)
			{
				float stride = Mathf.Max(1f, glowSpots.Count / (float)want);
				for (float f = 0f; f < glowSpots.Count && made < want; f += stride)
				{
					GameObject go = new GameObject("CaveGlow") { layer = layer };
					go.transform.SetParent(root, false);
					go.transform.position = glowSpots[Mathf.Min((int)f, glowSpots.Count - 1)];
					Light l = go.AddComponent<Light>();
					l.type = LightType.Point;
					l.color = new Color(0.35f, 0.95f, 0.85f);
					l.range = 64f;
					l.intensity = 3.4f;
					l.shadows = LightShadows.None;
					PerfCuller.AddLight(l);
					made++;
				}
			}
			foreach (Vector3 v in Landmarks.LitLanterns)
			{
				GameObject go = new GameObject("LanternLight") { layer = layer };
				go.transform.SetParent(root, false);
				go.transform.position = v + Vector3.up * 1.5f;
				Light l = go.AddComponent<Light>();
				l.type = LightType.Point;
				l.color = new Color(1f, 0.74f, 0.4f);
				l.range = 46f;
				l.intensity = 2.3f;
				l.shadows = LightShadows.None;
				PerfCuller.AddLight(l);
				made++;
			}
			// the shaft of daylight in the hall at the fork
			Vector3 hall = Data.Main[Data.Index("fork")].P + Data.Forward(Data.Main, Data.Index("fork"), true) * 150f + Vector3.up * 32f;
			GameObject shaft = new GameObject("CaveDaylight") { layer = layer };
			shaft.transform.SetParent(root, false);
			shaft.transform.position = hall;
			shaft.transform.rotation = Quaternion.LookRotation(Vector3.down + Vector3.right * 0.15f, Vector3.forward);
			Light s = shaft.AddComponent<Light>();
			s.type = LightType.Spot;
			s.color = new Color(0.78f, 0.9f, 1f);
			s.range = 110f;
			s.spotAngle = 48f;
			s.intensity = 3.2f;
			s.shadows = LightShadows.None;
			PerfCuller.AddLight(s);
			return made + 1;
		}

		/// <summary>Water dripping from the stalactites (see <see cref="Drips"/>).</summary>
		internal static void StartDrips(Transform root, int layer)
		{
			if (dripSpots.Count == 0)
			{
				return;
			}
			GameObject go = new GameObject("CaveDrips") { layer = layer };
			go.transform.SetParent(root, false);
			go.AddComponent<Drips>().Setup(dripSpots, layer);
		}
	}
}
