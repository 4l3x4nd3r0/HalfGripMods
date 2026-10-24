using System.Collections.Generic;
using UnityEngine;

namespace HalfGripHighway
{
	/// <summary>
	/// Cones that tell the player where to go. Straight on past each exit is a dead end, so a line of cones closes the road across every lane
	/// just beyond the exit, and cones edge each real ramp where it crosses the open plaza. They break when touched and take none of the
	/// car's speed, so they only guide.
	/// </summary>
	internal sealed partial class RoadBuilder
	{
		private bool NearRamp(Vector3 p, float margin)
		{
			float limit = (Plan.RaceHalf + margin) * (Plan.RaceHalf + margin);
			foreach (Vector3 r in rampPoints)
			{
				float dx = r.x - p.x;
				float dz = r.z - p.z;
				if (dx * dx + dz * dz < limit)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Cones from <paramref name="a"/> to <paramref name="b"/> every 4 m, leaving out any that would stand on a ramp.</summary>
		private void ConeLine(Vector3 a, Vector3 b)
		{
			float length = Vector3.Distance(a, b);
			int n = Mathf.Max(1, Mathf.CeilToInt(length / 4f));
			for (int k = 0; k <= n; k++)
			{
				Vector3 p = Vector3.Lerp(a, b, k / (float)n);
				if (!NearRamp(p, 2f))
				{
					Cone(p);
				}
			}
		}

		private void ExitCones(Route route)
		{
			float half = Plan.HalfWidth - 2f;

			// Past each exit (just beyond the plaza) the highway goes on: two lines of cones across the racing carriageway say the way is the ramp.
			float inner = Plan.MedianHalf + 1.5f;
			foreach (float x in new[] { Plan.PlazaEnd + 20f, Plan.PlazaEnd + 40f })
			{
				ConeLine(new Vector3(x, 0f, -half), new Vector3(x, 0f, -inner));
				ConeLine(new Vector3(-half, Plan.Low, -x), new Vector3(-inner, Plan.Low, -x));
			}

			// Cones along both edges of each ramp wherever it crosses a plaza, every 18 m.
			foreach (List<Vector3> ramp in new[] { route.NorthEast, route.SouthWest })
			{
				for (int i = 0; i < ramp.Count; i += 4)
				{
					for (int side = -1; side <= 1; side += 2)
					{
						Vector3 p = Geo.At(ramp, i, side * (Plan.RaceHalf + 2.5f), Plan.RampLift);
						if (InDeck(p) && !NearDeckEdge(p))
						{
							Cone(p);
						}
					}
				}
			}
		}

		/// <summary>Within a few metres of the guardrail line, where a cone would stand in the barrier.</summary>
		private static bool NearDeckEdge(Vector3 p)
		{
			return Mathf.Abs(Mathf.Abs(p.z) - Plan.HalfWidth) < 4f && Mathf.Abs(p.x) < Plan.Reach
				|| Mathf.Abs(Mathf.Abs(p.x) - Plan.HalfWidth) < 4f && Mathf.Abs(p.z) < Plan.Reach;
		}
	}
}
