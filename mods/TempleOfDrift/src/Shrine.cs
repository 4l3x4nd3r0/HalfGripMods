using System.Collections.Generic;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>
	/// The Shinto part, deeper in the forest after the temple: a large myojin torii over the road (two vermilion pillars leaning in, a nuki
	/// tie beam, a red shimaki under a black kasagi whose ends curve up, a plaque, a shimenawa rope with shide), stone lanterns beside it and
	/// then an avenue of smaller torii that carries on to where the road turns to dirt.
	/// </summary>
	internal static partial class Landmarks
	{
		/// <summary>
		/// One torii, after the temple gate and the hondo, close to the road and not tall (30 to the underside of the kasagi): the way out of
		/// the temple grounds. (Fushimi Inari's tunnels of hundreds of small torii belong to that shrine's own mountain path, not to a temple
		/// on a road, so the pass has just the one.)
		/// </summary>
		private static void Torii()
		{
			int s0 = Data.Index("shrine_in");
			Spot gate = SpotAt(s0, 34f);
			ToriiAt(gate, 29f, 30f, 0.8f, true);
			foreach (float side in new[] { -1f, 1f })
			{
				Lantern(gate.At(side * 40f, -0.8f, 6f));
			}
			Keep(gate.P, 46f);
			Mod.Log.LogInfo("Temple of Drift torii: one, 30 high");
		}

		/// <summary>One torii on the road at <paramref name="s"/>: pillars at +-half, <paramref name="height"/> to the underside of the kasagi, <paramref name="k"/> scales the timber.</summary>
		private static void ToriiAt(Spot s, float half, float height, float k, bool grand)
		{
			Quaternion rot = s.Rot;
			Batch red = Red(false), redC = Red(true);
			Batch black = Flat("torii black", Black, false);
			Batch plinth = Flat("torii plinth", Black, true);

			foreach (float side in new[] { -1f, 1f })
			{
				float r0 = 2.7f * k, r1 = 2.2f * k, lean = 1.5f * k;
				redC.Cone(s.At(side * half, -1.5f, 0f), r0, s.At(side * (half - lean), height, 0f), r1, 12, 12f, false);
				plinth.Cone(s.At(side * half, -1.5f, 0f), r0 * 1.3f, s.At(side * half, 2.2f * k, 0f), r0 * 1.12f, 12, 10f, true);
				Footing(plinth, s.At(side * half, 0f, 0f), r0 * 1.2f, r0 * 1.2f, rot, s.P.y - 1.5f);
			}
			float inner = half - 1.5f * k;
			// nuki: the tie beam, sticking out past the pillars
			red.Box(s.At(0f, height * 0.74f, 0f), new Vector3(2f * (half + 3.6f * k), 3.0f * k, 2.5f * k), rot, 12f);
			// shimaki: the lower, red top beam; kasagi: the black one with ends that curve up
			float topY = height + 1.6f * k;
			red.Box(s.At(0f, topY, 0f), new Vector3(2f * (half + 8.5f * k), 3.4f * k, 5.2f * k), rot, 12f);
			float halfTop = half + 12.5f * k;
			int segs = 13;
			float segW = 2f * halfTop / segs;
			float rise = 5.5f * k;
			for (int j = 0; j < segs; j++)
			{
				float x = -halfTop + (j + 0.5f) * segW;
				float u = x / halfTop;
				float y = topY + 3.2f * k + rise * u * u * u * u * 1.0f + rise * 0.35f * u * u;
				float slope = (4f * rise * u * u * u + 2f * rise * 0.35f * u) / halfTop;
				float ang = Mathf.Atan(slope) * Mathf.Rad2Deg;
				black.Box(s.At(x, y, 0f), new Vector3(segW * 1.18f, 3.6f * k, 6.4f * k), rot * Quaternion.Euler(0f, 0f, ang), 12f);
			}
			if (grand)
			{
				// the gakuzuka plaque between the nuki and the shimaki, black with a gold frame
				float py = (height * 0.74f + topY) * 0.5f + 1f;
				Flat("torii gold", Gold, false).Box(s.At(0f, py, 0f), new Vector3(9.4f, 9.4f, 1.5f), rot, 8f);
				black.Box(s.At(0f, py, 0f), new Vector3(8.2f, 8.2f, 1.8f), rot, 8f);
				// the shimenawa: a thick straw rope that sags between the pillars, with white paper shide
				Batch rope = Flat("shimenawa", new Color(0.78f, 0.68f, 0.42f), false);
				Batch paper = Flat("shide", new Color(0.96f, 0.96f, 0.94f), false);
				float ry = height * 0.56f;
				int pieces = 14;
				float w = inner * 2f - 3f;
				for (int j = 0; j < pieces; j++)
				{
					float x = -w * 0.5f + (j + 0.5f) * w / pieces;
					float u = x / (w * 0.5f);
					float y = ry - 3.4f * (1f - u * u);
					float nextU = (x + w / pieces) / (w * 0.5f);
					float ang = Mathf.Atan2(-3.4f * ((1f - nextU * nextU) - (1f - u * u)), w / pieces) * Mathf.Rad2Deg;
					rope.Box(s.At(x, y, 0f), new Vector3(w / pieces * 1.15f, 1.7f, 1.7f), rot * Quaternion.Euler(0f, 0f, ang), 6f);
				}
				foreach (float x in new[] { -14f, -7f, 0f, 7f, 14f })
				{
					float u = x / (w * 0.5f);
					float y = ry - 3.4f * (1f - u * u) - 3.2f;
					paper.Box(s.At(x, y, 0f), new Vector3(1.8f, 4.4f, 0.18f), rot * Quaternion.Euler(0f, 0f, 0f), 6f);
					paper.Box(s.At(x + 0.6f, y - 3.2f, 0.2f), new Vector3(1.8f, 2.6f, 0.18f), rot, 6f);
				}
			}
		}
	}
}
