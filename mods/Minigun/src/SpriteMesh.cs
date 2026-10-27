using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalfGripMinigun
{
	/// <summary>
	/// Builds the thin extruded "voxel" meshes the game uses for a gun in your hands, from a sprite's opaque pixels.
	/// Each part states where pixel (0,0), the sprite's top left, sits in the mesh and how big a pixel is, taken from the
	/// game's own meshes, so the new model drops into the same place as the one it replaces.
	/// </summary>
	internal static class SpriteMesh
	{
		public struct Frame
		{
			public float x0;
			public float xDir;
			public float dx;
			public float z0;
			public float dz;
			public float yMin;
			public float yMax;
		}

		public static Mesh Build(Texture2D texture, Frame f, string name)
		{
			int w = texture.width;
			int h = texture.height;
			Color32[] pixels = texture.GetPixels32();

			bool Opaque(int px, int py)
			{
				if (px < 0 || py < 0 || px >= w || py >= h)
				{
					return false;
				}
				// Texture rows run bottom to top, sprite rows top to bottom.
				return pixels[(h - 1 - py) * w + px].a > 8;
			}

			float X(float px) => f.x0 + f.xDir * px * f.dx;
			float Z(float py) => f.z0 + py * f.dz;

			var verts = new List<Vector3>();
			var normals = new List<Vector3>();
			var uvs = new List<Vector2>();
			var tris = new List<int>();

			void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
			{
				int i = verts.Count;
				verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
				normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
				uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
				// Unity treats the clockwise side as the front, so wind each triangle to face along n.
				if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0f)
				{
					tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
					tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
				}
				else
				{
					tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
					tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
				}
			}

			for (int py = 0; py < h; py++)
			{
				float zt = Z(py);
				float zb = Z(py + 1);
				float vTop = 1f - py / (float)h;
				float vBot = 1f - (py + 1) / (float)h;
				float vMid = 1f - (py + 0.5f) / h;

				int px = 0;
				while (px < w)
				{
					if (!Opaque(px, py))
					{
						px++;
						continue;
					}
					int a = px;
					while (px < w && Opaque(px, py))
					{
						px++;
					}
					int b = px;
					float xa = X(a);
					float xb = X(b);
					float u0 = a / (float)w;
					float u1 = b / (float)w;

					// Both faces.
					Quad(new Vector3(xa, f.yMax, zt), new Vector3(xb, f.yMax, zt), new Vector3(xb, f.yMax, zb), new Vector3(xa, f.yMax, zb),
						Vector3.up, new Vector2(u0, vTop), new Vector2(u1, vTop), new Vector2(u1, vBot), new Vector2(u0, vBot));
					Quad(new Vector3(xa, f.yMin, zt), new Vector3(xb, f.yMin, zt), new Vector3(xb, f.yMin, zb), new Vector3(xa, f.yMin, zb),
						Vector3.down, new Vector2(u0, vTop), new Vector2(u1, vTop), new Vector2(u1, vBot), new Vector2(u0, vBot));

					// Ends of the run.
					float uA = (a + 0.5f) / w;
					float uB = (b - 0.5f) / w;
					Quad(new Vector3(xa, f.yMin, zt), new Vector3(xa, f.yMin, zb), new Vector3(xa, f.yMax, zb), new Vector3(xa, f.yMax, zt),
						new Vector3(-f.xDir, 0f, 0f), new Vector2(uA, vMid), new Vector2(uA, vMid), new Vector2(uA, vMid), new Vector2(uA, vMid));
					Quad(new Vector3(xb, f.yMin, zt), new Vector3(xb, f.yMin, zb), new Vector3(xb, f.yMax, zb), new Vector3(xb, f.yMax, zt),
						new Vector3(f.xDir, 0f, 0f), new Vector2(uB, vMid), new Vector2(uB, vMid), new Vector2(uB, vMid), new Vector2(uB, vMid));

					// Top and bottom edges, merged along the run where the neighbour row is empty.
					for (int pass = 0; pass < 2; pass++)
					{
						int ny = pass == 0 ? py - 1 : py + 1;
						float zEdge = pass == 0 ? zt : zb;
						Vector3 n = pass == 0 ? new Vector3(0f, 0f, -1f) : new Vector3(0f, 0f, 1f);
						int s = a;
						while (s < b)
						{
							if (Opaque(s, ny))
							{
								s++;
								continue;
							}
							int e = s;
							while (e < b && !Opaque(e, ny))
							{
								e++;
							}
							float sa = X(s);
							float sb = X(e);
							float us0 = (s + 0.5f) / w;
							float us1 = (e - 0.5f) / w;
							Quad(new Vector3(sa, f.yMin, zEdge), new Vector3(sb, f.yMin, zEdge), new Vector3(sb, f.yMax, zEdge), new Vector3(sa, f.yMax, zEdge),
								n, new Vector2(us0, vMid), new Vector2(us1, vMid), new Vector2(us1, vMid), new Vector2(us0, vMid));
							s = e;
						}
					}
				}
			}

			var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
			mesh.SetVertices(verts);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateBounds();
			return mesh;
		}
	}
}
