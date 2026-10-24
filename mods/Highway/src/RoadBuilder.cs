using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripHighway
{
	/// <summary>Turns the plan into batches of triangles: two highways, a bridge, two ramps, walls, median, dead ends, signs and tree rows.</summary>
	internal sealed partial class RoadBuilder
	{
		public readonly Batch Asphalt = new Batch("asphalt", new Color(0.17f, 0.17f, 0.18f), true);
		public readonly Batch Deck = new Batch("deck", new Color(0.55f, 0.55f, 0.53f), true);
		public readonly Batch Barrier = new Batch("barrier", new Color(0.64f, 0.64f, 0.62f), true, wall: true);
		public readonly Batch Slope = new Batch("slope", new Color(0.30f, 0.46f, 0.20f), true);
		public readonly Batch Field = new Batch("field", new Color(0.33f, 0.50f, 0.22f), true);
		public readonly Batch Median = new Batch("median", new Color(0.36f, 0.43f, 0.30f), false);
		public readonly Batch White = new Batch("white", new Color(0.92f, 0.92f, 0.90f), false);
		public readonly Batch Yellow = new Batch("yellow", new Color(0.90f, 0.72f, 0.10f), false);
		public readonly Batch Black = new Batch("black", new Color(0.05f, 0.05f, 0.05f), false);
		public readonly Batch Dirt = new Batch("dirt", new Color(0.47f, 0.35f, 0.22f), true);
		public readonly Batch DirtSlope = new Batch("dirt slope", new Color(0.40f, 0.30f, 0.19f), true);
		public readonly Batch DirtTrack = new Batch("dirt track", new Color(0.33f, 0.24f, 0.15f), false);
		public readonly Batch Gantry = new Batch("gantry", new Color(0.36f, 0.37f, 0.40f), true, wall: true);
		public readonly Batch Board = new Batch("board", new Color(0.04f, 0.38f, 0.20f), false);

		public IEnumerable<Batch> All => new[] { Asphalt, Deck, Barrier, Slope, Field, Median, White, Yellow, Black, Gantry, Board, MachYellow, MachDark, MachGrey, MachGlass, MachOrange, Spoil, Trench, Dirt, DirtSlope, DirtTrack };

		private static readonly Vector2[] JerseyProfile =
		{
			new Vector2(-0.6f, -0.1f), new Vector2(-0.6f, 0.25f), new Vector2(-0.28f, 0.85f), new Vector2(-0.2f, 1.1f),
			new Vector2(0.2f, 1.1f), new Vector2(0.28f, 0.85f), new Vector2(0.6f, 0.25f), new Vector2(0.6f, -0.1f)
		};

		/// <summary>The median is a tall concrete wall: 2.3 m, wider at the foot.</summary>
		private static readonly Vector2[] MedianProfile =
		{
			new Vector2(-0.7f, -0.1f), new Vector2(-0.7f, 0.5f), new Vector2(-0.3f, 2.3f),
			new Vector2(0.3f, 2.3f), new Vector2(0.7f, 0.5f), new Vector2(0.7f, -0.1f)
		};

		private static readonly Vector2[] WallProfile =
		{
			new Vector2(-0.4f, -0.5f), new Vector2(-0.4f, Plan.WallHeight), new Vector2(0.4f, Plan.WallHeight), new Vector2(0.4f, -0.5f)
		};

		private static readonly Vector2[] BackstopProfile =
		{
			new Vector2(-0.8f, -0.5f), new Vector2(-0.8f, 3.5f), new Vector2(0.8f, 3.5f), new Vector2(0.8f, -0.5f)
		};

		private readonly List<Vector3> rampPoints = new List<Vector3>();
		private int realRampCount;   // the first this many rampPoints belong to the two paved ramps
		private Route route;

		public int Gaps = 4;
		public float GapLength = 120f;

		public void Build(Route r)
		{
			route = r;
			AddRampPoints(r.NorthEast);
			AddRampPoints(r.SouthWest);
			realRampCount = rampPoints.Count;
			finishedLines.Add(r.NorthEast);
			finishedLines.Add(r.SouthWest);
			AddRampPoints(r.DirtEast);
			AddRampPoints(r.DirtNorth);
			dirtPoints.AddRange(r.DirtEast);
			dirtPoints.AddRange(r.DirtNorth);

			Highway(Vector3.zero, Vector3.right, bridge: true);
			Highway(new Vector3(0f, Plan.Low, 0f), Vector3.back, bridge: false);
			Ramp(r.NorthEast);
			Ramp(r.SouthWest);
			DirtRoad(r.DirtEast);
			DirtRoad(r.DirtNorth);
			ExitCones(r);
			StartLine();
			GroundPlane();
			Forest();
		}

		private void AddRampPoints(List<Vector3> pts)
		{
			for (int i = 0; i < pts.Count; i += 2)
			{
				rampPoints.Add(pts[i]);
			}
		}

		// ---- where things are -------------------------------------------------------------------------------------

		/// <summary>Inside the footprint of either highway.</summary>
		private static bool InDeck(Vector3 p)
		{
			return (Mathf.Abs(p.z) < Plan.HalfWidth + 0.5f && Mathf.Abs(p.x) < Plan.Reach + 0.5f)
				|| (Mathf.Abs(p.x) < Plan.HalfWidth + 0.5f && Mathf.Abs(p.z) < Plan.Reach + 0.5f);
		}

		/// <summary>Over the body of a ramp, so a highway wall has to open here.</summary>
		private bool OnRamp(Vector3 p)
		{
			// A paved ramp opens the highway's guardrail within its width; a dirt road (the same width) within its width and the guardrail's offset.
			float paved = (Plan.RaceHalf - 1f) * (Plan.RaceHalf - 1f);
			float dirt = (Plan.RaceHalf + 2f) * (Plan.RaceHalf + 2f);
			for (int i = 0; i < rampPoints.Count; i++)
			{
				float dx = rampPoints[i].x - p.x;
				float dz = rampPoints[i].z - p.z;
				if (dx * dx + dz * dz < (i < realRampCount ? paved : dirt))
				{
					return true;
				}
			}
			return false;
		}

		public bool ClearOfRoads(float x, float z, float margin)
		{
			if (Mathf.Abs(z) < Plan.HalfWidth + margin && Mathf.Abs(x) < Plan.Reach + margin)
			{
				return false;
			}
			if (Mathf.Abs(x) < Plan.HalfWidth + margin && Mathf.Abs(z) < Plan.Reach + margin)
			{
				return false;
			}
			float limit = (Plan.RaceHalf + margin) * (Plan.RaceHalf + margin);
			foreach (Vector3 r in rampPoints)
			{
				float dx = r.x - x;
				float dz = r.z - z;
				if (dx * dx + dz * dz < limit)
				{
					return false;
				}
			}
			return true;
		}

		// ---- surfaces ---------------------------------------------------------------------------------------------

		/// <summary>
		/// A road slab. With a skirt its sides run down at 1:2 to the ground (an embankment); without, it is a thin deck with a fascia.
		/// </summary>
		private void Slab(IList<Vector3> p, float halfWidth, float lift, bool skirt, float thick, bool capStart, bool capEnd, Batch top = null, Batch slopeBatch = null, bool drawTop = true, Func<Vector3, bool> skipSkirt = null)
		{
			Batch slopes = slopeBatch ?? Slope;
			if (drawTop)
			{
				Geo.Strip(top ?? Asphalt, p, -halfWidth, halfWidth, lift);
			}
			if (!skirt)
			{
				// The underside faces down, or the deck is see-through from below.
				Geo.Strip(Deck, p, -halfWidth, halfWidth, lift - thick, null, facesDown: true);
			}
			for (int side = -1; side <= 1; side += 2)
			{
				for (int i = 0; i + 1 < p.Count; i++)
				{
					Vector3 e0 = Geo.At(p, i, side * halfWidth, lift);
					Vector3 e1 = Geo.At(p, i + 1, side * halfWidth, lift);
					Vector3 outward = Geo.Right(p, i) * side;
					if (skirt)
					{
						float h0 = e0.y - Plan.Ground;
						float h1 = e1.y - Plan.Ground;
						if ((h0 < 0.3f && h1 < 0.3f) || (skipSkirt != null && skipSkirt((e0 + e1) * 0.5f)))
						{
							continue;
						}
						Vector3 o0 = Geo.At(p, i, side * (halfWidth + 2f * h0), 0f);
						Vector3 o1 = Geo.At(p, i + 1, side * (halfWidth + 2f * h1), 0f);
						o0.y = Plan.Ground;
						o1.y = Plan.Ground;
						slopes.Quad(e0, e1, o1, o0, outward + Vector3.up);
					}
					else
					{
						Deck.Quad(e0, e1, e1 + Vector3.down * thick, e0 + Vector3.down * thick, outward);
					}
				}
			}
			if (skirt)
			{
				if (capStart)
				{
					Cap(p, 0, halfWidth, lift, -1f);
				}
				if (capEnd)
				{
					Cap(p, p.Count - 1, halfWidth, lift, 1f);
				}
			}
		}

		private void Cap(IList<Vector3> p, int i, float halfWidth, float lift, float dir)
		{
			float h = p[i].y + lift - Plan.Ground;
			if (h < 0.3f)
			{
				return;
			}
			Vector3 forward = Geo.Forward(p, i) * dir;
			Vector3 a = Geo.At(p, i, -halfWidth - 2f * h, 0f);
			Vector3 b = Geo.At(p, i, -halfWidth, lift);
			Vector3 c = Geo.At(p, i, halfWidth, lift);
			Vector3 d = Geo.At(p, i, halfWidth + 2f * h, 0f);
			a.y = Plan.Ground;
			d.y = Plan.Ground;
			Deck.Quad(a, b, c, d, forward);
		}

		// ---- the two highways -------------------------------------------------------------------------------------

		/// <summary>
		/// A highway along <paramref name="dir"/> through <paramref name="origin"/>. Positive lateral (right of travel) is the racing
		/// carriageway, negative the opposite one. The median stops short of each end, leaving an open plaza that the ramps use.
		/// </summary>
		private void Highway(Vector3 origin, Vector3 dir, bool bridge)
		{
			List<Vector3> Along(float s0, float s1, float step) => Geo.Straight(origin, dir, s0, s1, step);

			if (bridge)
			{
				Slab(Along(-Plan.Reach, -Plan.BridgeHalf, 20f), Plan.HalfWidth, 0f, true, 0f, true, true);
				Slab(Along(-Plan.BridgeHalf, Plan.BridgeHalf, 15f), Plan.HalfWidth, 0f, false, 1.6f, false, false);
				Slab(Along(Plan.BridgeHalf, Plan.Reach, 20f), Plan.HalfWidth, 0f, true, 0f, true, true);
			}
			else
			{
				Slab(Along(-Plan.Reach, Plan.Reach, 20f), Plan.HalfWidth, 0f, false, 1f, false, false);
			}

			// The highway goes on past the plazas to the far ends: the median and the markings stop only for the plazas (620 to 940 m out).
			float[] centres = GapCentres();
			float[,] spans = { { -Plan.Reach, -Plan.PlazaEnd }, { -Plan.MedianEnds, Plan.MedianEnds }, { Plan.PlazaEnd, Plan.Reach } };
			for (int k = 0; k < 3; k++)
			{
				List<Vector3> body = Along(spans[k, 0], spans[k, 1], 20f);
				Geo.Strip(Median, body, -Plan.MedianHalf, Plan.MedianHalf, 0.03f);
				for (int side = -1; side <= 1; side += 2)
				{
					Geo.Line(White, body, side * (Plan.MedianHalf + Plan.Inner + Plan.Lane), 0.3f, 0.04f, 12f, 16f);
					Geo.Line(White, body, side * (Plan.HalfWidth - Plan.Outer), 0.35f, 0.04f, 1f, 0f);
					Geo.Line(Yellow, body, side * (Plan.MedianHalf + Plan.Inner), 0.35f, 0.04f, 1f, 0f);
				}
				foreach (int side in new[] { -1, 1 })
				{
					MedianSpan(origin + new Vector3(dir.z, 0f, -dir.x) * (side * (Plan.MedianHalf - 0.7f)), dir, spans[k, 0], spans[k, 1], centres);
				}
			}
			foreach (float c in centres)
			{
				GapSigns(origin, dir, c);
			}

			// Outer edges, open where a ramp leaves or joins: concrete on the bridge span, steel guardrail everywhere else.
			List<Vector3> edge = Along(-Plan.Reach, Plan.Reach, 5f);
			Func<Vector3, bool> onBridge = p => bridge && Mathf.Abs(Vector3.Dot(p - origin, dir)) <= Plan.BridgeHalf + 3f;
			for (int side = -1; side <= 1; side += 2)
			{
				foreach (List<Vector3> run in Runs(edge, side * (Plan.HalfWidth + 0.4f), p => OnRamp(p) || !onBridge(p), 0f))
				{
					Geo.Extrude(Barrier, run, WallProfile);
				}
				foreach (List<Vector3> run in Runs(edge, side * (Plan.HalfWidth + 1.0f), p => OnRamp(p) || onBridge(p), 0f))
				{
					SteelRun(run, side);
				}
			}

			// The far ends are out of sight, shut off by blocks so no one drives out of the map.
			for (int end = -1; end <= 1; end += 2)
			{
				DeadEnd(origin, dir, end);
			}
			HighwaySigns(origin, dir, bridge, bridge ? "CLUJ-NAPOCA" : "SIBIU", bridge ? "ARAD" : "ORADEA", bridge ? "120 km" : "95 km");
			if (!bridge)
			{
				WorkZone(origin, dir);
			}
		}

		private float[] GapCentres()
		{
			int half = Mathf.Max(0, Gaps / 2);
			List<float> list = new List<float>();
			for (int k = 0; k < half; k++)
			{
				float pos = half == 1 ? 220f : Mathf.Lerp(325f, 122f, k / (float)(half - 1));
				list.Add(pos);
				list.Add(-pos);
			}
			if (half > 0)
			{
				float far = (Plan.PlazaEnd + Plan.Reach) * 0.5f;
				list.Add(far);
				list.Add(-far);
			}
			list.Sort();
			return list.ToArray();
		}

		/// <summary>A stretch of the median wall from <paramref name="a"/> to <paramref name="b"/> with a gap at each of <paramref name="centres"/> that falls inside it.</summary>
		private void MedianSpan(Vector3 edgeOrigin, Vector3 dir, float a, float b, float[] centres)
		{
			float cursor = a;
			foreach (float c in centres)
			{
				if (c - GapLength * 0.5f > a && c + GapLength * 0.5f < b)
				{
					MedianRun(edgeOrigin, dir, cursor, c - GapLength * 0.5f);
					cursor = c + GapLength * 0.5f;
				}
			}
			MedianRun(edgeOrigin, dir, cursor, b);
		}

		private void MedianRun(Vector3 origin, Vector3 dir, float s0, float s1)
		{
			if (s1 - s0 < 2f)
			{
				return;
			}
			Geo.Extrude(Barrier, Geo.Straight(origin, dir, s0, s1, 10f), MedianProfile);
		}

		/// <summary>The stretches of an offset line that are not cut.</summary>
		private List<List<Vector3>> Runs(List<Vector3> line, float lateral, Func<Vector3, bool> cull, float lift)
		{
			List<List<Vector3>> runs = new List<List<Vector3>>();
			List<Vector3> run = new List<Vector3>();
			for (int i = 0; i < line.Count; i++)
			{
				Vector3 p = Geo.At(line, i, lateral, lift);
				if (cull(p))
				{
					if (run.Count >= 2)
					{
						runs.Add(run);
					}
					run = new List<Vector3>();
					continue;
				}
				run.Add(p);
			}
			if (run.Count >= 2)
			{
				runs.Add(run);
			}
			return runs;
		}

		/// <summary>Two rows of blocks and a tall backstop across every lane, at the far end where nobody goes.</summary>
		private void DeadEnd(Vector3 origin, Vector3 dir, int end)
		{
			Vector3 across = new Vector3(dir.z, 0f, -dir.x);
			Vector3 at(float s, float lat) => origin + dir * (end * s) + across * lat;

			foreach (float s in new[] { Plan.Reach - 1.5f, Plan.Reach - 4.5f })
			{
				Geo.Extrude(Barrier, Geo.Straight(at(s, -Plan.HalfWidth), across, 0f, 2f * Plan.HalfWidth, 10f), JerseyProfile);
			}
			Geo.Extrude(Barrier, Geo.Straight(at(Plan.Reach + 1.5f, -Plan.HalfWidth), across, 0f, 2f * Plan.HalfWidth, 10f), BackstopProfile);
		}

		// ---- ramps ------------------------------------------------------------------------------------------------

		private void Ramp(List<Vector3> p)
		{
			if (p.Count < 2)
			{
				return;
			}
			Slab(p, Plan.RaceHalf, Plan.RampLift, true, 0.8f, false, false);
			Func<Vector3, bool> inPlaza = q => InDeck(q);
			Geo.Line(White, p, 0f, 0.3f, 0.06f, 12f, 16f, inPlaza);
			Geo.Line(White, p, Plan.RaceHalf - 1.5f, 0.35f, 0.06f, 1f, 0f, inPlaza);
			Geo.Line(White, p, -(Plan.RaceHalf - 1.5f), 0.35f, 0.06f, 1f, 0f, inPlaza);
			for (int side = -1; side <= 1; side += 2)
			{
				foreach (List<Vector3> run in Runs(p, side * (Plan.RaceHalf + 1.0f), q => InDeck(q) || NearDirt(q), Plan.RampLift))
				{
					SteelRun(run, side);
				}
			}
			RampSigns(p);
		}

		// ---- start line, ground ----------------------------------------------------------------------------------

		private void StartLine()
		{
			float x = Route.StartX;
			float z0 = -Plan.HalfWidth + 0.4f;
			float width = Plan.HalfWidth - Plan.MedianHalf - 0.8f;
			int cols = Mathf.RoundToInt(width / 1.8f);
			float cell = width / cols;
			for (int col = 0; col < cols; col++)
			{
				for (int row = 0; row < 2; row++)
				{
					Batch b = (col + row) % 2 == 0 ? White : Black;
					float za = z0 + col * cell;
					float xa = x - 1.5f + row * 1.5f;
					b.Quad(new Vector3(xa, 0.05f, za), new Vector3(xa + 1.5f, 0.05f, za), new Vector3(xa + 1.5f, 0.05f, za + cell), new Vector3(xa, 0.05f, za + cell), Vector3.up);
				}
			}
		}

		private void GroundPlane()
		{
			float r = 5200f;
			float y = Plan.Ground;
			Field.Quad(new Vector3(-r, y, -r), new Vector3(r, y, -r), new Vector3(r, y, r), new Vector3(-r, y, r), Vector3.up);
		}
	}
}
