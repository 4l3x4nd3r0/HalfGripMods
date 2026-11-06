using System;
using System.Collections.Generic;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>A rectangle on the ground the terrain leaves open, because a portal block stands there.</summary>
	internal struct HoleRect
	{
		public Vector2 Origin;
		public Vector2 Forward;
		public float From, To, Half;
		/// <summary>Half width at <see cref="To"/>; the hole is a trapezoid from Half at From to HalfEnd at To (0 = a plain rectangle).</summary>
		public float HalfEnd;
		/// <summary>The height of the road at the mouth: the curtain along a cut edge reaches down to 6 below it.</summary>
		public float FloorY;

		public bool Contains(float x, float z)
		{
			float rx = x - Origin.x, rz = z - Origin.y;
			float u = rx * Forward.x + rz * Forward.y;
			float l = rx * Forward.y - rz * Forward.x;
			if (u < From || u > To)
			{
				return false;
			}
			float half = HalfEnd > 0f ? Mathf.Lerp(Half, HalfEnd, (u - From) / Mathf.Max(0.01f, To - From)) : Half;
			return Mathf.Abs(l) <= half;
		}

		/// <summary>The point's place in the hole's own frame: <paramref name="u"/> along the way into the cave, <paramref name="l"/> across it.</summary>
		public void Local(float x, float z, out float u, out float l)
		{
			float rx = x - Origin.x, rz = z - Origin.y;
			u = rx * Forward.x + rz * Forward.y;
			l = rx * Forward.y - rz * Forward.x;
		}

		/// <summary>Is a point within <paramref name="margin"/> of the hole (so a terrain cell there has to be cut finely)?</summary>
		public bool Near(float x, float z, float margin)
		{
			float rx = x - Origin.x, rz = z - Origin.y;
			float u = rx * Forward.x + rz * Forward.y;
			float l = rx * Forward.y - rz * Forward.x;
			return u >= From - margin && u <= To + margin && Mathf.Abs(l) <= Mathf.Max(Half, HalfEnd) + margin;
		}
	}

	/// <summary>
	/// The mountain: a grid of heights (data\terrain.bin) built into 32 x 32 cell chunks with smooth normals, a grass mesh and a rock mesh
	/// (steep cells), and a collider each. The roads sit about a metre above it; the carve along them is in the data.
	/// </summary>
	internal static class Mountain
	{
		private const int ChunkCells = 32;
		private const float Tile = 56f;

		internal static int Build(Transform parent, int layer, List<HoleRect> holes, out int triangles)
		{
			triangles = 0;
			int nx = Data.Nx, nz = Data.Nz;
			float[] h = Data.Heights;
			float cell = Data.Cell;
			Material grass = Mats.Get(Tex.Grass, Color.white, true);   // two-sided: from inside the cave a gap shows rock, not sky
			Material rock = Mats.Get(Tex.Rock, Color.white, true);
			int chunks = 0;
			for (int cz0 = 0; cz0 < nz - 1; cz0 += ChunkCells)
			{
				for (int cx0 = 0; cx0 < nx - 1; cx0 += ChunkCells)
				{
					int w = Mathf.Min(ChunkCells, nx - 1 - cx0);
					int d = Mathf.Min(ChunkCells, nz - 1 - cz0);
					int vw = w + 1;
					Vector3[] verts = new Vector3[(w + 1) * (d + 1)];
					Vector3[] norms = new Vector3[verts.Length];
					Vector2[] uvs = new Vector2[verts.Length];
					for (int j = 0; j <= d; j++)
					{
						for (int i = 0; i <= w; i++)
						{
							int gx = cx0 + i, gz = cz0 + j;
							float x = Data.X0 + gx * cell, z = Data.Z0 + gz * cell, y = h[gz * nx + gx];
							int k = j * vw + i;
							verts[k] = new Vector3(x, y, z);
							float hl = h[gz * nx + Mathf.Max(gx - 1, 0)], hr = h[gz * nx + Mathf.Min(gx + 1, nx - 1)];
							float hd = h[Mathf.Max(gz - 1, 0) * nx + gx], hu = h[Mathf.Min(gz + 1, nz - 1) * nx + gx];
							norms[k] = new Vector3(hl - hr, 2f * cell, hd - hu).normalized;
							uvs[k] = new Vector2(x / Tile, (z + y * 0.9f) / Tile);
						}
					}
					List<int> grassTris = new List<int>();
					List<int> rockTris = new List<int>();
					// cells next to a hole are cut into 2-unit pieces and only the pieces whose centre is in the hole are left out, so the edge of the
					// hole is within 2 units of the real outline (a whole 14-unit cell left out left strips with nothing in them beside the mounds)
					List<Vector3> extraV = new List<Vector3>();
					List<Vector3> extraN = new List<Vector3>();
					List<Vector2> extraUV = new List<Vector2>();
					const int Sub = 7;
					for (int j = 0; j < d; j++)
					{
						for (int i = 0; i < w; i++)
						{
							float cx = Data.X0 + (cx0 + i + 0.5f) * cell, cz = Data.Z0 + (cz0 + j + 0.5f) * cell;
							bool near = false;
							for (int hi = 0; hi < holes.Count; hi++)
							{
								if (holes[hi].Near(cx, cz, cell * 1.2f))
								{
									near = true;
									break;
								}
							}
							int a = j * vw + i, b = a + 1, c = a + vw, e = c + 1;
							float ny = (norms[a].y + norms[b].y + norms[c].y + norms[e].y) * 0.25f;
							List<int> into = ny < 0.72f ? rockTris : grassTris;
							if (!near)
							{
								into.Add(a); into.Add(c); into.Add(b);
								into.Add(b); into.Add(c); into.Add(e);
								continue;
							}
							float x0c = Data.X0 + (cx0 + i) * cell, z0c = Data.Z0 + (cz0 + j) * cell;
							for (int sj = 0; sj < Sub; sj++)
							{
								for (int si = 0; si < Sub; si++)
								{
									float u0 = si / (float)Sub, u1 = (si + 1) / (float)Sub, v0 = sj / (float)Sub, v1 = (sj + 1) / (float)Sub;
									float wx = x0c + (u0 + u1) * 0.5f * cell, wz = z0c + (v0 + v1) * 0.5f * cell;
									bool open = false;
									for (int hi = 0; hi < holes.Count; hi++)
									{
										if (holes[hi].Contains(wx, wz))
										{
											open = true;
											break;
										}
									}
									if (open)
									{
										continue;
									}
									int bi = verts.Length + extraV.Count;
									float[] us = { u0, u1, u0, u1 }, vs = { v0, v0, v1, v1 };
									for (int q = 0; q < 4; q++)
									{
										Vector3 pl = Vector3.Lerp(Vector3.Lerp(verts[a], verts[b], us[q]), Vector3.Lerp(verts[c], verts[e], us[q]), vs[q]);
										Vector3 nl = Vector3.Lerp(Vector3.Lerp(norms[a], norms[b], us[q]), Vector3.Lerp(norms[c], norms[e], us[q]), vs[q]).normalized;
										Vector2 ul = Vector2.Lerp(Vector2.Lerp(uvs[a], uvs[b], us[q]), Vector2.Lerp(uvs[c], uvs[e], us[q]), vs[q]);
										extraV.Add(pl);
										extraN.Add(nl);
										extraUV.Add(ul);
									}
									// the piece's four corners are bi (u0,v0), bi+1 (u1,v0), bi+2 (u0,v1), bi+3 (u1,v1): the same winding as the whole cell
									into.Add(bi); into.Add(bi + 2); into.Add(bi + 1);
									into.Add(bi + 1); into.Add(bi + 2); into.Add(bi + 3);
									// a curtain of rock down every cut edge: where this piece borders a removed one (the staircase of the hole, measured
									// by the neighbour's centre), a vertical strip from the edge of the terrain down to 6 under the road, so there is no
									// gap between the terrain and the mound at any height. Not across the tube's own width on the far side of the hole
									// (that would be a wall inside the tunnel).
									float step = cell / Sub;
									float[] nxs = { -step, step, 0f, 0f }, nzs = { 0f, 0f, -step, step };
									int[] ea = { 0, 1, 0, 2 }, eb = { 2, 3, 1, 3 };
									for (int side = 0; side < 4; side++)
									{
										float qx = wx + nxs[side], qz = wz + nzs[side];
										int owner = -1;
										for (int hi = 0; hi < holes.Count; hi++)
										{
											if (holes[hi].Contains(qx, qz))
											{
												owner = hi;
												break;
											}
										}
										if (owner < 0)
										{
											continue;
										}
										float hu, hl;
										holes[owner].Local(wx, wz, out hu, out hl);
										if (Mathf.Abs(hl) < 28f)
										{
											continue;   // never across the road or the tunnel's width: a thin wall at road level is a bump, inside the tunnel a wall
										}
										Vector3 ta = extraV[bi + ea[side] - verts.Length], tb = extraV[bi + eb[side] - verts.Length];
										float bottom = holes[owner].FloorY - 6f;
										int ci = verts.Length + extraV.Count;
										Vector3 outward = new Vector3(nxs[side], 0f, nzs[side]).normalized;
										extraV.Add(ta + Vector3.up * 0.2f); extraV.Add(tb + Vector3.up * 0.2f); extraV.Add(new Vector3(ta.x, Mathf.Min(bottom, ta.y - 1f), ta.z)); extraV.Add(new Vector3(tb.x, Mathf.Min(bottom, tb.y - 1f), tb.z));
										for (int q = 0; q < 4; q++)
										{
											extraN.Add(outward);
											Vector3 pv = extraV[ci - verts.Length + q];   // ci is a mesh index (it counts the grid's own vertices); the list starts after them
											extraUV.Add(new Vector2(pv.x / Tile, (pv.z + pv.y * 0.9f) / Tile));
										}
										rockTris.Add(ci); rockTris.Add(ci + 2); rockTris.Add(ci + 1);
										rockTris.Add(ci + 1); rockTris.Add(ci + 2); rockTris.Add(ci + 3);
									}
								}
							}
						}
					}
					if (extraV.Count > 0)
					{
						Vector3[] allV = new Vector3[verts.Length + extraV.Count];
						Vector3[] allN = new Vector3[allV.Length];
						Vector2[] allUV = new Vector2[allV.Length];
						verts.CopyTo(allV, 0); norms.CopyTo(allN, 0); uvs.CopyTo(allUV, 0);
						extraV.CopyTo(allV, verts.Length); extraN.CopyTo(allN, verts.Length); extraUV.CopyTo(allUV, verts.Length);
						verts = allV; norms = allN; uvs = allUV;
					}					if (grassTris.Count + rockTris.Count == 0)
					{
						continue;
					}
					Mesh mesh = new Mesh { name = "ToD_Terrain_" + cx0 + "_" + cz0 };
					mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
					mesh.vertices = verts;
					mesh.normals = norms;
					mesh.uv = uvs;
					mesh.subMeshCount = 2;
					mesh.SetTriangles(grassTris, 0);
					mesh.SetTriangles(rockTris, 1);
					mesh.RecalculateBounds();
					GameObject go = new GameObject("Terrain_" + cx0 + "_" + cz0) { layer = layer };
					go.transform.SetParent(parent, false);
					go.AddComponent<MeshFilter>().sharedMesh = mesh;
					MeshRenderer tr = go.AddComponent<MeshRenderer>();
					tr.sharedMaterials = new[] { grass, rock };
					tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
					tr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
					tr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
					PerfCuller.Register(tr, false, false);
					go.AddComponent<MeshCollider>().sharedMesh = mesh;
					HandlingModifier handling = go.AddComponent<HandlingModifier>();
					handling.gripModifier = 0.7f;
					handling.resistanceModifier = 3f;
					handling.background = true;
					triangles += (grassTris.Count + rockTris.Count) / 3;
					chunks++;
				}
			}
			// a dark rock plate 5.5 under the road across every hole (the curtains hang to 6 under it), so a sliver left between a cut edge and
			// the mound can only ever show rock, never what is behind the map
			Material plate = Mats.Get(Tex.Rock, new Color(0.45f, 0.45f, 0.45f), true);
			foreach (HoleRect hole in holes)
			{
				Vector2 sd = new Vector2(hole.Forward.y, -hole.Forward.x);
				float u0 = hole.From - 3f, u1 = hole.To + 3f;
				float hw0 = Mathf.Max(hole.Half, 10f) + 3f, hw1 = (hole.HalfEnd > 0f ? hole.HalfEnd : hole.Half) + 3f;
				Func<float, float, Vector3> world = (u, l) => new Vector3(hole.Origin.x + hole.Forward.x * u + sd.x * l, hole.FloorY - 5.5f, hole.Origin.y + hole.Forward.y * u + sd.y * l);
				Mesh pm = new Mesh { name = "ToD_HolePlate" };
				pm.vertices = new[] { world(u0, -hw0), world(u0, hw0), world(u1, hw1), world(u1, -hw1) };
				pm.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
				pm.triangles = new[] { 0, 1, 2, 0, 2, 3 };
				pm.RecalculateNormals();
				pm.RecalculateBounds();
				GameObject pg = new GameObject("hole plate") { layer = layer };
				pg.transform.SetParent(parent, false);
				pg.AddComponent<MeshFilter>().sharedMesh = pm;
				MeshRenderer pr = pg.AddComponent<MeshRenderer>();
				pr.sharedMaterial = plate;
				pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				pr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
				pr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
				PerfCuller.Register(pr, false, false);
			}
			return chunks;
		}
	}
}
