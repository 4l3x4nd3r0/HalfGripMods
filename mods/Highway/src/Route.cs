using System.Collections.Generic;
using UnityEngine;

namespace HalfGripHighway
{
	/// <summary>
	/// The numbers of the map. Unity axes: X east, Z north, Y up. Highway B (the bridge) runs along X at height 0,
	/// highway L (the lower one) along Z at <see cref="Low"/>. Both are 46.2 m wide, two carriageways of two lanes
	/// either side of a median, and traffic keeps right: the race goes east on B's south carriageway and south on L's west one.
	/// </summary>
	internal static class Plan
	{
		public const float Reach = 2400f;          // each highway runs from -Reach to +Reach (the ends are out of sight)
		public const float Lane = 11f;             // a car is about 5.4 wide, so a lane is twice that
		public const float MedianHalf = 5f;        // a 10 m median (barrier lines on both edges, open between)
		public const float Inner = 2f;             // shoulder beside the median
		public const float Outer = 4f;             // shoulder beside the outer wall
		public const float HalfWidth = MedianHalf + Inner + 2f * Lane + Outer;   // median centre to the outer edge
		public const float RaceHalf = (HalfWidth - MedianHalf) / 2f;              // half of a carriageway (and of a ramp)
		public const float RaceOffset = MedianHalf + RaceHalf;                    // centre of the racing carriageway
		public const float MedianEnds = 620f;      // past this the carriageways are one open plaza, until PlazaEnd
		public const float PlazaEnd = 940f;        // the median and the markings go on from here to the far end
		public const float BridgeHalf = 45f;       // half the length of the bridge deck
		public const float Low = -9f;
		public const float Ground = -9.1f;
		public const float WallHeight = 2f;
		public const float PerMetre = 3.3f;        // a game car is 14.2 long and about 4.3 m in real life
		public const float RampLift = 0.03f;       // ramps sit a hair above the plaza they start in, so they do not z-fight
	}

	/// <summary>The lap: the racing line as a closed polyline, with its heights, plus the two ramps cut out of it.</summary>
	internal sealed class Route
	{
		public const float StartX = -300f;

		/// <summary>
		/// The work zone on the lower highway, just before it goes under the bridge: the outer lane of the racing carriageway (the side the
		/// right-hand exit is on) is closed from 335 m to 65 m before the bridge, and traffic is sent into the inner lane. The racing line
		/// sits on the lane divider (19 m from the median); the middle of the inner lane is 12.5 m, so it moves 6.5 m toward the median.
		/// </summary>
		public const float ZoneShift = 6.5f;

		/// <summary>0 outside the zone, 1 in the middle of it, smooth at both ends. <paramref name="s"/> is the distance before the bridge (z).</summary>
		public static float ZoneFraction(float s)
		{
			return Smooth((345f - s) / 60f) * (1f - Smooth((90f - s) / 45f));
		}

		public static bool InWorkZone(Vector3 p)
		{
			return p.y < -8f && p.z > 20f && p.z < 400f && Mathf.Abs(p.x + Plan.RaceOffset) < 14f;
		}

		/// <summary>Half-widths of the track at a waypoint: the whole carriageway, or only the open lane inside the work zone.</summary>
		public static void Widths(Vector3 p, out float right, out float left)
		{
			right = Plan.RaceHalf;
			left = Plan.RaceHalf;
			if (InWorkZone(p))
			{
				float f = ZoneFraction(p.z);
				right = Plan.RaceHalf - 9f * f;
				left = Plan.RaceHalf - 7f * f;
			}
		}

		public List<Vector3> Dense = new List<Vector3>();   // about every 5 m
		public float[] Cum;
		public float Length;                                  // closed lap length
		public List<Vector3> NorthEast = new List<Vector3>(); // ramp from B down to L
		public List<Vector3> SouthWest = new List<Vector3>(); // ramp from L up to B
		public List<Vector3> DirtEast = new List<Vector3>();   // the dirt shortcut that forks right at the bridge highway's exit and rejoins the south-west ramp
		public List<Vector3> DirtNorth = new List<Vector3>();  // the dirt shortcut that goes straight on at the end of the north-east ramp and rejoins the bridge highway

		private float neStart, swStart, climbStart, end;

		private sealed class Turtle
		{
			public Vector3 Pos;
			public float Yaw;                                  // degrees, 0 = +Z, 90 = +X
			public float Dist;
			public readonly List<Vector3> Pts = new List<Vector3>();
			public readonly List<float> Ds = new List<float>();

