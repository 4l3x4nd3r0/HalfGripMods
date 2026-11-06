using System;
using System.Collections.Generic;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>
	/// The map's trees. Only the game's own: the Forest map's cedars and the game's cherry (both drawn by <see cref="TreeInstancer"/>), plus the
	/// bamboo clumps this map made itself (merged in batches far from the road, one object each near it). Trees stand in groves (noise), thick
	/// along the touge and the dirt road, and close to the road they are solid: <see cref="TreeHit"/>.
	/// </summary>
	internal static class Forest
	{
		private const float CellSize = 500f;
		private static readonly Dictionary<string, Batch> batches = new Dictionary<string, Batch>();
		private static List<Batch> output;

		private static Batch Get(string species, int variant, int cx, int cz, Texture2D tex, Color tint)
		{
			string key = species + variant + "@" + cx + "," + cz;
			Batch b;
			if (!batches.TryGetValue(key, out b))
			{
				b = new Batch("tree " + key, tex, tint, false);
				batches[key] = b;
				output.Add(b);
			}
			return b;
		}

		private const float SolidRange = 1e6f;   // every tree is solid and falls (the game's forest trees all do): no scenery you drive through
		private const int Variants = 5;

		internal sealed class Solid
		{
			public Vector3 Pos;
			public float Radius, Height, Fell, Yaw;
			public TreeInstancer.Handle Handle;
			public int Bamboo = -1;
			public TreeHit Hit;   // the object that reacts to the car (for the debug hit test)
		}

		internal static readonly List<Solid> Solids = new List<Solid>();

		/// <summary>The trees close to the road, made real: a trunk collider for each, and for a bamboo clump its own object (it leans and falls).</summary>
		internal static void BuildSolids(Transform parent, int layer)
		{
			GameObject holder = new GameObject("TreeSolids") { layer = layer };
			holder.transform.SetParent(parent, false);
			Mesh[] stalks = new Mesh[Variants], leaves = new Mesh[Variants];
			Color leafTint = new Color(0.45f, 0.72f, 0.3f);
			for (int v = 0; v < Variants; v++)
			{
				Batch sb = new Batch("bamboo", Tex.Bamboo, Color.white, false);
				Batch lb = new Batch("bamboo leaves", Tex.Leaves, leafTint, false);
				BambooInto(Vector3.zero, new System.Random(900 + v), sb, lb);
				stalks[v] = sb.ToMesh();
				leaves[v] = lb.ToMesh();
			}
			Material stalkMat = Mats.Get(Tex.Bamboo, Color.white);
			Material leafMat = Mats.Get(Tex.Leaves, leafTint);
			foreach (Solid s in Solids)
			{
				Transform visual = null;
				if (s.Bamboo >= 0)
				{
					GameObject g = new GameObject("bamboo clump") { layer = layer };
					g.transform.SetParent(holder.transform, false);
					g.transform.SetPositionAndRotation(s.Pos, Quaternion.Euler(0f, s.Yaw, 0f));
					g.AddComponent<MeshFilter>().sharedMesh = stalks[s.Bamboo];
					MeshRenderer r = g.AddComponent<MeshRenderer>();
					r.sharedMaterial = stalkMat;
					r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
					r.receiveShadows = false;
					GameObject l = new GameObject("leaves") { layer = layer };
					l.transform.SetParent(g.transform, false);
					l.AddComponent<MeshFilter>().sharedMesh = leaves[s.Bamboo];
					MeshRenderer lr = l.AddComponent<MeshRenderer>();
					lr.sharedMaterial = leafMat;
					lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
					lr.receiveShadows = false;
					visual = g.transform;
					PerfCuller.Register(r, false, false);
					PerfCuller.Register(lr, false, false);
				}
				if (s.Bamboo >= 0)
				{
					s.Hit = TreeHit.BambooClump(holder.transform, layer, s.Pos, s.Radius, s.Radius + 6f, s.Height, s.Fell, Mod.BambooSpeedKept.Value, visual).GetComponent<TreeHit>();
				}
				else
				{
					s.Hit = TreeHit.Solid(holder.transform, layer, s.Pos, s.Radius, s.Height, s.Fell, s.Handle, visual).GetComponent<TreeHit>();
				}
			}
			Mod.Log.LogInfo("Solid trees: " + Solids.Count + " near the road");
		}

		/// <summary>A tree already standing: ground position and the radius of its crown, for the spacing test.</summary>
		private struct Spot2
		{
			public float X, Z, R;
			public bool Cherry;
		}

		private static float Crown(int species, float h)
		{
			return species == 0 ? h * 0.20f : species == 1 ? h * 0.33f : 6f;
		}

		internal static int Build(List<Batch> into, List<Keepout> keepouts, int want)
		{
			output = into;
			batches.Clear();
			Solids.Clear();
			if (want <= 0)
			{
				return 0;
			}
			List<Pt> p = Data.Main;
			System.Random rng = new System.Random(2112);

			float gs = 60f;
			Dictionary<long, List<Vector2>> grid = new Dictionary<long, List<Vector2>>();
			Action<List<Pt>> index = list =>
			{
				foreach (Pt q in list)
				{
					long key = ((long)Mathf.FloorToInt(q.P.x / gs) << 32) ^ (uint)Mathf.FloorToInt(q.P.z / gs);
					List<Vector2> cell;
					if (!grid.TryGetValue(key, out cell))
					{
						cell = new List<Vector2>();
						grid[key] = cell;
					}
					cell.Add(new Vector2(q.P.x, q.P.z));
				}
			};
			index(p);
			index(Data.Branch);
			Func<float, float, float> nearest = (x, z) =>
			{
				float best = 1e9f;
				int cx = Mathf.FloorToInt(x / gs), cz = Mathf.FloorToInt(z / gs);
				for (int dx = -2; dx <= 2; dx++)
				{
					for (int dz = -2; dz <= 2; dz++)
					{
						List<Vector2> cell;
						if (grid.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out cell))
						{
							foreach (Vector2 v in cell)
							{
								float d = (v.x - x) * (v.x - x) + (v.y - z) * (v.y - z);
								if (d < best)
								{
									best = d;
								}
							}
						}
					}
				}
				return Mathf.Sqrt(best);
			};

			// trees already placed, in 32-unit cells: no two crowns may overlap by more than a fifth
			const float SpaceCell = 32f;
			Dictionary<long, List<Spot2>> placed = new Dictionary<long, List<Spot2>>();
			List<Spot2> all = new List<Spot2>();

			// the mountain, the hairpins and the temple get most of the tries: that is where the user wants the forest
			List<int> focus = new List<int>();
			for (int i = 0; i < p.Count; i++)
			{
				int sec = p[i].Section;
				if (p[i].Surface != Data.Cave && (sec == Data.SecStart || sec == Data.SecTouge || sec == Data.SecTemple || sec == Data.SecShrine))
				{
					focus.Add(i);
				}
			}

			// the temple and the shrine grounds: where the cherries are wanted, kept up with a quota once the 800 are placed
			List<int> groundsIdx = new List<int>();
			for (int i = 0; i < p.Count; i++)
			{
				if (p[i].Surface != Data.Cave && (p[i].Section == Data.SecTemple || p[i].Section == Data.SecShrine))
				{
					groundsIdx.Add(i);
				}
			}
			const int CherryTarget = 110;

			float floor = Data.Heights[0];
			for (int i = 0; i < Data.Heights.Length; i += 7)
			{
				floor = Mathf.Min(floor, Data.Heights[i]);
			}
			int made = 0, tries = 0;
			int[] counts = new int[6];
			int rRoad = 0, rGrove = 0, rKeep = 0, rCave = 0, rSlope = 0, rSpace = 0, rFlat = 0;
			// the real surface of the terrain mesh (a ray down onto its collider), not the grid's bilinear height: the mesh splits every cell into two
			// triangles, so the two differ by a few units on a slope and trees hung in the air
			Physics.SyncTransforms();
			Func<float, float, float> ground = (gx, gz) =>
			{
				RaycastHit hit;
				if (Physics.Raycast(new Vector3(gx, Data.HeightAt(gx, gz) + 60f, gz), Vector3.down, out hit, 160f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.name.StartsWith("Terrain_"))
				{
					return hit.point.y;
				}
				return Data.HeightAt(gx, gz);
			};
			while ((made < want || (counts[1] < CherryTarget && groundsIdx.Count > 0)) && tries < want * 150)
			{
				tries++;
				bool cherryPhase = made >= want;   // the 800 are placed: only cherries now, on the temple grounds, until there are enough
				Pt q = cherryPhase ? p[groundsIdx[rng.Next(groundsIdx.Count)]] : ((focus.Count > 0 && rng.NextDouble() < 0.65) ? p[focus[rng.Next(focus.Count)]] : p[rng.Next(p.Count)]);
				if (q.Surface == Data.Cave)
				{
					continue;
				}
				float ang = (float)(rng.NextDouble() * Math.PI * 2.0);
				float dist = 30f + (float)Math.Pow(rng.NextDouble(), 1.5) * 320f;
				float x = q.P.x + Mathf.Cos(ang) * dist, z = q.P.z + Mathf.Sin(ang) * dist;
				bool grounds = q.Section == Data.SecTemple || q.Section == Data.SecShrine;
				bool mountain = q.Section == Data.SecStart || q.Section == Data.SecTouge;
				// species first, so the clearances can use the size of its crown
				float roll = (float)rng.NextDouble();
				int species;
				if (grounds)
				{
					species = roll < 0.72f ? 1 : roll < 0.88f ? 0 : 4;
				}
				else if (mountain)
				{
					species = roll < 0.92f ? 0 : roll < 0.96f ? 1 : 4;
				}
				else if (q.Section == Data.SecForest)
				{
					species = roll < 0.45f ? 0 : roll < 0.87f ? 4 : 1;
				}
				else
				{
					species = roll < 0.78f ? 0 : 1;
				}
				if (cherryPhase)
				{
					species = 1;
				}
				float h = species == 0 ? R(rng, 50f, 80f) : species == 1 ? R(rng, 30f, 44f) : 50f;
				float crown = Crown(species, h);

				float roadDist = nearest(x, z);
				if (roadDist < 30f + crown + (grounds ? 14f : 0f))
				{
					rRoad++;
					continue;
				}
				// groves: trees thicken where the noise is high and thin out in clearings
				float grove = Mathf.PerlinNoise(x * 0.0042f + 17f, z * 0.0042f + 5f);
				if (grove < 0.36f && rng.NextDouble() > 0.12)
				{
					rGrove++;
					continue;
				}
				bool blocked = false;
				foreach (Keepout k in keepouts)
				{
					float lim = k.R + crown;
					if ((new Vector2(x, z) - k.C).sqrMagnitude < lim * lim)
					{
						blocked = true;
						break;
					}
				}
				if (blocked)
				{
					rKeep++;
					continue;
				}
				// the sticks of the ground: no tree over the cave roof or the rock round its mouths
				if (Cave.BlocksTree(x, z, crown + 8f))
				{
					rCave++;
					continue;
				}
				// the lowest ground under the trunk, so it never hangs on a slope
				float tr = Mathf.Max(1.5f, crown * 0.2f);
				float y = ground(x, z);
				float low = Mathf.Min(y, ground(x + tr, z), ground(x - tr, z), ground(x, z + tr), ground(x, z - tr));
				float slope = Mathf.Abs(Data.HeightAt(x + 8f, z) - Data.HeightAt(x - 8f, z)) + Mathf.Abs(Data.HeightAt(x, z + 8f) - Data.HeightAt(x, z - 8f));
				// a cherry tree needs a flat spot with room all round: its crown must not run into the hillside either
				float slopeLimit = species == 1 ? 8f : 22f;
				if (species == 1 && slope <= slopeLimit)
				{
					float hmin = y, hmax = y, ring = crown * 0.6f;
					for (int rq = 0; rq < 8; rq++)
					{
						float ga = rq * Mathf.PI * 0.25f;
						float gh = ground(x + Mathf.Cos(ga) * ring, z + Mathf.Sin(ga) * ring);
						hmin = Mathf.Min(hmin, gh);
						hmax = Mathf.Max(hmax, gh);
					}
					if (hmax - hmin > 2.2f)
					{
						rFlat++;
						continue;
					}
				}				if (slope > slopeLimit || y < floor + 10f)
				{
					rSlope++;
					continue;
				}
				// no overlap with the trees already there
				int ccx = Mathf.FloorToInt(x / SpaceCell), ccz = Mathf.FloorToInt(z / SpaceCell);
				bool crowded = false;
				for (int dx = -1; dx <= 1 && !crowded; dx++)
				{
					for (int dz = -1; dz <= 1 && !crowded; dz++)
					{
						List<Spot2> cell;
						if (!placed.TryGetValue(((long)(ccx + dx) << 32) ^ (uint)(ccz + dz), out cell))
						{
							continue;
						}
						foreach (Spot2 o in cell)
						{
							float lim = (o.R + crown) * (o.Cherry || species == 1 ? 1.0f : 0.8f);   // a cherry's crown keeps clear of every other tree's
							if ((o.X - x) * (o.X - x) + (o.Z - z) * (o.Z - z) < lim * lim)
							{
								crowded = true;
								break;
							}
						}
					}
				}
				if (crowded)
				{
					rSpace++;
					continue;
				}

				int cx = Mathf.FloorToInt(x / CellSize), cz = Mathf.FloorToInt(z / CellSize);
				Vector3 pos = new Vector3(x, low - 0.6f - slope * 0.05f, z);
				float yaw = (float)(rng.NextDouble() * 360.0);
				bool near = roadDist < SolidRange;
				switch (species)
				{
					case 0:
					{
						TreeInstancer.Handle handle = near ? new TreeInstancer.Handle() : null;
						if (!TreeInstancer.AddForest(pos, yaw, h, rng.Next(1000), handle))
						{
							continue;
						}
						if (near)
						{
							Solids.Add(new Solid { Pos = pos, Radius = h * 0.03f, Height = h * 0.8f, Fell = 120f, Handle = handle });
						}
						break;
					}
					case 1:
					{
						TreeInstancer.Handle handle = near ? new TreeInstancer.Handle() : null;
						if (!TreeInstancer.AddSakura(pos, yaw, h, rng.Next(1000), handle))
						{
							continue;
						}
						if (near)
						{
							Solids.Add(new Solid { Pos = pos, Radius = h * 0.045f, Height = h * 0.7f, Fell = 120f, Handle = handle });
						}
						break;
					}
					default:
						if (near)
						{
							// solid for a slow car, broken and driven through by a fast one (see TreeHit.BambooClump)
							Solids.Add(new Solid { Pos = pos, Radius = 3.5f, Height = 50f, Fell = Mod.BambooFellSpeed.Value, Bamboo = rng.Next(Variants), Yaw = yaw });
						}
						else
						{
							BambooClump(pos, rng, cx, cz);
						}
						break;
				}
				Spot2 s2 = new Spot2 { X = x, Z = z, R = crown, Cherry = species == 1 };
				long pk = ((long)ccx << 32) ^ (uint)ccz;
				List<Spot2> list;
				if (!placed.TryGetValue(pk, out list))
				{
					list = new List<Spot2>();
					placed[pk] = list;
				}
				list.Add(s2);
				all.Add(s2);
				counts[species]++;
				made++;
			}
			// the proof: no two crowns overlap by more than the allowance
			int overlaps = 0;
			for (int i = 0; i < all.Count; i++)
			{
				for (int j = i + 1; j < all.Count; j++)
				{
					float lim = (all[i].R + all[j].R) * 0.8f;
					if ((all[i].X - all[j].X) * (all[i].X - all[j].X) + (all[i].Z - all[j].Z) * (all[i].Z - all[j].Z) < lim * lim - 0.01f)
					{
						overlaps++;
					}
				}
			}
			long fingerprint = 17;
			foreach (Spot2 sp in all)
			{
				fingerprint = fingerprint * 31 + Mathf.RoundToInt(sp.X * 10f);
				fingerprint = fingerprint * 31 + Mathf.RoundToInt(sp.Z * 10f);
				fingerprint = fingerprint * 31 + (sp.Cherry ? 1 : 0);
			}
			Mod.Log.LogInfo("Forest fingerprint " + fingerprint + " over " + all.Count + " trees");
			Mod.Log.LogInfo("Forest: " + made + " trees (game cedar " + counts[0] + ", game cherry " + counts[1] + ", bamboo " + counts[4] + "), " + Solids.Count + " of them solid, " + batches.Count + " batches; "
				+ overlaps + " overlapping pairs; rejected: road " + rRoad + ", grove " + rGrove + ", building " + rKeep + ", cave " + rCave + ", slope " + rSlope + ", not flat (cherry) " + rFlat + ", spacing " + rSpace);
			return made;
		}
		private static float R(System.Random rng, float lo, float hi)
		{
			return lo + (float)rng.NextDouble() * (hi - lo);
		}

		private static void BambooClump(Vector3 pos, System.Random rng, int cx, int cz)
		{
			BambooInto(pos, rng, Get("bamboo", 0, cx, cz, Tex.Bamboo, Color.white), Get("bamboo leaves", 0, cx, cz, Tex.Leaves, new Color(0.45f, 0.72f, 0.3f)));
		}

		/// <summary>A clump of tall bamboo: green stalks with dark joints and a tuft of leaves at the top.</summary>
		private static void BambooInto(Vector3 pos, System.Random rng, Batch stalk, Batch leaf)
		{
			int stalks = rng.Next(6, 11);
			for (int i = 0; i < stalks; i++)
			{
				float a = R(rng, 0f, 6.28f), d = R(rng, 0f, 4.5f);
				Vector3 b = pos + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
				float h = R(rng, 42f, 62f);
				Vector3 top = b + new Vector3(R(rng, -4f, 4f), h, R(rng, -4f, 4f));
				stalk.Cone(b, 0.6f, top, 0.35f, 5, 10f);
				leaf.Sphere(top, new Vector3(3.6f, 2.0f, 3.6f), 3, 6, 8f, 0.3f, rng.Next(1000));
			}
		}
	}
}
