using System.Collections.Generic;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>
	/// The things that stand beside and over the road: the start gantry, the hairpin chevron boards, the temple (Temple.cs) and the Shinto
	/// gates (Shrine.cs). All of it is batches of boxes and cones in the game's low-poly look, textured by <see cref="Tex"/>.
	/// </summary>
	internal static partial class Landmarks
	{
		private static List<Batch> into;
		private static List<Keepout> keep;
		private static List<Label> labels;
		private static readonly Dictionary<string, Batch> pool = new Dictionary<string, Batch>();

		private static readonly Color Black = new Color(0.05f, 0.05f, 0.06f);
		private static readonly Color Gold = new Color(0.88f, 0.70f, 0.22f);

		internal static void Build(List<Batch> batches, List<Keepout> keepouts, List<Label> labelList)
		{
			into = batches;
			keep = keepouts;
			labels = labelList;
			pool.Clear();
			footings = 0;

			Chevrons();
			TofuStand();
			Sanmon();
			Courtyard();
			Torii();
			Mod.Log.LogInfo("Temple of Drift landmarks: " + pool.Count + " batches, " + footings + " footings added where the ground fell away");
		}

		// ---- helpers ------------------------------------------------------------------------------------------------------

		private static Batch B(string name, Texture2D tex, Color tint, bool collide)
		{
			string key = name + (collide ? "#c" : "#n");
			Batch b;
			if (!pool.TryGetValue(key, out b))
			{
				b = new Batch(name, tex, tint, collide) { CastShadows = true };
				pool[key] = b;
				into.Add(b);
			}
			return b;
		}

		private static Batch Red(bool collide) { return B("red lacquer", Tex.WoodRed, Color.white, collide); }
		private static Batch Dark(bool collide) { return B("dark wood", Tex.WoodDark, Color.white, collide); }
		private static Batch Plaster(bool collide) { return B("plaster", Tex.Plaster, Color.white, collide); }
		private static Batch Tile() { return B("roof tiles", Tex.Tile, Color.white, false); }
		private static Batch Stone(bool collide) { return B("stone", Tex.Stone, Color.white, collide); }
		private static Batch Flat(string name, Color c, bool collide) { return B(name, Tex.White, c, collide); }

		private static void Keep(Vector3 p, float r)
		{
			keep.Add(new Keepout { C = new Vector2(p.x, p.z), R = r });
		}

		/// <summary>The lowest ground under a rectangle (corners, edge middles and centre, 1 unit wider than the piece).</summary>
		internal static float GroundMin(Vector3 c, float hx, float hz, Quaternion rot)
		{
			float low = float.MaxValue;
			foreach (float ux in new[] { -1f, 0f, 1f })
			{
				foreach (float uz in new[] { -1f, 0f, 1f })
				{
					Vector3 q = c + rot * new Vector3(ux * (hx + 1f), 0f, uz * (hz + 1f));
					low = Mathf.Min(low, Data.HeightAt(q.x, q.z));
				}
			}
			return low;
		}

		/// <summary>
		/// Makes a base stand on the ground wherever the ground is: a block from the lowest terrain under the rectangle (and 1.5 below it) up to
		/// <paramref name="topY"/>. Nothing is added when the ground is already higher than the piece's own bottom.
		/// </summary>
		private static void Footing(Batch b, Vector3 c, float hx, float hz, Quaternion rot, float bottomY)
		{
			float g = GroundMin(c, hx, hz, rot);
			if (g > bottomY - 0.2f)
			{
				return;
			}
			float low = g - 1.5f;
			b.Box(new Vector3(c.x, (bottomY + low) * 0.5f + 0.3f, c.z), new Vector3(2f * hx - 0.1f, bottomY - low + 0.6f, 2f * hz - 0.1f), rot, 10f);
			footings++;
		}

		private static int footings;

		/// <summary>A place on the lap: a point <paramref name="along"/> units after route point <paramref name="index"/>.</summary>
		private sealed class Spot
		{
			public Vector3 P;
			public Vector3 F;
			public Vector3 R;

			public Quaternion Rot
			{
				get { return Quaternion.LookRotation(F, Vector3.up); }
			}

			public Vector3 At(float across, float up, float along)
			{
				return P + R * across + Vector3.up * up + F * along;
			}
		}

		private static Spot SpotAt(int index, float along)
		{
			List<Pt> p = Data.Main;
			int n = p.Count;
			float f = index + along / Data.Meta["step"];
			int i0 = ((Mathf.FloorToInt(f) % n) + n) % n;
			float t = f - Mathf.Floor(f);
			int i1 = (i0 + 1) % n;
			Vector3 fwd = Vector3.Lerp(Data.Forward(p, i0, true), Data.Forward(p, i1, true), t).normalized;
			return new Spot
			{
				P = Vector3.Lerp(p[i0].P, p[i1].P, t),
				F = fwd,
				R = new Vector3(fwd.z, 0f, -fwd.x)
			};
		}

		/// <summary>
		/// A hip roof (or a pyramid when the ridge is short): <paramref name="w"/> across (local x), <paramref name="d"/> deep (local z), the eave
		/// line at the centre's height, rising <paramref name="rise"/> to a ridge of <paramref name="ridge"/> along x. <paramref name="eave"/> is a
		/// thick slab under it (the soffit), <paramref name="oh"/> the overhang.
		/// </summary>
		private static void Roof(Batch tile, Batch eave, Vector3 center, Quaternion rot, float w, float d, float rise, float ridge, float oh)
		{
			float hw = w * 0.5f + oh, hd = d * 0.5f + oh;
			float r = Mathf.Min(ridge * 0.5f, hw * 0.92f);
			System.Func<float, float, float, Vector3> L = (x, y, z) => center + rot * new Vector3(x, y, z);
			Vector3 a = L(-hw, 0f, -hd), b = L(hw, 0f, -hd), c = L(hw, 0f, hd), dd = L(-hw, 0f, hd);
			Vector3 inside = L(0f, -rise, 0f);
			if (r < 0.6f)
			{
				Vector3 apex = L(0f, rise, 0f);
				tile.Tri(a, b, apex, (a + b + apex) / 3f - inside, 14f);
				tile.Tri(b, c, apex, (b + c + apex) / 3f - inside, 14f);
				tile.Tri(c, dd, apex, (c + dd + apex) / 3f - inside, 14f);
				tile.Tri(dd, a, apex, (dd + a + apex) / 3f - inside, 14f);
			}
			else
			{
				Vector3 r1 = L(-r, rise, 0f), r2 = L(r, rise, 0f);
				tile.Quad(a, b, r2, r1, (a + b + r2 + r1) * 0.25f - inside, 14f);
				tile.Quad(dd, c, r2, r1, (dd + c + r2 + r1) * 0.25f - inside, 14f);
				tile.Tri(b, c, r2, (b + c + r2) / 3f - inside, 14f);
				tile.Tri(dd, a, r1, (dd + a + r1) / 3f - inside, 14f);
			}
			if (eave != null)
			{
				eave.Box(L(0f, -1.3f, 0f), new Vector3(2f * hw - 1.5f, 2.6f, 2f * hd - 1.5f), rot, 10f);
			}
		}

		// ---- start gantry ---------------------------------------------------------------------------------------------------

		private static void StartGantry()
		{
			Spot s = SpotAt(0, 6f);
			Quaternion rot = s.Rot;
			float half = Data.Main[0].W * 0.5f + 5f;
			foreach (float side in new[] { -1f, 1f })
			{
				Red(true).Pillar(s.At(side * half, -1.5f, 0f), 1.8f, 42f, 10, 12f);
				Stone(true).Box(s.At(side * half, 0.2f, 0f), new Vector3(6f, 2.4f, 6f), rot, 10f);
			}
			Red(false).Box(s.At(0f, 41f, 0f), new Vector3(2f * half + 10f, 3.2f, 3.6f), rot, 12f);
			Flat("gantry banner", new Color(0.08f, 0.08f, 0.09f), false).Box(s.At(0f, 36.5f, -0.3f), new Vector3(2f * half - 4f, 6.4f, 0.8f), rot, 12f);
			Flat("gantry banner gold", Gold, false).Box(s.At(0f, 33.0f, -0.3f), new Vector3(2f * half - 4f, 0.7f, 0.9f), rot, 12f);
			labels.Add(new Label { Pos = s.At(0f, 36.5f, -0.9f), Facing = -s.F, Text = "TEMPLE OF DRIFT", Color = new Color(0.96f, 0.86f, 0.5f), Size = new Vector2(2f * half - 8f, 5.6f) });
			Keep(s.P, 60f);
		}

		// ---- hairpin chevron boards --------------------------------------------------------------------------------------------

		private static void Chevrons()
		{
			List<Pt> p = Data.Main;
			Batch board = Flat("chevron boards", new Color(0.07f, 0.07f, 0.08f), false);
			Batch bar = Flat("chevron arrows", new Color(0.98f, 0.82f, 0.10f), false);
			Batch post = Flat("chevron posts", new Color(0.55f, 0.57f, 0.60f), true);
			int count = 0;
			for (int i = 0; i < p.Count; i++)
			{
				if (!p[i].Hairpin || i % 3 != 0)
				{
					continue;
				}
				float turn = Data.Turn(p, i, true) + Data.Turn(p, (i + 1) % p.Count, true);
				float side = turn > 0f ? 1f : -1f;
				Vector3 f = Data.Forward(p, i, true), r = Data.Right(p, i, true);
				Vector3 baseP = p[i].P + r * side * (p[i].W * 0.5f + 11f);
				Vector3 n = -side * r;
				Quaternion rot = Quaternion.LookRotation(n, Vector3.up);
				Vector3 center = baseP + Vector3.up * 6f;
				board.Box(center, new Vector3(14f, 6.4f, 0.6f), rot, 10f);
				post.Box(baseP + Vector3.up * 1.6f, new Vector3(0.9f, 5.2f, 0.9f), rot, 10f);
				Footing(post, baseP, 0.45f, 0.45f, rot, baseP.y - 1.0f);				float sgn = Vector3.Dot(rot * Vector3.right, f) >= 0f ? 1f : -1f;
				foreach (float arm in new[] { 1f, -1f })
				{
					Vector3 c = center + rot * new Vector3(sgn * 0.8f, arm * 1.5f, 0.4f);
					Quaternion br = rot * Quaternion.Euler(0f, 0f, sgn * -arm * 38f);
					bar.Box(c, new Vector3(5.0f, 0.9f, 0.25f), br, 10f);
				}
				count++;
			}
			Mod.Log.LogInfo("Temple of Drift chevron boards: " + count);
		}
	}
}