			public Turtle(Vector3 pos, float yaw)
			{
				Pos = pos;
				Yaw = yaw;
				Add();
			}

			private void Add()
			{
				Pts.Add(Pos);
				Ds.Add(Dist);
			}

			private Vector3 Heading => new Vector3(Mathf.Sin(Yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(Yaw * Mathf.Deg2Rad));

			public void Line(float length)
			{
				Vector3 h = Heading;
				int n = Mathf.Max(1, Mathf.CeilToInt(length / 5f));
				Vector3 from = Pos;
				for (int i = 1; i <= n; i++)
				{
					Pos = from + h * (length * i / n);
					Dist += length / n;
					Add();
				}
			}

			/// <summary>Positive degrees turn right, negative left.</summary>
			public void Arc(float radius, float degrees)
			{
				float s = Mathf.Sign(degrees);
				Vector3 h = Heading;
				Vector3 right = new Vector3(h.z, 0f, -h.x);
				Vector3 center = Pos + right * (s * radius);
				Vector3 from = Pos - center;
				float arc = radius * Mathf.Abs(degrees) * Mathf.Deg2Rad;
				int n = Mathf.Max(2, Mathf.CeilToInt(arc / 5f));
				for (int i = 1; i <= n; i++)
				{
					Pos = center + Quaternion.Euler(0f, s * Mathf.Abs(degrees) * i / n, 0f) * from;
					Dist += arc / n;
					Add();
				}
				Yaw += degrees;
			}
		}

		public static Route Build()
		{
			Route r = new Route();
			Turtle t = new Turtle(new Vector3(StartX, 0f, -Plan.RaceOffset), 90f);

			// B east to the plaza, then the north-east ramp: left, north, left, west, left onto L, heading south.
			t.Line(950f);
			float arc1 = t.Dist;
			t.Arc(250f, -90f);
			t.Line(480f + Plan.RaceOffset);
			t.Arc(150f, -90f);
			t.Line(600f + Plan.RaceOffset);
			t.Arc(150f, -90f);
			float neEnd = t.Dist;

			// L south, then the south-west ramp: right, west, right, north, right onto B, heading east.
			t.Line(1380f);
			float swArc = t.Dist;
			t.Arc(250f, 90f);
			t.Line(400f - Plan.RaceOffset);
			t.Arc(150f, 90f);
			t.Line(600f - Plan.RaceOffset);
			t.Arc(150f, 90f);
			r.end = t.Dist;
			t.Line(350f);
			float lap = t.Dist;

			// The last point is the first one again.
			t.Pts.RemoveAt(t.Pts.Count - 1);
			t.Ds.RemoveAt(t.Ds.Count - 1);

			r.neStart = arc1 - 30f;
			r.swStart = swArc - 20f;
			r.climbStart = r.end - 410f;
			float descend = arc1 + 190f;

			for (int i = 0; i < t.Pts.Count; i++)
			{
				float d = t.Ds[i];
				float y;
				if (d < descend + 300f)
				{
					y = Mathf.Lerp(0f, Plan.Low, Smooth((d - descend) / 300f));
				}
				else if (d < r.climbStart)
				{
					y = Plan.Low;
				}
				else
				{
					y = Mathf.Lerp(Plan.Low, 0f, Smooth((d - r.climbStart) / 300f));
				}
				Vector3 p = t.Pts[i];
				p.y = y;
				r.Dense.Add(p);
			}
			r.Cum = Geo.Cumulative(r.Dense);
			r.Length = lap;

			r.NorthEast = r.Sub(r.neStart, neEnd + 20f);
			r.SouthWest = r.Sub(r.swStart, r.end + 30f);

			r.DirtEast = BuildDirtEast(r);
			r.DirtNorth = BuildDirtNorth(r);

			// Into the inner lane through the work zone (toward the median, which is +x on the lower highway's racing side).
			for (int i = 0; i < r.Dense.Count; i++)
			{
				Vector3 q = r.Dense[i];
				if (t.Ds[i] > neEnd && t.Ds[i] < swArc && InWorkZone(q))
				{
					q.x += ZoneShift * ZoneFraction(q.z);
					r.Dense[i] = q;
				}
			}
			r.Cum = Geo.Cumulative(r.Dense);
			return r;
		}

