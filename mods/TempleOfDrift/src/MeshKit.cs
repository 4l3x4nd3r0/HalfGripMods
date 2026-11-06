using System;
using System.Collections.Generic;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>
	/// All the triangles that share one texture and tint. Every face has its own vertices, so Unity's normals come out flat, and each face is
	/// wound to look toward a hint direction, so no call has to get the winding right by hand. Textures are projected from the world
	/// (<c>tile</c> is the size in units of one repeat), so a texel is the same size on every wall, road and roof.
	/// </summary>
	internal sealed class Batch
	{
		public readonly string Name;
		public readonly Texture2D Tex;
		public readonly Color Tint;
		public readonly bool Collide;
		public readonly bool Wall;
		public readonly bool TwoSided;
		/// <summary>"dirt", "gravel", "grass", "rough" or null: a HandlingModifier is added to the object made from this batch.</summary>
		public string Handling;
		/// <summary>Material smoothness: wet rock and puddles are high, dry things low.</summary>
		public float Smooth = 0.04f;
		/// <summary>Self-lit colour (glowing fungus, lanterns); black for none.</summary>
		public Color Emission = Color.black;
		/// <summary>Only the buildings cast shadows; everything else is cheaper without.</summary>
		public bool CastShadows;
		/// <summary>A collider and nothing to see: no renderer is made (the cave's guide walls).</summary>
		public bool Invisible;
		/// <summary>The collider gets a frictionless material, so a car scrapes along it instead of snagging.</summary>
		public bool Slippery;

		private readonly List<Vector3> verts = new List<Vector3>();
		private readonly List<Vector2> uvs = new List<Vector2>();
		private readonly List<int> tris = new List<int>();

		public Batch(string name, Texture2D tex, Color tint, bool collide, bool wall = false, bool twoSided = false)
		{
			Name = name;
			Tex = tex;
			Tint = tint;
			Collide = collide;
			Wall = wall;
			TwoSided = twoSided;
		}

		public int Count
		{
			get { return verts.Count; }
		}

		private static Vector2 Proj(Vector3 p, Vector3 n, float tile)
		{
			float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
			if (ay >= ax && ay >= az)
			{
				return new Vector2(p.x / tile, p.z / tile);
			}
			if (ax >= az)
			{
				return new Vector2(p.z / tile, p.y / tile);
			}
			return new Vector2(p.x / tile, p.y / tile);
		}

		private void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
		{
			int i = verts.Count;
			verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
			uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
			tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
			tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
		}

		public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 hint, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
		{
			if (Vector3.Dot(Vector3.Cross(b - a, c - a), hint) < 0f)
			{
				Vector3 sv = b; b = d; d = sv;
				Vector2 su = ub; ub = ud; ud = su;
			}
			Add(a, b, c, d, ua, ub, uc, ud);
			if (TwoSided)
			{
				Add(a, d, c, b, ua, ud, uc, ub);
			}
		}

		public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 hint, float tile)
		{
			Vector3 n = Vector3.Cross(b - a, c - a);
			Quad(a, b, c, d, hint, Proj(a, n, tile), Proj(b, n, tile), Proj(c, n, tile), Proj(d, n, tile));
		}

		public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 hint, float tile)
		{
			Vector3 n = Vector3.Cross(b - a, c - a);
			if (Vector3.Dot(n, hint) < 0f)
			{
				Vector3 sv = b; b = c; c = sv;
				n = -n;
			}
			Vector2 ua = Proj(a, n, tile), ub = Proj(b, n, tile), uc = Proj(c, n, tile);
			int i = verts.Count;
			verts.Add(a); verts.Add(b); verts.Add(c);
			uvs.Add(ua); uvs.Add(ub); uvs.Add(uc);
			tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
			if (TwoSided)
			{
				i = verts.Count;
				verts.Add(a); verts.Add(c); verts.Add(b);
				uvs.Add(ua); uvs.Add(uc); uvs.Add(ub);
				tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
			}
		}

		public void Box(Vector3 center, Vector3 size, Quaternion rotation, float tile)
		{
			Vector3 h = size * 0.5f;
			Vector3 x = rotation * new Vector3(h.x, 0f, 0f);
			Vector3 y = rotation * new Vector3(0f, h.y, 0f);
			Vector3 z = rotation * new Vector3(0f, 0f, h.z);
			Face(center, x, y, z, tile);
			Face(center, -x, y, z, tile);
			Face(center, y, x, z, tile);
			Face(center, -y, x, z, tile);
			Face(center, z, x, y, tile);
			Face(center, -z, x, y, tile);
		}

		private void Face(Vector3 center, Vector3 normal, Vector3 u, Vector3 v, float tile)
		{
			Vector3 c = center + normal;
			Quad(c - u - v, c + u - v, c + u + v, c - u + v, normal, tile);
		}

		/// <summary>A box from a point on the ground: width along the right axis, depth along forward, height up.</summary>
		public void BoxAt(Vector3 basePos, Vector3 forward, float width, float depth, float height, float tile)
		{
			Quaternion rot = Quaternion.LookRotation(forward, Vector3.up);
			Box(basePos + Vector3.up * (height * 0.5f), new Vector3(width, height, depth), rot, tile);
		}

		/// <summary>A tapered post or cone from a base circle to a top circle (radius 0 makes a point).</summary>
		public void Cone(Vector3 baseCenter, float baseRadius, Vector3 top, float topRadius, int sides, float tile, bool caps = false)
		{
			Vector3 axis = (top - baseCenter).normalized;
			Vector3 side = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
			Vector3 u = Vector3.Cross(axis, side).normalized;
			Vector3 v = Vector3.Cross(axis, u).normalized;
			for (int i = 0; i < sides; i++)
			{
				float a0 = i * Mathf.PI * 2f / sides;
				float a1 = (i + 1) * Mathf.PI * 2f / sides;
				Vector3 r0 = u * Mathf.Cos(a0) + v * Mathf.Sin(a0);
				Vector3 r1 = u * Mathf.Cos(a1) + v * Mathf.Sin(a1);
				Vector3 b0 = baseCenter + r0 * baseRadius, b1 = baseCenter + r1 * baseRadius;
				Vector3 t0 = top + r0 * topRadius, t1 = top + r1 * topRadius;
				if (topRadius < 0.001f)
				{
					Tri(b0, b1, top, r0 + r1, tile);
				}
				else
				{
					Quad(b0, b1, t1, t0, r0 + r1, tile);
				}
				if (caps)
				{
					Tri(baseCenter, b0, b1, -axis, tile);
					if (topRadius >= 0.001f)
					{
						Tri(top, t0, t1, axis, tile);
					}
				}
			}
		}

		private static float H2(int a, int b, int seed)
		{
			unchecked
			{
				int h = a * 374761393 + b * 668265263 + seed * 1442695041;
				h = (h ^ (h >> 13)) * 1274126177;
				h ^= h >> 16;
				return (h & 0xFFFF) / 65535f;
			}
		}

		/// <summary>A flat-shaded ball (an ellipsoid when the radii differ) whose vertices are pushed in and out a little, for a canopy or a boulder.</summary>
		public void Sphere(Vector3 center, Vector3 radii, int rings, int sides, float tile, float lump = 0f, int seed = 0)
		{
			System.Func<int, int, Vector3> P = (r, s) =>
			{
				float lat = Mathf.PI * (-0.5f + r / (float)rings);
				float lon = Mathf.PI * 2f * (s % sides) / sides;
				float k = 1f + lump * (H2(r, s % sides, seed) - 0.5f) * (r == 0 || r == rings ? 0f : 1f);
				return center + new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon) * radii.x, Mathf.Sin(lat) * radii.y, Mathf.Cos(lat) * Mathf.Sin(lon) * radii.z) * k;
			};
			for (int r = 0; r < rings; r++)
			{
				for (int s = 0; s < sides; s++)
				{
					Vector3 a = P(r, s), b = P(r, s + 1), c = P(r + 1, s + 1), d = P(r + 1, s);
					Vector3 mid = (a + b + c + d) * 0.25f;
					if (r == 0)
					{
						Tri(a, c, d, mid - center, tile);
					}
					else if (r == rings - 1)
					{
						Tri(a, b, d, mid - center, tile);
					}
					else
					{
						Quad(a, b, c, d, mid - center, tile);
					}
				}
			}
		}

		public void Pillar(Vector3 baseCenter, float radius, float height, int sides, float tile)
		{
			Cone(baseCenter, radius, baseCenter + Vector3.up * height, radius, sides, tile, false);
		}

		public Mesh ToMesh()
		{
			Mesh mesh = new Mesh { name = "TempleOfDrift_" + Name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
			mesh.SetVertices(verts);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}
	}

	/// <summary>Materials made from the Stadium ground's shader (so the scene's lighting and shadows work), one per texture and tint.</summary>
	internal static class Mats
	{
		internal static Material Source;
		private static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

		internal static void Reset(Material source)
		{
			Source = source;
			cache.Clear();
		}

		internal static Material Get(Texture2D tex, Color tint, bool twoSided = false, float smooth = 0.04f, Color emission = default(Color))
		{
			string key = (tex != null ? tex.GetInstanceID().ToString() : "-") + "|" + tint.r.ToString("0.000") + tint.g.ToString("0.000") + tint.b.ToString("0.000") + "|" + (twoSided ? "2" : "1") + "|" + smooth.ToString("0.00") + "|" + emission.r.ToString("0.0") + emission.g.ToString("0.0") + emission.b.ToString("0.0");
			Material m;
			if (cache.TryGetValue(key, out m) && m != null)
			{
				return m;
			}
			Shader shader = Source != null ? Source.shader : null;
			m = shader != null ? new Material(shader) : null;
			if (m == null || (!m.HasProperty("_BaseColor") && !m.HasProperty("_Color")) || (!m.HasProperty("_BaseMap") && !m.HasProperty("_MainTex")))
			{
				shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
				m = new Material(shader);
			}
			if (tex != null)
			{
				if (m.HasProperty("_BaseMap"))
				{
					m.SetTexture("_BaseMap", tex);
				}
				if (m.HasProperty("_MainTex"))
				{
					m.SetTexture("_MainTex", tex);
				}
			}
			if (m.HasProperty("_BaseColor"))
			{
				m.SetColor("_BaseColor", tint);
			}
			if (m.HasProperty("_Color"))
			{
				m.SetColor("_Color", tint);
			}
			if (m.HasProperty("_Smoothness"))
			{
				m.SetFloat("_Smoothness", smooth);
			}
			if (m.HasProperty("_Metallic"))
			{
				m.SetFloat("_Metallic", 0f);
			}
			if (emission.maxColorComponent > 0.01f && m.HasProperty("_EmissionColor"))
			{
				m.EnableKeyword("_EMISSION");
				m.SetColor("_EmissionColor", emission);
				m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
			}
			if (twoSided && m.HasProperty("_Cull"))
			{
				m.SetFloat("_Cull", 0f);
			}
			m.hideFlags = HideFlags.HideAndDontSave;
			cache[key] = m;
			return m;
		}
	}
}
