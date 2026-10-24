using System.Collections.Generic;
using UnityEngine;

namespace HalfGripHighway
{
	/// <summary>
	/// The two roads that were never finished, and are the map's shortcuts. Each forks off a real ramp or a plaza and rejoins the race line
	/// further round (see <c>Route.BuildDirtEast</c> and <c>BuildDirtNorth</c>), skipping about half the lap, so the game's own shortcut
	/// detection pays the player for taking them. They are dirt: the surface has less grip and more drag (see <c>HighwayScene</c>), which
	/// is where a build with the right tyres pays off. There is no guardrail near their forks, only cones. Heavy machinery and containers
	/// stand along them, parked against the edges so a wide, straight corridor is always free.
	/// </summary>
	internal sealed partial class RoadBuilder
	{
		private const float DirtHalf = Plan.RaceHalf;   // as wide as the paved ramps

		private readonly List<List<Vector3>> finishedLines = new List<List<Vector3>>();

		private readonly List<Vector3> dirtPoints = new List<Vector3>();

		private static bool NearDeck(Vector3 p, float margin)
		{
			return (Mathf.Abs(p.z) < Plan.HalfWidth + margin && Mathf.Abs(p.x) < Plan.Reach + margin)
				|| (Mathf.Abs(p.x) < Plan.HalfWidth + margin && Mathf.Abs(p.z) < Plan.Reach + margin);
		}

		/// <summary>Within a dirt road's width of its centre line (so a paved ramp's guardrail has to open there).</summary>
		private bool NearDirt(Vector3 p)
		{
			float limit = (DirtHalf + 2f) * (DirtHalf + 2f);
			for (int i = 0; i < dirtPoints.Count; i += 2)
			{
				float dx = dirtPoints[i].x - p.x;
				float dz = dirtPoints[i].z - p.z;
				if (dx * dx + dz * dz < limit)
				{
					return true;
				}
			}
			return false;
		}

