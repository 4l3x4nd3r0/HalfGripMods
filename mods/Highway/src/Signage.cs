using System.Collections.Generic;
using UnityEngine;

namespace HalfGripHighway
{
	/// <summary>Everything that stands on the road and breaks: signs, drums, steel guardrail. Built as <see cref="Prop"/>s, made real by <see cref="HighwayScene"/>.</summary>
	internal sealed partial class RoadBuilder
	{
		public readonly List<Prop> Props = new List<Prop>();
		public readonly List<TreeSpot> Trees = new List<TreeSpot>();
		public readonly List<RailPiece> Rails = new List<RailPiece>();
		public readonly List<Label> Labels = new List<Label>();
		private int railChunk;
		private int railInChunk;

		private static readonly Color RedC = new Color(0.78f, 0.07f, 0.07f);
		private static readonly Color WhiteC = new Color(0.93f, 0.93f, 0.90f);
		private static readonly Color GreenC = new Color(0.04f, 0.38f, 0.20f);
		private static readonly Color YellowC = new Color(0.92f, 0.74f, 0.08f);
		private static readonly Color OrangeC = new Color(0.95f, 0.42f, 0.05f);
		private static readonly Color DarkC = new Color(0.04f, 0.04f, 0.04f);
		private static readonly Color PoleC = new Color(0.36f, 0.37f, 0.40f);
		private static readonly Color SteelC = new Color(0.66f, 0.68f, 0.72f);
		private static readonly Color PaleC = new Color(0.95f, 0.95f, 0.92f);

		private Prop NewProp(string kind, Vector3 pos, Vector3 facing, float mass)
		{
			Prop p = new Prop { Kind = kind, Pos = pos, Rot = Quaternion.LookRotation(facing, Vector3.up), Mass = mass };
			Props.Add(p);
			return p;
		}

		/// <summary>A stake under a post, sunk 3 m into the ground, so a sign beside the road stands on the slope instead of floating over it.</summary>
		private static void Foot(Prop p, float x, float size)
		{
			p.Box(PoleC, new Vector3(x, -1.5f, 0f), new Vector3(0.3f, 3f, 0.3f));
		}

		// ---- steel guardrail --------------------------------------------------------------------------------------

		/// <summary>
		/// A stretch of guardrail along <paramref name="run"/>, cut into pieces of about 8 m. They are only recorded here: <see cref="RailField"/>
		/// draws them (merged, 16 pieces to a mesh), tests the cars against them and breaks the ones a car hits.
		/// </summary>
		private void SteelRun(List<Vector3> run, int side)
		{
			if (run.Count < 2)
			{
				return;
			}
			float[] cum = Geo.Cumulative(run);
			float total = cum[cum.Length - 1];
			int n = Mathf.Max(1, Mathf.RoundToInt(total / 8f));
			float len = total / n;
			railChunk++;
			railInChunk = 0;
			for (int k = 0; k < n; k++)
			{
				Geo.PointAt(run, cum, (k + 0.5f) * len, out Vector3 pos, out Vector3 right);
				if (railInChunk >= 16)
				{
					railChunk++;
					railInChunk = 0;
				}
				railInChunk++;
				Rails.Add(new RailPiece { Pos = pos, Along = new Vector3(-right.z, 0f, right.x), Out = right * side, Len = len, Chunk = railChunk });
			}
		}

		// ---- signs ------------------------------------------------------------------------------------------------

		/// <summary>The small ROAD CLOSED board in front of a dead end.</summary>
		private void ClosedBoard(Vector3 pos, Vector3 facing)
		{
			Prop p = NewProp("closed-small", pos, facing, 14f);
			p.Box(WhiteC, new Vector3(0f, 1.7f, 0f), new Vector3(3.2f, 1.0f, 0.12f));
			p.Box(OrangeC, new Vector3(0f, 2.14f, 0.07f), new Vector3(3.2f, 0.16f, 0.12f));
			p.Box(OrangeC, new Vector3(0f, 1.26f, 0.07f), new Vector3(3.2f, 0.16f, 0.12f));
			foreach (float x in new[] { -1.3f, 1.3f })
			{
				p.Box(DarkC, new Vector3(x, 0.6f, 0f), new Vector3(0.15f, 1.2f, 0.15f));
				p.Hit(new Vector3(x, 0.6f, 0f), new Vector3(0.2f, 1.2f, 0.2f));
				Foot(p, x, 0.6f);
			}
			p.Hit(new Vector3(0f, 1.7f, 0f), new Vector3(3.2f, 1.0f, 0.16f));
			p.Text("ROAD CLOSED", new Vector3(0f, 1.7f, 0.075f), new Vector2(2.9f, 0.8f), DarkC);
		}

