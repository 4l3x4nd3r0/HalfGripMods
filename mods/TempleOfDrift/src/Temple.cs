using System.Collections.Generic;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>
	/// The Buddhist temple the road runs through: after the tofu stand, a two-storey sanmon gate over the road, then stone lanterns, the
	/// hondo (main hall, hip-and-gable roof with deep eaves) on one side and a five-storey pagoda on the other. The ground round it is
	/// levelled in <c>make_terrain.py</c>, so the buildings stand on a stone podium at road height.
	/// </summary>
	internal static partial class Landmarks
	{
		private static int TempleIn
		{
			get { return Data.Index("temple_in"); }
		}

		// ---- the tofu stand and the car ---------------------------------------------------------------------------------------

		/// <summary>The shop is drawn this many times its natural size (1.2: about two car lengths wide, as in the photos of the real shop).</summary>
		private const float StandScale = 1.2f;

		private static Batch Glow(string name, Color c, float strength)
		{
			string key = name + "#glow";
			Batch b;
			if (!pool.TryGetValue(key, out b))
			{
				b = new Batch(name, Tex.White, c, false) { Emission = c * strength, Smooth = 0.3f };
				pool[key] = b;
				into.Add(b);
			}
			return b;
		}

		private static void TofuStand()
		{
			// at the start, behind the grid, on the right of the road: a small shop (about two car lengths wide, like the real one) facing the road,
			// a concrete car park that starts at the road's edge, and the AE86 parked sideways across two bays in front of the door
			Spot s = SpotAt(0, -60f);
			float K = StandScale;
			float front = 58f;                                  // across the road: where the shop's front stands
			Vector3 c = s.At(front + 9f * K, -0.6f, 0f);        // the shop's centre
			Vector3 face = -s.R;
			Quaternion rot = Quaternion.LookRotation(face, Vector3.up);
			System.Func<float, float, float, Vector3> L = (x, y, z) => c + rot * new Vector3(x * K, y * K, z * K);
			System.Func<float, float, float, Vector3> Z = (x, y, z) => new Vector3(x * K, y * K, z * K);
			Batch dark = Dark(true), darkD = Dark(true), plaster = Plaster(true);   // all of it solid: walls, posts, counter, crates, poles
			dark.Box(L(0f, 0.75f, 0f), Z(26f, 1.5f, 18f), rot, 10f);                              // the floor
			Footing(dark, c, 13f * K, 9f * K, rot, c.y);                                          // wherever the ground falls away under the shop
			foreach (float x in new[] { -11.5f, 11.5f })
			{
				foreach (float z in new[] { -7.5f, 7.5f })
				{
					darkD.Box(L(x, 6.0f, z), Z(1.3f, 9f, 1.3f), rot, 8f);
				}
			}
			plaster.Box(L(0f, 5.8f, -8.2f), Z(25f, 8.5f, 0.6f), rot, 12f);                          // back wall
			plaster.Box(L(-12.5f, 5.8f, 0f), Z(0.6f, 8.5f, 15f), rot, 12f);                        // side walls
			plaster.Box(L(12.5f, 5.8f, 0f), Z(0.6f, 8.5f, 15f), rot, 12f);
			darkD.Box(L(0f, 3.4f, 6.2f), Z(22f, 3.4f, 3.2f), rot, 10f);                            // the counter
			Roof(Tile(), darkD, L(0f, 10.5f, 0f), rot, 26f * K, 18f * K, 6.5f * K, 18f * K, 3.5f * K);
			Batch blue = Flat("noren blue", new Color(0.10f, 0.20f, 0.46f), false);
			Batch white = Flat("noren white", new Color(0.93f, 0.93f, 0.90f), false);
			blue.Box(L(0f, 7.6f, 9.2f), Z(19f, 5.2f, 0.3f), rot, 10f);
			white.Box(L(0f, 7.6f, 9.4f), Z(5.2f, 2.6f, 0.3f), rot, 10f);
			darkD.Box(L(-8f, 2.6f, -3.5f), Z(4.2f, 2.2f, 4.2f), rot, 8f);                          // crates and trays
			darkD.Box(L(-3.5f, 2.6f, -4.5f), Z(4.2f, 2.2f, 4.2f), rot, 8f);
			darkD.Box(L(-6f, 4.7f, -4f), Z(4.2f, 2.0f, 4.2f), rot, 8f);
			Flat("tofu", new Color(0.96f, 0.94f, 0.84f), false).Box(L(4f, 5.4f, 6.2f), Z(3.2f, 0.8f, 3.2f), rot, 8f);
			Flat("tofu", new Color(0.96f, 0.94f, 0.84f), false).Box(L(7.5f, 5.4f, 6.2f), Z(3.2f, 0.8f, 3.2f), rot, 8f);
			foreach (float x in new[] { -10f, 10f })
			{
				Glow("paper lantern", new Color(0.9f, 0.14f, 0.08f), 1.4f).Cone(L(x, 7.6f, 9.0f), 1.5f * K, L(x, 10.6f, 9.0f), 1.5f * K, 10, 8f, true);
			}
			// the sign and the two nobori stand beside the shop, clear of its roof: the eaves reach 16.5 (unscaled) to each side of the centre and
			// 15 forward, so everything here is outside x = +-16.5 (a pole or a board inside that went through the roof). The sign is on a stilt under
			// each end of its board, so nothing passes through the lettering.
			foreach (float xp in new[] { 22.2f, 31.8f })
			{
				darkD.Box(L(xp, 4.2f, 12f), Z(0.9f, 8.4f, 0.9f), rot, 8f);
			}
			Flat("shop sign", new Color(0.93f, 0.92f, 0.86f), true).Box(L(27f, 10.4f, 12f), Z(11f, 4.2f, 0.5f), rot, 8f);
			labels.Add(new Label { Pos = L(27f, 10.4f, 12.45f), Facing = face, Text = "KAZE TOFU", Color = new Color(0.08f, 0.10f, 0.28f), Size = new Vector2(10.4f * K, 3.6f * K) });
			Batch flagRed = Flat("nobori red", new Color(0.82f, 0.1f, 0.08f), true);
			Batch flagWhite = Flat("nobori white", new Color(0.95f, 0.94f, 0.9f), true);
			foreach (float x in new[] { -17.5f, 17.5f })
			{
				float outward = x < 0f ? -2.0f : 2.0f;   // the cloth hangs away from the shop
				darkD.Box(L(x, 9.5f, 11.5f), Z(0.6f, 19f, 0.6f), rot, 8f);
				flagRed.Box(L(x + outward, 10.5f, 11.5f), Z(3.2f, 14f, 0.25f), rot, 8f);
				flagWhite.Box(L(x + outward, 10.5f, 11.65f), Z(1.4f, 7f, 0.25f), rot, 8f);
			}			Keep(c, 34f);
			Keep(L(27f, 0f, 12f), 14f);   // round the sign

			// the car park: concrete level with the road, from the road's edge (it overlaps the shoulder) to a raised walk in front of the shop;
			// 6 bays of 9 along the road, 18.5 deep, white bay lines, wheel stops
			Quaternion padRot = Quaternion.LookRotation(s.F, Vector3.up);   // local x across the road, local z along it
			float padTop = s.P.y - 0.01f;
			float padFrom = 22f, padTo = 52f, walkTo = front - 0.6f;
			Batch concrete = B("parking concrete", Tex.Asphalt, new Color(1.05f, 1.05f, 1.05f), true);
			Vector3 padC = s.At((padFrom + padTo) * 0.5f, 0f, 0f);
			concrete.Box(new Vector3(padC.x, padTop - 0.4f, padC.z), new Vector3(padTo - padFrom, 0.8f, 54f), padRot, 36f);
			Footing(concrete, new Vector3(padC.x, 0f, padC.z), (padTo - padFrom) * 0.5f, 27f, padRot, padTop - 0.8f);
			Batch walk = B("parking walk", Tex.Stone, new Color(0.78f, 0.78f, 0.75f), true);
			Vector3 walkC = s.At((padTo + walkTo) * 0.5f, 0f, 0f);
			walk.Box(new Vector3(walkC.x, padTop + 0.14f - 0.4f, walkC.z), new Vector3(walkTo - padTo, 0.8f, 84f), padRot, 12f);
			Footing(walk, new Vector3(walkC.x, 0f, walkC.z), (walkTo - padTo) * 0.5f, 42f, padRot, padTop - 0.8f);
			Batch bayLine = Flat("parking lines", new Color(0.93f, 0.93f, 0.9f), false);
			Batch stop = Stone(false);
			float bayFrom = 33f, bayTo = 51.5f;
			Vector3 lineBase = new Vector3(padC.x, padTop + 0.03f, padC.z);
			for (int i = 0; i <= 6; i++)
			{
				bayLine.Box(s.At((bayFrom + bayTo) * 0.5f, 0f, 0f) + Vector3.up * (padTop + 0.03f - s.P.y) + s.F * (-27f + i * 9f), new Vector3(bayTo - bayFrom, 0.04f, 0.4f), padRot, 8f);
			}
			bayLine.Box(s.At(bayFrom, 0f, 0f) + Vector3.up * (padTop + 0.03f - s.P.y), new Vector3(0.4f, 0.04f, 54.4f), padRot, 8f);
			for (int i = 0; i < 6; i++)
			{
				stop.Box(s.At(bayTo - 0.9f, 0f, 0f) + Vector3.up * (padTop + 0.25f - s.P.y) + s.F * (-22.5f + i * 9f), new Vector3(0.7f, 0.5f, 3.6f), padRot, 6f);
			}
			// the AE86 sideways across the two middle bays (the line between them runs under its middle), a little angled, like parked by someone
			// who does not care about the lines
			Vector3 carAt = s.At(42.5f, 0f, 0f);
			carAt.y = padTop + 0.05f;
			Ae86(carAt, Quaternion.AngleAxis(-7f, Vector3.up) * s.F, 1f);
			Keep(padC, 40f);
			Keep(carAt, 16f);
		}
		// ---- the sanmon gate ----------------------------------------------------------------------------------------------------

		private static void Sanmon()
		{
			Spot s = SpotAt(TempleIn, 126f);
			Quaternion rot = s.Rot;
			Batch red = Red(true), redD = Red(false), dark = Dark(false), plasterC = Plaster(true), plasterD = Plaster(false), stone = Stone(true);
			float[] cols = { -60f, -42f, -26f, 26f, 42f, 60f };
			foreach (float x in cols)
			{
				foreach (float z in new[] { -12f, 12f })
				{
					red.Pillar(s.At(x, -1.5f, z), 2.3f, 32f, 10, 12f);
					stone.Box(s.At(x, 0.3f, z), new Vector3(6.4f, 2.4f, 6.4f), rot, 10f);
					Footing(stone, s.At(x, 0f, z), 3.2f, 3.2f, rot, s.P.y - 0.9f);
				}
			}
			// the side bays are closed with plaster walls; the central bay is the road
			foreach (float side in new[] { -1f, 1f })
			{
				plasterC.Box(s.At(side * 43f, 15f, 0f), new Vector3(34f, 30f, 2.4f), rot, 12f);
				stone.Box(s.At(side * 43f, 1.2f, 0f), new Vector3(36f, 3f, 6f), rot, 10f);
				Footing(stone, s.At(side * 43f, 0f, 0f), 18f, 3f, rot, s.P.y - 0.3f);
			}
			// beams and the bracket layer under the lower roof
			redD.Box(s.At(0f, 33.5f, 0f), new Vector3(132f, 3.4f, 30f), rot, 12f);
			dark.Box(s.At(0f, 36.2f, 0f), new Vector3(136f, 2.4f, 36f), rot, 10f);
			Roof(Tile(), dark, s.At(0f, 37.5f, 0f), rot, 128f, 40f, 9f, 100f, 11f);
			// upper storey: plaster between red posts, a balcony rail round it
			plasterD.Box(s.At(0f, 52.5f, 0f), new Vector3(104f, 22f, 28f), rot, 12f);
			for (int i = -3; i <= 3; i++)
			{
				redD.Box(s.At(i * 17f, 52.5f, 14.6f), new Vector3(2.4f, 22f, 2.4f), rot, 10f);
				redD.Box(s.At(i * 17f, 52.5f, -14.6f), new Vector3(2.4f, 22f, 2.4f), rot, 10f);
			}
			redD.Box(s.At(0f, 44.5f, 17.5f), new Vector3(112f, 2f, 1.6f), rot, 10f);
			redD.Box(s.At(0f, 44.5f, -17.5f), new Vector3(112f, 2f, 1.6f), rot, 10f);
			redD.Box(s.At(0f, 63.8f, 0f), new Vector3(108f, 2.6f, 32f), rot, 10f);
			Roof(Tile(), dark, s.At(0f, 65f, 0f), rot, 104f, 28f, 20f, 62f, 15f);
			// the name board hung on the front of the upper storey: black with a gold frame, readable from the road
			Flat("plaque gold", Gold, false).Box(s.At(0f, 53f, -15.6f), new Vector3(54f, 15f, 1.2f), rot, 8f);
			Flat("plaque black", Black, false).Box(s.At(0f, 53f, -16.3f), new Vector3(50f, 12f, 1.2f), rot, 8f);
			labels.Add(new Label { Pos = s.At(0f, 53f, -17.0f), Facing = -s.F, Text = "TEMPLE OF DRIFT", Color = new Color(0.97f, 0.85f, 0.45f), Size = new Vector2(47f, 9.5f) });
			Keep(s.P, 95f);
		}

		// ---- the compound: lanterns, hondo, pagoda -----------------------------------------------------------------------------------

		private static void Courtyard()
		{
			int t0 = TempleIn;
			// stone lanterns along both sides of the road after the gate
			for (float along = 168f; along <= 264f; along += 24f)
			{
				foreach (float side in new[] { -1f, 1f })
				{
					Lantern(SpotAt(t0, along).At(side * 36f, -1.3f, 0f));
				}
			}
			Hondo(SpotAt(t0, 214f));
			Pagoda(SpotAt(t0, 176f));
		}

		/// <summary>Positions of the lantern flames that are real lights (the cave mouths); Cave.MakeLights turns them into point lights.</summary>
		internal static readonly List<Vector3> LitLanterns = new List<Vector3>();

		/// <summary>
		/// A stone toro (tourou): plinth, base, shaft with a ring, lotus seat, platform, a firebox with four open windows, a curved roof with a
		/// jewel finial. It always stands on the ground: the plinth runs down to the terrain under it, wherever p is.
		/// </summary>
		internal static void Lantern(Vector3 p, bool lit = false)
		{
			float ground = Data.HeightAt(p.x, p.z);
			float y0 = Mathf.Max(ground, Mathf.Min(p.y, ground + 1.5f)) + 0.5f;   // top of the plinth
			float sink = Mathf.Max(3f, y0 - ground + 3f);
			// the plinth stands on the lowest ground under its 6.2 square, so no corner hangs
			float low = GroundMin(new Vector3(p.x, 0f, p.z), 3.1f, 3.1f, Quaternion.identity);
			sink = Mathf.Max(sink, y0 - low + 1.5f);
			LanternOn(new Vector3(p.x, y0, p.z), sink, lit);
		}

		/// <summary>A lantern inside the cave: <paramref name="floor"/> is the floor under it (the hill above is no help there).</summary>
		internal static void CaveLantern(Vector3 floor)
		{
			LanternOn(new Vector3(floor.x, floor.y + 0.5f, floor.z), 3f, true, true);
		}

		private static void LanternOn(Vector3 b, float sink, bool lit, bool cave = false)
		{
			// in the cave the batches carry the cave's name, so the culler shows them with the cave and not with the outdoors
			Batch stone = cave ? B("cave lantern stone", Tex.Stone, Color.white, false) : Stone(true);   // in the cave nothing solid: it stands outside the guide wall
			Batch stoneN = cave ? B("cave lantern stone", Tex.Stone, Color.white, false) : Stone(false);
			Quaternion rot = Quaternion.identity;
			stone.Box(b + Vector3.down * (sink * 0.5f), new Vector3(6.2f, sink, 6.2f), rot, 8f);                // plinth, runs into the ground
			stone.Box(b + Vector3.up * 0.4f, new Vector3(5.0f, 0.8f, 5.0f), rot, 8f);                           // step
			stone.Box(b + Vector3.up * 1.4f, new Vector3(3.6f, 1.2f, 3.6f), rot, 8f);                           // base
			stone.Pillar(b + Vector3.up * 1.9f, 0.85f, 5.4f, 8, 8f);                                            // shaft
			stoneN.Cone(b + Vector3.up * 4.3f, 1.25f, b + Vector3.up * 5.0f, 1.25f, 8, 8f, true);              // the ring on the shaft
			stoneN.Cone(b + Vector3.up * 7.2f, 0.9f, b + Vector3.up * 8.2f, 2.5f, 8, 8f, true);                 // lotus seat flaring out
			stone.Box(b + Vector3.up * 8.55f, new Vector3(4.4f, 0.7f, 4.4f), rot, 8f);                          // platform
			// the firebox: four corner posts and a lit core, so the windows are open
			foreach (float sx in new[] { -1f, 1f })
			{
				foreach (float sz in new[] { -1f, 1f })
				{
					stone.Pillar(b + new Vector3(sx * 1.45f, 8.9f, sz * 1.45f), 0.38f, 3.0f, 6, 8f);
				}
			}
			Glow(cave ? "cave lantern glow" : "lantern glow", new Color(1f, 0.78f, 0.38f), lit ? 2.4f : 1.4f).Box(b + Vector3.up * 10.4f, new Vector3(1.9f, 1.9f, 1.9f), rot, 8f);
			stoneN.Box(b + Vector3.up * 12.05f, new Vector3(4.2f, 0.5f, 4.2f), rot, 8f);                         // eave plate
			Roof(stoneN, null, b + Vector3.up * 12.3f, rot, 5.2f, 5.2f, 2.4f, 0f, 1.0f);                  // the roof
			stoneN.Cone(b + Vector3.up * 14.4f, 0.9f, b + Vector3.up * 15.4f, 0.2f, 8, 8f, true);               // the jewel
			stoneN.Sphere(b + Vector3.up * 14.9f, new Vector3(0.7f, 0.7f, 0.7f), 3, 6, 8f);
			if (lit)
			{
				LitLanterns.Add(b + Vector3.up * 10.4f);
			}
		}

		/// <summary>The main hall, set back on the north side, facing the road: a stone podium, red pillars, plaster walls, a double roof.</summary>
		private static void Hondo(Spot s)
		{
			Vector3 c = s.At(132f, -1.3f, 0f);
			Quaternion rot = Quaternion.LookRotation(-s.R, Vector3.up);   // front (+z) faces the road
			System.Func<float, float, float, Vector3> L = (x, y, z) => c + rot * new Vector3(x, y, z);
			Batch stone = Stone(true), red = Red(true), redD = Red(false), plasterC = Plaster(true), dark = Dark(false);
			stone.Box(L(0f, -6.5f, 0f), new Vector3(112f, 29f, 76f), rot, 12f);                  // podium, top at +8, sunk 21 into the ground
			Footing(stone, c, 56f, 38f, rot, c.y - 21f);
			Footing(stone, L(0f, 0f, 44f), 19f, 6.5f, rot, c.y);                                 // under the stairs
			for (int i = 0; i < 3; i++)
			{
				stone.Box(L(0f, 1.2f + i * 2.4f, 40f + (2 - i) * 4f), new Vector3(38f, 2.4f, 5f), rot, 10f);   // stairs toward the road
			}
			plasterC.Box(L(0f, 20f, 0f), new Vector3(92f, 24f, 52f), rot, 12f);                  // the closed core
			for (int i = -5; i <= 5; i++)
			{
				red.Pillar(L(i * 9.6f, 8f, 30f), 2.0f, 27f, 10, 12f);                            // the front colonnade
			}
			foreach (float x in new[] { -46f, 46f })
			{
				foreach (float z in new[] { -26f, 26f })
				{
					red.Pillar(L(x, 8f, z), 2.2f, 27f, 10, 12f);
				}
			}
			redD.Box(L(0f, 35.5f, 15f), new Vector3(108f, 3f, 56f), rot, 10f);                   // beam layer
			dark.Box(L(0f, 38f, 15f), new Vector3(112f, 2.4f, 60f), rot, 10f);
			Roof(Tile(), dark, L(0f, 39.5f, 0f), rot, 104f, 66f, 15f, 70f, 14f);                  // lower hip roof
			plasterC.Box(L(0f, 50f, 0f), new Vector3(60f, 14f, 38f), rot, 12f);                  // the clerestory (the "gable" of the irimoya)
			Roof(Tile(), dark, L(0f, 55.5f, 0f), rot, 64f, 42f, 17f, 44f, 7f);                   // upper roof
			Flat("roof ornament", Gold, false).Box(L(24f, 76f, 0f), new Vector3(2.4f, 5f, 2.4f), rot, 6f);
			Flat("roof ornament", Gold, false).Box(L(-24f, 76f, 0f), new Vector3(2.4f, 5f, 2.4f), rot, 6f);
			Keep(c, 90f);
		}

		/// <summary>A five-storey pagoda on the south side: each storey a plaster box and a pyramid roof, a finial on top.</summary>
		private static void Pagoda(Spot s)
		{
			Vector3 c = s.At(-118f, -1.3f, 0f);
			Quaternion rot = Quaternion.LookRotation(s.R, Vector3.up);
			System.Func<float, float, float, Vector3> L = (x, y, z) => c + rot * new Vector3(x, y, z);
			Batch stone = Stone(true), red = Red(true), redD = Red(false), plasterC = Plaster(true), dark = Dark(false);
			stone.Box(L(0f, -7.5f, 0f), new Vector3(54f, 29f, 54f), rot, 12f);
			Footing(stone, c, 27f, 27f, rot, c.y - 22f);
			float y = 7f;
			for (int k = 0; k < 5; k++)
			{
				float side = 26f - k * 3.4f;
				Batch walls = k == 0 ? plasterC : Plaster(false);
				walls.Box(L(0f, y + 7f, 0f), new Vector3(side * 0.8f, 14f, side * 0.8f), rot, 12f);
				foreach (float dx in new[] { -1f, 1f })
				{
					foreach (float dz in new[] { -1f, 1f })
					{
						(k == 0 ? red : redD).Pillar(L(dx * side * 0.4f, y, dz * side * 0.4f), 1.4f, 14f, 8, 10f);
					}
				}
				Roof(Tile(), dark, L(0f, y + 14f, 0f), rot, side, side, 6.5f, 0f, 7f);
				y += 14f + 5.5f;
			}
			// the finial: a mast with rings
			Flat("finial", Gold, false).Cone(L(0f, y, 0f), 0.9f, L(0f, y + 22f, 0f), 0.35f, 8, 6f);
			for (int i = 0; i < 6; i++)
			{
				Flat("finial", Gold, false).Cone(L(0f, y + 3f + i * 3f, 0f), 2.4f - i * 0.25f, L(0f, y + 3.9f + i * 3f, 0f), 2.4f - i * 0.25f, 10, 6f, true);
			}
			Keep(c, 60f);
		}
	}
}