		/// <summary>
		/// Shortcut one. The north-east ramp reflected in the racing line, so it forks to the right exactly where the real one forks to the left,
		/// then straight on west across the lower highway's south plaza and into the start of the south-west ramp's west leg, heading the way the race
		/// goes. It skips the north-east ramp and the whole lower highway, about 3.7 km of the lap.
		/// </summary>
		private static List<Vector3> BuildDirtEast(Route r)
		{
			float mirror = -2f * Plan.RaceOffset;
			List<Vector3> path = new List<Vector3>();
			foreach (Vector3 q in r.NorthEast)
			{
				if (q.z > 700f && q.x < 140f)
				{
					break;
				}
				path.Add(new Vector3(q.x, q.y, mirror - q.z));
			}
			Vector3 from = path[path.Count - 1];
			const float toX = -300f;
			float length = from.x - toX;
			int steps = Mathf.CeilToInt(length / 5f);
			for (int i = 1; i <= steps; i++)
			{
				float x = Mathf.Lerp(from.x, toX, i / (float)steps);
				// It meets the west leg's own line (z = -900) at x = -262 and runs along it from there.
				float blend = Smooth((from.x - x) / (from.x + 262f));
				path.Add(new Vector3(x, Plan.Low, Mathf.Lerp(from.z, -900f, blend)));
			}
			return path;
		}

		/// <summary>
		/// Shortcut two. From the end of the north-east ramp's west leg it goes straight on west across the lower highway's north plaza instead of
		/// turning onto it, then follows the south-west ramp's route reflected north of the bridge highway, down and east into the bridge highway's
		/// west plaza. It skips the lower highway and the south-west ramp, about 3.5 km of the lap.
		/// </summary>
		private static List<Vector3> BuildDirtNorth(Route r)
		{
			List<Vector3> path = new List<Vector3>();
			Vector3 from = Vector3.zero;
			foreach (Vector3 q in r.NorthEast)
			{
				if (q.z > 700f && q.x < 140f)
				{
					break;
				}
				from = q;
			}
			const float toX = -262f;
			float length = from.x - toX;
			int steps = Mathf.CeilToInt(length / 5f);
			for (int i = 0; i <= steps; i++)
			{
				float t = i / (float)steps;
				path.Add(new Vector3(Mathf.Lerp(from.x, toX, t), Plan.Low, Mathf.Lerp(from.z, 900f, Smooth(t))));
			}
			bool started = false;
			foreach (Vector3 q in r.SouthWest)
			{
				if (!started && q.x <= toX + 0.5f && q.z < -800f)
				{
					started = true;
				}
				if (started)
				{
					path.Add(new Vector3(q.x, q.y, -q.z));
				}
			}
			return path;
		}

		private static float Smooth(float x)
		{
			x = Mathf.Clamp01(x);
			return x * x * (3f - 2f * x);
		}

		public List<Vector3> Sub(float d0, float d1)
		{
			List<Vector3> pts = new List<Vector3>();
			for (int i = 0; i < Dense.Count; i++)
			{
				if (Cum[i] >= d0 && Cum[i] <= d1)
				{
					pts.Add(Dense[i]);
				}
			}
			return pts;
		}

		/// <summary>
		/// Waypoints for the game's track spline, the way the game's own maps have them (Mountain: 16 for 3.1 km): a point at least every
		/// <paramref name="maxStep"/> metres on straights and one for every <paramref name="maxTurn"/> degrees of bend. The game smooths
		/// between them, and the bots do not need a finer line than that.
		/// </summary>
		public List<Vector3> ControlPoints(float maxStep, float maxTurn)
		{
			List<Vector3> pts = new List<Vector3> { Dense[0] };
			float since = 0f;
			float turned = 0f;
			for (int i = 1; i < Dense.Count - 1; i++)
			{
				Vector3 a = Dense[i] - Dense[i - 1];
				Vector3 b = Dense[i + 1] - Dense[i];
				a.y = 0f;
				b.y = 0f;
				since += a.magnitude;
				turned += Vector3.Angle(a, b);
				if (since >= (InWorkZone(Dense[i]) ? 40f : maxStep) || turned >= maxTurn)
				{
					pts.Add(Dense[i]);
					since = 0f;
					turned = 0f;
				}
			}
			// The last waypoint must not sit on top of the first one: the loop closes between them.
			while (pts.Count > 4 && Vector3.Distance(pts[pts.Count - 1], pts[0]) < maxStep * 0.4f)
			{
				pts.RemoveAt(pts.Count - 1);
			}
			return pts;
		}

		/// <summary>Where along the lap a point of the dense line sits.</summary>
		public Vector3 AtDistance(float s)
		{
			Geo.PointAt(Dense, Cum, s, out Vector3 pos, out _);
			return pos;
		}
	}
}