		private void Drum(Vector3 pos)
		{
			const float k = 2.2f;
			Prop p = NewProp("drum", pos, Vector3.forward, 8f);
			p.Box(OrangeC, new Vector3(0f, 0.5f, 0f) * k, new Vector3(0.7f, 1.0f, 0.7f) * k);
			p.Box(WhiteC, new Vector3(0f, 0.62f, 0f) * k, new Vector3(0.72f, 0.18f, 0.72f) * k);
			p.Hit(new Vector3(0f, 0.5f, 0f) * k, new Vector3(0.7f, 1.0f, 0.7f) * k);
		}

		/// <summary>A speed-limit board on a post. Only its front carries the number, so the traffic it is meant for reads it and the other carriageway sees a plain back.</summary>
		private void SpeedSign(Vector3 basePos, Vector3 facing, string text)
		{
			Prop p = NewProp("speed", basePos, facing, 30f);
			p.Box(PoleC, new Vector3(0f, 1.7f, 0f), new Vector3(0.3f, 3.4f, 0.3f));
			p.Disc(PoleC, new Vector3(0f, 4.8f, -0.12f), 1.5f, 0.18f);
			p.Disc(RedC, new Vector3(0f, 4.8f, 0.05f), 1.5f, 0.16f);
			p.Disc(WhiteC, new Vector3(0f, 4.8f, 0.15f), 1.15f, 0.06f);
			p.Hit(new Vector3(0f, 1.7f, 0f), new Vector3(0.3f, 3.4f, 0.3f));
			p.Hit(new Vector3(0f, 4.8f, 0f), new Vector3(2.9f, 2.9f, 0.3f));
			Foot(p, 0f, 1.2f);
			p.Text(text, new Vector3(0f, 4.8f, 0.19f), new Vector2(2.1f, 1.6f), DarkC, 1);
		}

		/// <summary>
		/// An exit sign over one carriageway, as tall as the start lights: a pillar in the median, a pillar just outside the road, a beam and
		/// a big green board with a white edge, in the style of Romanian motorway signs: a diagonal arrow, IESIRE and the destination, and the
		/// distance in a white box. Fixed and solid, never breakable. <paramref name="lines"/> are the two lines of text.
		/// </summary>
		private void ExitGantry(Vector3 origin, Vector3 dir, float s, int side, string[] lines, string distance, bool arrowLeft, bool arrow)
		{
			Vector3 across = new Vector3(dir.z, 0f, -dir.x);
			Vector3 facing = -dir * side;
			Quaternion rot = Quaternion.LookRotation(facing, Vector3.up);
			Vector3 onRoad = origin + dir * s;
			float inner = side * 2.2f;
			float outer = side * (Plan.HalfWidth + 2.2f);
			float top = origin.y + 26f;
			float bottom = Plan.Ground;
			float height = top - bottom;
			foreach (float lat in new[] { inner, outer })
			{
				Vector3 c = onRoad + across * lat;
				Gantry.Box(new Vector3(c.x, bottom + height * 0.5f, c.z), new Vector3(1.6f, height, 1.6f), rot);
			}
			Vector3 mid = onRoad + across * ((inner + outer) * 0.5f);
			float span = Mathf.Abs(outer - inner);
			Gantry.Box(new Vector3(mid.x, top - 0.6f, mid.z), new Vector3(span, 1.2f, 1.4f), rot);

			// The board: green with a white edge. Positions below are in the viewer's terms (vx to the right, vy up), because the board's own
			// x axis points to the viewer's left.
			float width = span - 3f;
			Vector3 face = new Vector3(mid.x, origin.y + 19f, mid.z) + facing * 1.1f;
			Vector3 At(float vx, float vy, float z)
			{
				return face + rot * new Vector3(-vx, vy, z);
			}
			White.Box(face, new Vector3(width + 0.7f, 8.7f, 0.5f), rot);
			Board.Box(face, new Vector3(width, 8f, 0.6f), rot);

			float ax = -width * 0.5f + 4.6f;
			if (arrow)
			{
				// Diagonal arrow: up and toward the side the exit leaves on.
				Vector2 d = new Vector2(arrowLeft ? -0.5f : 0.5f, 1f).normalized;
				Vector2 tip = d * 2.7f;
				Stroke(rot, At(ax, 0f, 0.34f), new Vector2(0f, 0f), d, 5.4f, 0.8f);
				for (int k = -1; k <= 1; k += 2)
				{
					float a = k * 40f * Mathf.Deg2Rad;
					Vector2 back = -d;
					Vector2 e = new Vector2(back.x * Mathf.Cos(a) - back.y * Mathf.Sin(a), back.x * Mathf.Sin(a) + back.y * Mathf.Cos(a));
					Stroke(rot, At(ax, 0f, 0.34f), tip + e * 1.3f, e, 2.7f, 0.8f);
				}
			}

			// Distance, in a white box on the right.
			float dx = width * 0.5f - 4.2f;
			White.Box(At(dx, 0f, 0.34f), new Vector3(7.2f, 4.6f, 0.06f), rot);
			Labels.Add(new Label { Text = distance, Pos = At(dx, 0f, 0.4f) + facing * 0.02f, Facing = facing, Size = new Vector2(6.6f, 3.6f), Color = DarkC });

			// Two lines of text between the arrow and the box.
			float tx = (ax + dx) * 0.5f;
			float tw = dx - ax - 8.5f;
			Labels.Add(new Label { Text = lines[0], Pos = At(tx, 1.9f, 0.4f), Facing = facing, Size = new Vector2(tw, 2.4f), Color = PaleC });
			Labels.Add(new Label { Text = lines[1], Pos = At(tx, -1.4f, 0.4f), Facing = facing, Size = new Vector2(tw, 3.4f), Color = PaleC });
		}