		private bool NearRealRamp(Vector3 p, float margin)
		{
			float limit = (Plan.RaceHalf + margin) * (Plan.RaceHalf + margin);
			for (int i = 0; i < realRampCount; i++)
			{
				float dx = rampPoints[i].x - p.x;
				float dz = rampPoints[i].z - p.z;
				if (dx * dx + dz * dz < limit)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The path without the points at either end that are inside a highway (so the dirt starts at the highway's edge).</summary>
		private static List<Vector3> TrimEnds(List<Vector3> path, float margin)
		{
			int a = 0;
			while (a < path.Count && NearDeck(path[a], margin))
			{
				a++;
			}
			int b = path.Count - 1;
			while (b > a && NearDeck(path[b], margin))
			{
				b--;
			}
			return path.GetRange(a, b - a + 1);
		}

		/// <summary>On a finished road: a highway's deck (plazas included) or the paved ramps' surface.</summary>
		private bool InFinished(Vector3 p)
		{
			if (InDeck(p))
			{
				return true;
			}
			float limit = Plan.RaceHalf * Plan.RaceHalf;
			foreach (List<Vector3> line in finishedLines)
			{
				for (int i = 0; i + 1 < line.Count; i++)
				{
					float abx = line[i + 1].x - line[i].x;
					float abz = line[i + 1].z - line[i].z;
					float t = Mathf.Clamp01(((p.x - line[i].x) * abx + (p.z - line[i].z) * abz) / Mathf.Max(1e-4f, abx * abx + abz * abz));
					float dx = line[i].x + abx * t - p.x;
					float dz = line[i].z + abz * t - p.z;
					if (dx * dx + dz * dz < limit)
					{
						return true;
					}
				}
			}
			return false;
		}

		private static Vector3 Bilerp(Vector3 l0, Vector3 r0, Vector3 l1, Vector3 r1, float across, float along)
		{
			return Vector3.Lerp(Vector3.Lerp(l0, r0, across), Vector3.Lerp(l1, r1, across), along);
		}

		/// <summary>
		/// The dirt surface. A piece of road that lies wholly outside the finished roads is one quad; a piece that touches one is cut into 2 x 1 m cells and
		/// only the cells outside are kept, so the dirt starts exactly where a highway's edge or a paved ramp ends and never lies over them.
		/// </summary>
		private void DirtSurface(List<Vector3> run)
		{
			for (int i = 0; i + 1 < run.Count; i++)
			{
				Vector3 l0 = Geo.At(run, i, -DirtHalf, Plan.RampLift);
				Vector3 r0 = Geo.At(run, i, DirtHalf, Plan.RampLift);
				Vector3 l1 = Geo.At(run, i + 1, -DirtHalf, Plan.RampLift);
				Vector3 r1 = Geo.At(run, i + 1, DirtHalf, Plan.RampLift);
				bool any = false;
				bool all = true;
				for (int a = 0; a <= 4; a++)
				{
					for (int b = 0; b <= 2; b++)
					{
						bool inside = InFinished(Bilerp(l0, r0, l1, r1, a / 4f, b / 2f));
						any |= inside;
						all &= inside;
					}
				}
				if (all)
				{
					continue;
				}
				if (!any)
				{
					Dirt.Quad(l0, r0, r1, l1, Vector3.up);
					continue;
				}
				const int across = 14;
				const int along = 5;
				for (int u = 0; u < across; u++)
				{
					for (int v = 0; v < along; v++)
					{
						float u0 = u / (float)across, u1 = (u + 1) / (float)across, v0 = v / (float)along, v1 = (v + 1) / (float)along;
						if (InFinished(Bilerp(l0, r0, l1, r1, (u0 + u1) * 0.5f, (v0 + v1) * 0.5f)))
						{
							continue;
						}
						Dirt.Quad(Bilerp(l0, r0, l1, r1, u0, v0), Bilerp(l0, r0, l1, r1, u1, v0), Bilerp(l0, r0, l1, r1, u1, v1), Bilerp(l0, r0, l1, r1, u0, v1), Vector3.up);
					}
				}
			}
		}

		private void DirtRoad(List<Vector3> path)
		{
			if (path.Count < 20)
			{
				return;
			}
			// The visible run: from where the centre line first leaves the finished roads to where it last does.
			int a0 = 0;
			while (a0 < path.Count && InFinished(path[a0]))
			{
				a0++;
			}
			int a1 = path.Count - 1;
			while (a1 > a0 && InFinished(path[a1]))
			{
				a1--;
			}
			if (a1 - a0 < 20)
			{
				return;
			}
			List<Vector3> run = path.GetRange(a0, a1 - a0 + 1);

			DirtSurface(path);
			Slab(path, DirtHalf, Plan.RampLift, true, 0.8f, false, false, Dirt, DirtSlope, drawTop: false, skipSkirt: InFinished);
			Geo.Strip(DirtTrack, path, -6.2f, -4.4f, 0.07f, InFinished);
			Geo.Strip(DirtTrack, path, 4.4f, 6.2f, 0.07f, InFinished);

			MouthCones(run, true);
			MouthCones(run, false);
			for (int i = 0; i < path.Count; i += 4)
			{
				for (int side = -1; side <= 1; side += 2)
				{
					Vector3 p = Geo.At(path, i, side * (DirtHalf + 2.5f), Plan.RampLift);
					if (InDeck(p) && !NearDeckEdge(p) && !NearRealRamp(p, 1.5f))
					{
						Cone(p);
					}
				}
			}
			Machinery(run);
		}

		/// <summary>Cones along both edges of the first 60 m of a dirt road, where it leaves a ramp or a plaza. They break and take no speed.</summary>
		private void MouthCones(List<Vector3> run, bool atStart)
		{
			int n = run.Count;
			Vector3 p = atStart ? run[0] : run[n - 1];
			Vector3 inward = atStart ? run[3] - run[0] : run[n - 4] - run[n - 1];
			inward.y = 0f;
			inward.Normalize();
			Vector3 right = new Vector3(inward.z, 0f, -inward.x);
			for (int k = 0; k < 7; k++)
			{
				for (int side = -1; side <= 1; side += 2)
				{
					Cone(new Vector3(p.x, p.y + Plan.RampLift, p.z) + inward * (k * 9f) + right * (side * (DirtHalf + 1.8f)));
				}
			}
		}

		/// <summary>
		/// Machines and containers at work, one group every 170 m, alternating sides. Each is parked against its edge, lined up with the road, with
		/// at least 13 m of the 24 m road left clear, and never in a bend, so the road can be driven flat out.
		/// </summary>
		private void Machinery(List<Vector3> run)
		{
			float tall = HalfGripHighway.Containers.Height;
			int group = 0;
			for (int i = 40; i < run.Count - 40; i += 34)
			{
				Vector3 p = run[i];
				Vector3 forward = run[i + 1] - run[i - 1];
				forward.y = 0f;
				forward.Normalize();
				Vector3 right = new Vector3(forward.z, 0f, -forward.x);
				float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
				Vector3 before = run[i] - run[i - 6];
				Vector3 after = run[i + 6] - run[i];
				before.y = 0f;
				after.y = 0f;
				if (Vector3.Angle(before, after) > 5f || NearDeck(p, 40f) || NearRealRamp(p, 30f))
				{
					continue;
				}
				float side = group % 2 == 0 ? 1f : -1f;
				float half;
				switch (group % 6)
				{
					case 0: half = 5.3f; break;
					case 1: half = 4.8f; break;
					case 2: half = 3.5f; break;
					case 3: half = 3.2f; break;
					case 4: half = 4.8f; break;
					default: half = 3.8f; break;
				}
				Vector3 o = new Vector3(p.x, p.y + Plan.RampLift, p.z) + right * (side * (DirtHalf - half - 0.6f));
				switch (group % 6)
				{
					case 0:
						Excavator(o, yaw);
						Heap(o + forward * 34f, new Vector3(3f, 1.2f, 2.4f), yaw);
						break;
					case 1:
						DumpTruck(o, yaw);
						break;
					case 2:
						Containers.Add(new ContainerSpot { Pos = o, Yaw = yaw, Kind = group % 3 });
						Containers.Add(new ContainerSpot { Pos = o + Vector3.up * tall, Yaw = yaw, Kind = (group + 1) % 3 });
						Containers.Add(new ContainerSpot { Pos = o + forward * 24f, Yaw = yaw, Kind = (group + 2) % 3 });
						break;
					case 3:
						Roller(o, yaw);
						break;
					case 4:
						ContainerTruck(o, yaw + 180f, group % 3);
						break;
					default:
						Heap(o, new Vector3(4f, 1.4f, 3f), yaw + 15f);
						Heap(o + forward * 22f, new Vector3(3f, 1.0f, 2.4f), yaw - 10f);
						break;
				}
				group++;
			}
		}
	}
}
