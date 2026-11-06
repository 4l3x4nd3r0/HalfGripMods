using System.Collections.Generic;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>
	/// The roads of the lap (and the short cave branch) as batches: asphalt with edge lines, a dashed centre line and the red and white
	/// hairpin paint; the temple's gravel path (stone threshold, edge stones) that thins into the dirt road; the dirt road with ruts; the
	/// cave's wet, uneven floor; a short sloped shoulder on every road down to the carved terrain; steel guardrail pieces on the outside
	/// of the bends (see <see cref="BreakField"/>); the chequered start line.
	/// </summary>
	internal sealed class Roads
	{
		internal readonly List<Batch> All = new List<Batch>();
		internal readonly List<RailPiece> Rails = new List<RailPiece>();
		private int railChunk;
		private int railInChunk;

		private Batch Make(string name, Texture2D tex, Color tint, bool collide, string handling = null, float smooth = 0.04f)
		{
			Batch b = new Batch(name, tex, tint, collide) { Handling = handling, Smooth = smooth };
			All.Add(b);
			return b;
		}

		private Batch asphaltHp, asphalt, skirtAsphalt, white, yellow, stripes, redPaint, dirt, dirtSkirt, rut, gravel, gravelSkirt, paving, edgeStones;
		private Batch caveFloor, shortFloor, puddles, chequerWhite, chequerBlack;
		private Batch[] blend;
		private readonly List<Vector3> mainCave = new List<Vector3>();

		/// <summary>The shoulder is short and shallow: the road lies in the ground rather than on top of it.</summary>
		private const float Shoulder = 4.5f;
		private const float Drop = 0.45f;
		private const float Lift = 0.14f;
		private const float CaveShoulder = 6f;

		internal void Build()
		{
			asphaltHp = Make("asphalt hairpin", Tex.Asphalt, Color.white, true, "slick");
			asphalt = Make("asphalt", Tex.Asphalt, Color.white, true, "touge");
			skirtAsphalt = Make("shoulder", Tex.Asphalt, new Color(0.8f, 0.8f, 0.8f), true, "touge");
			white = Make("white line", Tex.White, new Color(0.93f, 0.93f, 0.9f), false);
			yellow = Make("yellow line", Tex.White, new Color(0.95f, 0.78f, 0.15f), false);
			stripes = Make("hairpin stripes", Tex.Stripes, Color.white, true, "paint");
			redPaint = Make("hairpin red", Tex.RedPaint, Color.white, true, "paint");
			gravel = Make("temple gravel", Tex.TempleGravel, new Color(0.94f, 0.88f, 0.78f), true, "gravel");
			gravelSkirt = Make("temple gravel slope", Tex.TempleGravel, new Color(0.85f, 0.85f, 0.85f), true, "gravel");
			paving = Make("temple paving", Tex.Stone, new Color(0.9f, 0.9f, 0.9f), true);
			edgeStones = Make("path edge stones", Tex.Stone, new Color(0.85f, 0.85f, 0.82f), false);
			dirt = Make("dirt", Tex.Dirt, Color.white, true, "dirt");
			dirtSkirt = Make("dirt slope", Tex.Dirt, new Color(0.85f, 0.85f, 0.85f), true, "dirt");
			rut = Make("ruts", Tex.Dirt, new Color(0.62f, 0.6f, 0.58f), false);
			blend = new Batch[3];
			blend[0] = Make("path to dirt 1", Tex.TempleGravel, new Color(0.88f, 0.80f, 0.70f), true, "dirt");
			blend[1] = Make("path to dirt 2", Tex.Dirt, new Color(1.18f, 1.15f, 1.1f), true, "dirt");
			blend[2] = Make("path to dirt 3", Tex.Dirt, new Color(1.07f, 1.05f, 1.02f), true, "dirt");
			caveFloor = Make("cave floor", Tex.WetFloor, Color.white, true, "dirt", 0.6f);
			shortFloor = Make("short way floor", Tex.WetFloor, new Color(0.9f, 0.88f, 0.86f), true, "rough", 0.55f);
			puddles = Make("cave puddles", Tex.White, new Color(0.075f, 0.11f, 0.135f), false, null, 0.9f);   // water, not a black hole: lighter, and smaller below
			chequerWhite = Make("start line white", Tex.White, new Color(0.95f, 0.95f, 0.95f), false);
			chequerBlack = Make("start line black", Tex.White, new Color(0.06f, 0.06f, 0.07f), false);

			List<Pt> p = Data.Main;
			int n = p.Count;
			MakeBank(p);
			DensifyMainFloor(p, Data.Index("fork") - 6, Data.Index("join") + 6);
			float dist = 0f;
			for (int i = 0; i < n; i++)
			{
				int j = (i + 1) % n;
				Segment(p, i, j, true, dist, false);
				dist += (p[j].P - p[i].P).magnitude;
			}
			Guardrails(p);
			StartLine(p);
			Threshold(p);

			List<Pt> b = Data.Branch;
			for (int i = 0; i + 1 < b.Count; i++)
			{
				Segment(b, i, i + 1, false, 0f, true);
			}
			Mod.Log.LogInfo("Roads: " + Rails.Count + " guardrail pieces");
		}

		/// <summary>Banking of the hairpins: the road rises toward the outside of the bend (height per unit of lateral distance), 0 elsewhere.</summary>
		private const float BankTan = 0.0875f;   // 5 degrees
		private static float[] bank;

		/// <summary>One slope per route point: the same sin ramp along each run of hairpin points as the terrain generator uses.</summary>
		private static void MakeBank(List<Pt> p)
		{
			int n = p.Count;
			bank = new float[n];
			int k = 0;
			while (k < n)
			{
				if (!p[k].Hairpin)
				{
					k++;
					continue;
				}
				int e = k;
				while (e < n && p[e].Hairpin)
				{
					e++;
				}
				float turn = Data.Turn(p, (k + e) / 2, true);
				float sign = turn > 0f ? 1f : -1f;   // a left turn has its inside on the left: the right side (positive lateral) is the high one
				for (int q = k; q < e; q++)
				{
					bank[q] = sign * BankTan * Mathf.Sin(Mathf.PI * (q - k + 0.5f) / (e - k));
				}
				k = e;
			}
		}

		private static Vector3 At(List<Pt> p, int i, bool loop, float lateral, float lift)
		{
			float tilt = (loop && bank != null && i < bank.Length) ? lateral * bank[i] : 0f;
			return p[i].P + Data.Right(p, i, loop) * lateral + Vector3.up * (lift + tilt);
		}

		/// <summary>The foot of a road's slope: where it meets the ground (on a banked bend the ground is well below the raised edge).</summary>
		private static Vector3 Foot(List<Pt> p, int i, bool loop, float lateral)
		{
			Vector3 v = At(p, i, loop, lateral, -Drop);
			if (loop && bank != null && i < bank.Length && Mathf.Abs(bank[i]) > 0.001f)
			{
				v.y = Mathf.Min(v.y, Data.HeightAt(v.x, v.z) + 0.1f);
			}
			return v;
		}

		private static float Noise(Vector3 q)
		{
			unchecked
			{
				int x = Mathf.RoundToInt(q.x * 0.5f), z = Mathf.RoundToInt(q.z * 0.5f);
				int h = x * 374761393 + z * 668265263 + 1274126177;
				h = (h ^ (h >> 13)) * 1274126177;
				h ^= h >> 16;
				return (h & 0xFFFF) / 65535f;
			}
		}

		private void Segment(List<Pt> p, int i, int j, bool loop, float dist, bool branch)
		{
			Pt a = p[i];
			float wa = a.W * 0.5f, wb = p[j].W * 0.5f;
			Vector3 up = Vector3.up;

			if (a.Surface == Data.Cave)
			{
				CaveFloor(p, i, j, loop, wa, wb, branch ? shortFloor : caveFloor, dist);
				return;
			}

			bool isDirt = a.Surface == Data.Dirt;
			bool isGravel = a.Surface == Data.Gravel;
			Batch top, slope;
			if (isGravel)
			{
				top = gravel;
				slope = gravelSkirt;
			}
			else if (isDirt)
			{
				// just after the temple the gravel thins out into the dirt road: a few segments of a gravel/dirt mix
				int since = 99;
				for (int k = 1; k <= 7; k++)
				{
					if (p[((i - k) % p.Count + p.Count) % p.Count].Surface == Data.Gravel)
					{
						since = k;
						break;
					}
				}
				if (since <= 7)
				{
					top = since <= 2 ? blend[0] : (i % 3 == 0 ? blend[0] : blend[since <= 5 ? 1 : 2]);
				}
				else
				{
					top = dirt;
				}
				slope = dirtSkirt;
			}
			else
			{
				top = a.Hairpin ? asphaltHp : asphalt;
				slope = skirtAsphalt;
			}
			top.Quad(At(p, i, loop, -wa, 0f), At(p, i, loop, wa, 0f), At(p, j, loop, wb, 0f), At(p, j, loop, -wb, 0f), up, isGravel || isDirt ? 38f : 36f);
			slope.Quad(At(p, i, loop, wa, 0f), Foot(p, i, loop, wa + Shoulder), Foot(p, j, loop, wb + Shoulder), At(p, j, loop, wb, 0f), up, 36f);
			slope.Quad(At(p, i, loop, -wa, 0f), Foot(p, i, loop, -wa - Shoulder), Foot(p, j, loop, -wb - Shoulder), At(p, j, loop, -wb, 0f), up, 36f);

			if (isGravel)
			{
				// a row of rough stones along each edge of the path
				if (i % 2 == 0)
				{
					Vector3 f = Data.Forward(p, i, loop);
					Quaternion rot = Quaternion.LookRotation(f, up);
					foreach (float side in new[] { -1f, 1f })
					{
						Vector3 pos = At(p, i, loop, side * (wa + 1.6f), 0.1f);
						edgeStones.Box(pos, new Vector3(2.8f + Noise(pos) * 1.6f, 1.3f, 3.4f + Noise(pos + Vector3.one * 7f) * 2f), rot, 6f);
					}
				}
				return;
			}
			if (isDirt)
			{
				float rl = 5f;
				foreach (float side in new[] { -0.22f, 0.22f })
				{
					rut.Quad(At(p, i, loop, side * a.W - rl * 0.5f, Lift), At(p, i, loop, side * a.W + rl * 0.5f, Lift), At(p, j, loop, side * p[j].W + rl * 0.5f, Lift), At(p, j, loop, side * p[j].W - rl * 0.5f, Lift), up, 24f);
				}
				return;
			}

			// edge lines
			float edge = wa - 3.4f, edgeB = wb - 3.4f, lw = 0.7f;
			foreach (float side in new[] { -1f, 1f })
			{
				white.Quad(At(p, i, loop, side * edge - lw, Lift), At(p, i, loop, side * edge + lw, Lift), At(p, j, loop, side * edgeB + lw, Lift), At(p, j, loop, side * edgeB - lw, Lift), up, 8f);
			}
			// dashed centre line: 9 on, 13 off
			float d = dist % 22f;
			if (d < 9f)
			{
				yellow.Quad(At(p, i, loop, -0.5f, Lift), At(p, i, loop, 0.5f, Lift), At(p, j, loop, 0.5f, Lift), At(p, j, loop, -0.5f, Lift), up, 8f);
			}

			// the red and white hairpin paint, on the inside of the bend (like the Irohazaka stripes): v runs along the road
			if (a.Hairpin)
			{
				float turn = Data.Turn(p, i, loop) + Data.Turn(p, Mathf.Min(i + 1, p.Count - 1), loop);
				float innerSign = turn > 0f ? -1f : 1f;   // a left turn has its inside on the left (negative lateral)
				float seg = (p[j].P - a.P).magnitude;
				float v0 = dist / 16f, v1 = (dist + seg) / 16f;
				float e0 = innerSign * wa, e1 = innerSign * (wa - a.W * 0.46f);
				float f0 = innerSign * wb, f1 = innerSign * (wb - p[j].W * 0.46f);
				stripes.Quad(At(p, i, loop, e0, Lift * 1.5f), At(p, i, loop, e1, Lift * 1.5f), At(p, j, loop, f1, Lift * 1.5f), At(p, j, loop, f0, Lift * 1.5f), up,
					new Vector2(0f, v0), new Vector2(1f, v0), new Vector2(1f, v1), new Vector2(0f, v1));
				float o0 = -innerSign * wa, o1 = -innerSign * (wa - a.W * 0.16f);
				float q0 = -innerSign * wb, q1 = -innerSign * (wb - p[j].W * 0.16f);
				redPaint.Quad(At(p, i, loop, o0, Lift * 1.5f), At(p, i, loop, o1, Lift * 1.5f), At(p, j, loop, q1, Lift * 1.5f), At(p, j, loop, q0, Lift * 1.5f), up, 16f);
			}
		}

		/// <summary>
		/// The cave floor, from wall foot to wall foot, in lateral bands that are pushed up and down a little by noise (it is a cave, not a
		/// track), with puddles on it. The edges stay level so it meets the walls.
		/// </summary>
		private void CaveFloor(List<Pt> p, int i, int j, bool loop, float wa, float wb, Batch target, float dist)
		{
			const int bands = 8;
			bool shortWay = target == shortFloor;
			float sh = shortWay ? 4f : CaveShoulder;
			float ea = wa + sh, eb = wb + sh;
			float ends = shortWay ? ShortWayFade(p[i].P) : 1f;
			float amp = shortWay ? 0.55f * ends : 0.12f * MouthFade(i);   // the long way is smooth and starts perfectly flat at each mouth (no lip where the dirt ends); the short one stays rough, except where it joins the long road
			for (int k = 0; k < bands; k++)
			{
				float ua = -1f + 2f * k / bands, ub = -1f + 2f * (k + 1) / bands;
				Vector3 a0 = Wavy(p, i, loop, ua * ea, amp, ea), a1 = Wavy(p, i, loop, ub * ea, amp, ea), b1 = Wavy(p, j, loop, ub * eb, amp, eb), b0 = Wavy(p, j, loop, ua * eb, amp, eb);
				if (shortWay)
				{
					// the short floor lies over the long road's floor where the two overlap: a quad wholly inside it is left out, every other one
					// stays whole and at its own height (a quad moved down a little leaves a hairline crack against its neighbours, which the seal check found)
					int inside = (InMainFloor(a0) ? 1 : 0) + (InMainFloor(a1) ? 1 : 0) + (InMainFloor(b1) ? 1 : 0) + (InMainFloor(b0) ? 1 : 0);
					if (inside == 4)
					{
						continue;
					}
				}
				target.Quad(a0, a1, b1, b0, Vector3.up, 26f);
			}
			// a puddle now and then, off the very middle of the road more often than on it
			Vector3 q = p[i].P;
			// no puddles in the halls where the two roads meet (a large black patch there read as a hole in the floor)
			bool inHall = (p[i].P - Data.Main[Data.Index("fork")].P).sqrMagnitude < 90f * 90f || (p[i].P - Data.Main[Data.Index("join")].P).sqrMagnitude < 90f * 90f;
			if (!inHall && Noise(q * 1.7f) > 0.78f)
			{
				float lat = (Noise(q + Vector3.one * 13f) - 0.5f) * 2f * ea * 0.85f;
				float rad = 1.8f + Noise(q + Vector3.one * 29f) * 3.4f;
				Vector3 c = Wavy(p, i, loop, lat, shortWay ? 0.55f : 0.12f, ea) + Vector3.up * 0.07f;
				int sides = 9;
				for (int s = 0; s < sides; s++)
				{
					float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
					float r0 = rad * (0.7f + 0.5f * Noise(c + new Vector3(s, 0f, 0f))), r1 = rad * (0.7f + 0.5f * Noise(c + new Vector3(s + 1, 0f, 0f)));
					puddles.Tri(c, c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r0, c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r1, Vector3.up, 8f);
				}
			}
		}

		private static Vector3 Wavy(List<Pt> p, int i, bool loop, float lateral, float amp = 0.55f, float limit = -1f)
		{
			Vector3 pos = p[i].P + Data.Right(p, i, loop) * lateral;
			float edge = Mathf.Clamp01(1f - Mathf.Abs(lateral) / (limit > 0f ? limit : p[i].W * 0.5f + CaveShoulder));   // the wave dies out exactly at the wall foot: no slit between floor and wall
			float wave = (Mathf.Sin(pos.x * 0.07f + pos.z * 0.05f) * 0.5f + (SoftNoise(pos) - 0.5f)) * amp;
			return pos + Vector3.up * (wave * Mathf.Min(1f, edge * 6f));
		}

		private struct FloorSample
		{
			public Vector3 P, R;
			public float Half;
		}

		private readonly List<FloorSample> mainFloor = new List<FloorSample>();

		/// <summary>The long road's cave floor as samples every 4 units (position, right, half width wall foot to wall foot).</summary>
		private void DensifyMainFloor(List<Pt> p, int from, int to)
		{
			mainFloor.Clear();
			for (int q = from; q < to; q++)
			{
				Pt a = p[q], b = p[(q + 1) % p.Count];
				int steps = Mathf.Max(1, Mathf.RoundToInt((b.P - a.P).magnitude / 4f));
				for (int s = 0; s < steps; s++)
				{
					float u = s / (float)steps;
					FloorSample f = new FloorSample();
					f.P = Vector3.Lerp(a.P, b.P, u);
					f.R = Vector3.Lerp(Data.Right(p, q, true), Data.Right(p, (q + 1) % p.Count, true), u).normalized;
					f.Half = Mathf.Lerp(a.W, b.W, u) * 0.5f + CaveShoulder;
					mainFloor.Add(f);
				}
			}
		}

		/// <summary>Is a point on the long road's cave floor (between its wall feet, at about its height)?</summary>
		private bool InMainFloor(Vector3 q)
		{
			float best = 1e9f;
			int bi = -1;
			for (int k = 0; k < mainFloor.Count; k++)
			{
				float dx = mainFloor[k].P.x - q.x, dz = mainFloor[k].P.z - q.z;
				float d = dx * dx + dz * dz;
				if (d < best)
				{
					best = d;
					bi = k;
				}
			}
			if (bi < 0 || best > 40f * 40f)
			{
				return false;
			}
			FloorSample m = mainFloor[bi];
			Vector3 f = new Vector3(-m.R.z, 0f, m.R.x);
			Vector3 d3 = q - m.P;
			if (Mathf.Abs(Vector3.Dot(d3, f)) > 4.5f || (bi == 0 && Vector3.Dot(d3, f) < 0f) || (bi == mainFloor.Count - 1 && Vector3.Dot(d3, f) > 0f))
			{
				return false;
			}
			return Mathf.Abs(Vector3.Dot(d3, m.R)) < m.Half - 1.5f && Mathf.Abs(d3.y) < 3f;
		}

		/// <summary>0 while the short way is within 20 of the long road's axis, 1 from 36: its rough floor does not start until the two floors have parted.</summary>
		private float ShortWayFade(Vector3 q)
		{
			float best = 1e9f;
			for (int k = 0; k < mainFloor.Count; k++)
			{
				float dx = mainFloor[k].P.x - q.x, dz = mainFloor[k].P.z - q.z;
				best = Mathf.Min(best, dx * dx + dz * dz);
			}
			float t = Mathf.Clamp01((Mathf.Sqrt(best) - 20f) / 16f);
			return t * t * (3f - 2f * t);
		}

		/// <summary>0 at both ends of the short way (the fork and the join), 1 from 3 points in: its rough floor eases in from the long road's floor.</summary>
		private static float ShortFade(int i, int count)
		{
			float t = Mathf.Clamp01(Mathf.Min(i, count - 2 - i) / 3f);
			return t * t * (3f - 2f * t);
		}
		/// <summary>0 at the first and last route point of the cave, 1 from 4 route points in: the floor's roll eases in from the mouth.</summary>
		private static float MouthFade(int i)
		{
			int a = Data.Index("cave_in"), b = Data.Index("cave_out");
			float d = Mathf.Min(i - a, b - i);
			float t = Mathf.Clamp01(d / 4f);
			return t * t * (3f - 2f * t);
		}

		/// <summary>Value noise with smooth transitions over 6 units: the floor rolls instead of stepping every 2 units.</summary>
		private static float SoftNoise(Vector3 q)
		{
			float u = q.x / 6f, v = q.z / 6f;
			int ix = Mathf.FloorToInt(u), iz = Mathf.FloorToInt(v);
			float fx = u - ix, fz = v - iz;
			fx = fx * fx * (3f - 2f * fx);
			fz = fz * fz * (3f - 2f * fz);
			float a = Noise(new Vector3(ix * 2f, 0f, iz * 2f)), b = Noise(new Vector3((ix + 1) * 2f, 0f, iz * 2f));
			float c = Noise(new Vector3(ix * 2f, 0f, (iz + 1) * 2f)), d = Noise(new Vector3((ix + 1) * 2f, 0f, (iz + 1) * 2f));
			return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
		}

		/// <summary>A band of stone paving where the asphalt ends and the temple path begins.</summary>
		private void Threshold(List<Pt> p)
		{
			int n = p.Count;
			for (int i = 0; i < n; i++)
			{
				int prev = (i - 1 + n) % n;
				if (p[i].Surface != Data.Gravel || p[prev].Surface == Data.Gravel)
				{
					continue;
				}
				for (int k = 0; k < 2; k++)
				{
					int a = (i + k) % n, b = (i + k + 1) % n;
					float wa = p[a].W * 0.5f, wb = p[b].W * 0.5f;
					paving.Quad(At(p, a, true, -wa - 3f, Lift), At(p, a, true, wa + 3f, Lift), At(p, b, true, wb + 3f, Lift), At(p, b, true, -wb - 3f, Lift), Vector3.up, 12f);
				}
			}
		}

		/// <summary>Steel guardrail pieces (the same rail as Highway's) on the outside of every bend of the touge, the valley turn and the ridge road.</summary>
		private void Guardrails(List<Pt> p)
		{
			int n = p.Count;
			List<Vector3> run = new List<Vector3>();
			float side = 0f;
			for (int i = 0; i <= n; i++)
			{
				int k = i % n;
				Pt a = p[k];
				float outSide = 0f;
				if (i < n && a.Surface == Data.Asphalt && (a.Section == Data.SecTouge || a.Section == Data.SecRidge || a.Section == Data.SecValley))
				{
					float t = Data.Turn(p, k, true);
					if (Mathf.Abs(t) >= 0.014f)
					{
						outSide = t > 0f ? 1f : -1f;   // the outside of a left turn is the right
					}
				}
				if (outSide != 0f && (side == 0f || outSide == side))
				{
					side = outSide;
					run.Add(At(p, k, true, side * (a.W * 0.5f + 2.2f), 0f));
					continue;
				}
				if (run.Count >= 2)
				{
					AddRun(run, side);
				}
				run.Clear();
				side = 0f;
				if (outSide != 0f)
				{
					side = outSide;
					run.Add(At(p, k, true, side * (a.W * 0.5f + 2.2f), 0f));
				}
			}
		}

		private void AddRun(List<Vector3> run, float side)
		{
			float total = 0f;
			float[] cum = new float[run.Count];
			for (int i = 1; i < run.Count; i++)
			{
				Vector3 d = run[i] - run[i - 1];
				d.y = 0f;
				total += d.magnitude;
				cum[i] = total;
			}
			int count = Mathf.Max(1, Mathf.RoundToInt(total / 8f));
			float len = total / count;
			railChunk++;
			railInChunk = 0;
			int seg = 0;
			for (int k = 0; k < count; k++)
			{
				float s = (k + 0.5f) * len;
				while (seg + 2 < run.Count && cum[seg + 1] < s)
				{
					seg++;
				}
				float span = Mathf.Max(1e-3f, cum[seg + 1] - cum[seg]);
				float t = Mathf.Clamp01((s - cum[seg]) / span);
				Vector3 pos = Vector3.Lerp(run[seg], run[seg + 1], t);
				Vector3 along = run[seg + 1] - run[seg];
				along.y = 0f;
				if (along.sqrMagnitude < 1e-6f)
				{
					continue;
				}
				along.Normalize();
				Vector3 right = new Vector3(along.z, 0f, -along.x);
				if (railInChunk >= 16)
				{
					railChunk++;
					railInChunk = 0;
				}
				railInChunk++;
				Rails.Add(new RailPiece { Pos = pos, Along = along, Out = right * side, Len = len, Chunk = railChunk });
			}
		}

		/// <summary>A chequered line across the road at point 0, fourteen squares by two.</summary>
		private void StartLine(List<Pt> p)
		{
			Vector3 right = Data.Right(p, 0, true);
			Vector3 fwd = Data.Forward(p, 0, true);
			float w = p[0].W;
			int cols = 16;
			float cw = w / cols, ch = 3.2f;
			for (int row = 0; row < 2; row++)
			{
				for (int c = 0; c < cols; c++)
				{
					Vector3 a = p[0].P + right * (-w * 0.5f + c * cw) + fwd * (row * ch) + Vector3.up * Lift * 1.5f;
					Batch b = (c + row) % 2 == 0 ? chequerWhite : chequerBlack;
					b.Quad(a, a + right * cw, a + right * cw + fwd * ch, a + fwd * ch, Vector3.up, 8f);
				}
			}
		}
	}
}