		/// <summary>A white bar of the arrow, drawn from <paramref name="from"/> along <paramref name="dir"/> (viewer terms).</summary>
		private void Stroke(Quaternion rot, Vector3 origin, Vector2 from, Vector2 dir, float length, float thick)
		{
			Vector2 centre = from;
			Vector3 local = new Vector3(-dir.x, dir.y, 0f);
			Quaternion tilt = Quaternion.FromToRotation(Vector3.up, local);
			White.Box(origin + rot * new Vector3(-centre.x, centre.y, 0f), new Vector3(thick, length, 0.06f), rot * tilt);
		}

		private void WarningBoard(Vector3 basePos, Vector3 facing, string text)
		{
			Prop p = NewProp("warning", basePos, facing, 40f);
			foreach (float x in new[] { -2.8f, 2.8f })
			{
				p.Box(PoleC, new Vector3(x, 1.6f, 0f), new Vector3(0.25f, 3.2f, 0.25f));
				p.Hit(new Vector3(x, 1.6f, 0f), new Vector3(0.25f, 3.2f, 0.25f));
				Foot(p, x, 1.0f);
			}
			p.Box(YellowC, new Vector3(0f, 4.2f, 0f), new Vector3(7f, 2f, 0.2f));
			p.Hit(new Vector3(0f, 4.2f, 0f), new Vector3(7f, 2f, 0.2f));
			p.Text(text, new Vector3(0f, 4.2f, 0.11f), new Vector2(6.4f, 1.8f), DarkC, 1);
		}

		private void Chevron(Vector3 basePos, Vector3 facing, string arrow)
		{
			Prop p = NewProp("chevron", basePos, facing, 14f);
			p.Box(PoleC, new Vector3(0f, 0.7f, 0f), new Vector3(0.2f, 1.4f, 0.2f));
			p.Box(YellowC, new Vector3(0f, 2.3f, 0f), new Vector3(2.4f, 2.4f, 0.14f));
			p.Hit(new Vector3(0f, 0.7f, 0f), new Vector3(0.2f, 1.4f, 0.2f));
			p.Hit(new Vector3(0f, 2.3f, 0f), new Vector3(2.4f, 2.4f, 0.14f));
			Foot(p, 0f, 0.9f);
			p.Text(arrow, new Vector3(0f, 2.3f, 0.085f), new Vector2(2.1f, 2.1f), DarkC);
		}

		/// <summary>A sign that stands in a median gap, board across the road, and flies apart when a car hits it.</summary>
		private void GapSign(Vector3 pos, Vector3 along)
		{
			Prop p = NewProp("gapsign", pos, along, 18f);
			p.Box(PoleC, new Vector3(0f, 1.1f, 0f), new Vector3(0.25f, 2.2f, 0.25f));
			p.Box(YellowC, new Vector3(0f, 2.3f, 0f), new Vector3(2.6f, 1.1f, 0.14f));
			for (int k = -1; k <= 1; k++)
			{
				p.Box(DarkC, new Vector3(k * 0.9f, 2.3f, 0.02f), new Vector3(0.28f, 1.14f, 0.16f), Quaternion.Euler(0f, 0f, 30f));
			}
			p.Hit(new Vector3(0f, 1.1f, 0f), new Vector3(0.4f, 2.2f, 0.4f));
			p.Hit(new Vector3(0f, 2.3f, 0f), new Vector3(2.6f, 1.1f, 0.2f));
			Foot(p, 0f, 1.1f);
		}

		private static string Round50(float metres)
		{
			return Mathf.Max(50, Mathf.RoundToInt(metres / 50f) * 50) + " m";
		}

