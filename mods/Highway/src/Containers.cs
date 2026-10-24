using System.Collections.Generic;
using UnityEngine;

namespace HalfGripHighway
{
	/// <summary>Where a shipping container stands (base centre) and which way its long side runs.</summary>
	internal struct ContainerSpot
	{
		public Vector3 Pos;
		public float Yaw;     // degrees, the direction of the long side (0 = +Z, 90 = +X)
		public int Kind;      // 0..2: green, purple and blue
	}

	/// <summary>
	/// Shipping containers, made by the mod: a box with corrugated sides drawn into a small generated texture, in three colours. They are solid
	/// and never break. A container is 2 x 2 x 6 units in the mesh and drawn <see cref="Scale"/> times larger, which is 20 units long: a car in this
	/// game is 14 units long, and a car is 4.3 m, so one metre is 3.3 units.
	/// </summary>
	internal static class Containers
	{
		internal const float Scale = Plan.PerMetre;
		internal const float Height = 2f * Scale;
		internal const float Length = 6f * Scale;

		private static Mesh mesh;
		private static Material[] materials;

		private static readonly Color[] Paint =
		{
			new Color(0.26f, 0.42f, 0.30f),   // green
			new Color(0.42f, 0.25f, 0.42f),   // purple
			new Color(0.22f, 0.30f, 0.52f)    // blue
		};

		private static void Make(Material source)
		{
			if (mesh != null && materials != null && materials[0] != null)
			{
				return;
			}
			mesh = MakeMesh();
			Shader shader = source != null ? source.shader : Shader.Find("Universal Render Pipeline/Lit");
			materials = new Material[Paint.Length];
			for (int i = 0; i < Paint.Length; i++)
			{
				Material m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
				Texture2D texture = MakeTexture(Paint[i], i);
				if (m.HasProperty("_BaseColor"))
				{
					m.SetColor("_BaseColor", Color.white);
				}
				if (m.HasProperty("_Color"))
				{
					m.SetColor("_Color", Color.white);
				}
				if (m.HasProperty("_BaseMap"))
				{
					m.SetTexture("_BaseMap", texture);
				}
				if (m.HasProperty("_MainTex"))
				{
					m.SetTexture("_MainTex", texture);
				}
				if (m.HasProperty("_Smoothness"))
				{
					m.SetFloat("_Smoothness", 0.12f);
				}
				materials[i] = m;
			}
		}

		/// <summary>Ribs every 25 cm, darker edges and a little weathering.</summary>
		private static Texture2D MakeTexture(Color paint, int seed)
		{
			const int n = 64;
			Texture2D texture = new Texture2D(n, n, TextureFormat.RGBA32, true)
			{
				wrapMode = TextureWrapMode.Repeat,
				filterMode = FilterMode.Bilinear,
				hideFlags = HideFlags.HideAndDontSave
			};
			Color[] pixels = new Color[n * n];
			System.Random rng = new System.Random(1000 + seed);
			for (int y = 0; y < n; y++)
			{
				for (int x = 0; x < n; x++)
				{
					float rib = (x % 16) < 5 ? 0.70f : (x % 16) < 7 ? 0.88f : 1.0f;
					float edge = (y < 4 || y >= n - 4) ? 0.55f : 1.0f;
					float wear = 0.92f + 0.08f * (float)rng.NextDouble();
					float k = rib * edge * wear;
					pixels[y * n + x] = new Color(paint.r * k, paint.g * k, paint.b * k, 1f);
				}
			}
			texture.SetPixels(pixels);
			texture.Apply(true);
			return texture;
		}

		/// <summary>A box, 2 wide and 2 tall (x, y) and 6 long (z), standing on y = 0, with UVs of one texture tile to the metre.</summary>
		private static Mesh MakeMesh()
		{
			List<Vector3> v = new List<Vector3>();
			List<Vector2> uv = new List<Vector2>();
			List<int> t = new List<int>();

			void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float uSize, float vSize)
			{
				if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
				{
					Vector3 swap = b;
					b = d;
					d = swap;
				}
				int i = v.Count;
				v.Add(a);
				v.Add(b);
				v.Add(c);
				v.Add(d);
				uv.Add(new Vector2(0f, 0f));
				uv.Add(new Vector2(uSize, 0f));
				uv.Add(new Vector2(uSize, vSize));
				uv.Add(new Vector2(0f, vSize));
				t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
			}

			Vector3 p000 = new Vector3(-1f, 0f, -3f), p100 = new Vector3(1f, 0f, -3f), p110 = new Vector3(1f, 2f, -3f), p010 = new Vector3(-1f, 2f, -3f);
			Vector3 p001 = new Vector3(-1f, 0f, 3f), p101 = new Vector3(1f, 0f, 3f), p111 = new Vector3(1f, 2f, 3f), p011 = new Vector3(-1f, 2f, 3f);
			Face(p000, p001, p011, p010, Vector3.left, 6f, 1f);
			Face(p100, p101, p111, p110, Vector3.right, 6f, 1f);
			Face(p000, p100, p110, p010, Vector3.back, 2f, 1f);
			Face(p001, p101, p111, p011, Vector3.forward, 2f, 1f);
			Face(p010, p110, p111, p011, Vector3.up, 2f, 6f);
			Face(p000, p100, p101, p001, Vector3.down, 2f, 6f);

			Mesh m = new Mesh { name = "HighwayContainer", hideFlags = HideFlags.HideAndDontSave };
			m.SetVertices(v);
			m.SetUVs(0, uv);
			m.SetTriangles(t, 0);
			m.RecalculateNormals();
			m.RecalculateBounds();
			return m;
		}

		internal static int Build(List<ContainerSpot> spots, int layer, Material source, Transform parent)
		{
			if (spots.Count == 0)
			{
				return 0;
			}
			Make(source);
			GameObject holder = new GameObject("Containers");
			holder.transform.SetParent(parent, false);
			foreach (ContainerSpot spot in spots)
			{
				int k = Mathf.Clamp(spot.Kind, 0, materials.Length - 1);
				GameObject go = new GameObject("Container") { layer = layer };
				go.transform.SetParent(holder.transform, false);
				go.transform.SetPositionAndRotation(spot.Pos, Quaternion.Euler(0f, spot.Yaw, 0f));
				go.transform.localScale = Vector3.one * Scale;
				go.AddComponent<MeshFilter>().sharedMesh = mesh;
				go.AddComponent<MeshRenderer>().sharedMaterial = materials[k];
				BoxCollider box = go.AddComponent<BoxCollider>();
				box.center = mesh.bounds.center;
				box.size = mesh.bounds.size;
			}
			return spots.Count;
		}
	}
}