		/// <summary>
		/// Signs along a highway, for each carriageway: speed limits beside the road and on the median side (each facing only its own traffic),
		/// Romanian-style exit boards 500, 250 and 150 m before the exit and, on the other carriageway, boards that name the city the motorway
		/// goes on to. The highway has no closed end, so there is nothing to warn about.
		/// </summary>
		private void HighwaySigns(Vector3 origin, Vector3 dir, bool exitLeft, string destination, string onwards, string onwardsDistance)
		{
			Vector3 across = new Vector3(dir.z, 0f, -dir.x);
			const float exit = 700f;

			for (int side = -1; side <= 1; side += 2)
			{
				Vector3 facing = -dir * side;
				foreach (float distance in new[] { 410f, 240f })
				{
					float s = side * distance;
					SpeedSign(origin + dir * s + across * (side * (Plan.HalfWidth + 3.0f)), facing, "120");
					SpeedSign(origin + dir * s + across * (side * 1.8f), facing, "120");
				}
				foreach (float before in new[] { 500f, 250f, 150f })
				{
					float s = side * (exit - before);
					if (side > 0)
					{
						ExitGantry(origin, dir, s, side, new[] { "IESIRE", destination }, (int)before + " m", exitLeft, true);
					}
					else
					{
						ExitGantry(origin, dir, s, side, new[] { exitLeft ? "A1" : "A3", onwards }, onwardsDistance, false, false);
					}
				}
			}
		}

		/// <summary>Chevrons on the outside of every ramp bend and a speed limit near the start, both beside the road, outside the guardrail, turned to the oncoming traffic.</summary>
		private void RampSigns(List<Vector3> p)
		{
			float lift = Plan.RampLift;
			float beside = Plan.RaceHalf + 2.6f;
			for (int i = 6; i + 6 < p.Count; i += 12)
			{
				if (InDeck(p[i]))
				{
					continue;
				}
				Vector3 f0 = Geo.Forward(p, i - 4);
				Vector3 f1 = Geo.Forward(p, i + 4);
				float turn = Mathf.Asin(Mathf.Clamp(Vector3.Cross(f0, f1).y, -1f, 1f)) * Mathf.Rad2Deg;
				if (Mathf.Abs(turn) < 4f)
				{
					continue;
				}
				// A positive turn is to the right, so the outside is the left edge. The board looks back along the road, turned a little toward the road.
				float outer = turn > 0f ? -beside : beside;
				Vector3 inward = Geo.Right(p, i) * (outer > 0f ? -1f : 1f);
				Vector3 facing = (-Geo.Forward(p, i) * 0.9f + inward * 0.45f).normalized;
				Chevron(Geo.At(p, i, outer, lift), facing, turn > 0f ? ">" : "<");
			}
			for (int i = 20; i + 8 < p.Count; i++)
			{
				if (InDeck(p[i]))
				{
					continue;
				}
				SpeedSign(Geo.At(p, i + 8, beside, lift), -Geo.Forward(p, i + 8), "80");
				break;
			}
		}

		/// <summary>Five breakable signs across a median gap, on the median's centre line.</summary>
		private void GapSigns(Vector3 origin, Vector3 dir, float centre)
		{
			float spacing = GapLength / 6f;
			for (int k = -2; k <= 2; k++)
			{
				GapSign(origin + dir * (centre + k * spacing), dir);
			}
		}

		// ---- trees ------------------------------------------------------------------------------------------------

		private void Forest()
		{
			System.Random rng = new System.Random(20261024);
			for (int tries = 0; tries < 6000 && Trees.Count < 420; tries++)
			{
				float x = (float)(rng.NextDouble() * 3000 - 1500);
				float z = (float)(rng.NextDouble() * 3000 - 1500);
				AddTree(rng, x, z);
			}
			// And along the stretches of highway that go on past the plazas, so they do not run through bare grass.
			for (int arm = 0; arm < 4; arm++)
			{
				for (int k = 0; k < 70; k++)
				{
					float along = (arm % 2 == 0 ? 1f : -1f) * (1450f + (float)rng.NextDouble() * 950f);
					float across = ((float)rng.NextDouble() < 0.5f ? -1f : 1f) * (Plan.HalfWidth + 45f + (float)rng.NextDouble() * 140f);
					if (arm < 2)
					{
						AddTree(rng, along, across);
					}
					else
					{
						AddTree(rng, across, along);
					}
				}
			}
		}

		private void AddTree(System.Random rng, float x, float z)
		{
			if (!ClearOfRoads(x, z, 40f))
			{
				return;
			}
			Trees.Add(new TreeSpot
			{
				Pos = new Vector3(x, Plan.Ground, z),
				Yaw = (float)(rng.NextDouble() * 360.0),
				Height = 14f + (float)rng.NextDouble() * 12f,
				Pick = rng.Next(1000)
			});
		}
	}
}
