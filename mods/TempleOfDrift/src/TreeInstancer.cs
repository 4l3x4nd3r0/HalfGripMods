using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace TempleOfDrift
{
	/// <summary>
	/// The game's own cherry tree (two cut-out card meshes of about 100 faces, extracted into this mod: they only exist in a scene that is not
	/// loaded in a race) and the Forest's own tree models, drawn with GPU instancing: each (mesh, material) is one draw call for every tree
	/// in view, however many that is. Trees are grouped in 250-unit cells; only cells in front of the camera and within the draw distance are
	/// sent. No shadows, no colliders (as the game's own trees).
	/// </summary>
	internal sealed class TreeInstancer : MonoBehaviour
	{
		private const float Cell = 250f;
		internal static float DrawDistance = 760f;   // a field only so the render rig can raise it for aerial pictures; the game never changes it

		internal sealed class Set
		{
			public Mesh Mesh;
			public Material Material;
			public Dictionary<long, List<Matrix4x4>> Cells = new Dictionary<long, List<Matrix4x4>>();
			public Dictionary<long, Matrix4x4[]> Arrays = new Dictionary<long, Matrix4x4[]>();
		}

		private static readonly List<Set> sets = new List<Set>();
		private static readonly Dictionary<string, Set> byKey = new Dictionary<string, Set>();
		private static Mesh[] sakuraMeshes;
		private static Material sakuraMaterial;
		private static readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();

		private int layer;
		private Camera drawing;
		private int frame = -1;
		private readonly Plane[] planes = new Plane[6];
		private Matrix4x4[] scratch = new Matrix4x4[1023];
		internal static int DrawnLast;

		private static long Key(int cx, int cz)
		{
			return ((long)cx << 32) ^ (uint)cz;
		}

		internal static void Reset()
		{
			sets.Clear();
			byKey.Clear();
		}

		// ---- the game's cherry tree -------------------------------------------------------------------------------------

		private static Mesh LoadSakura(string file)
		{
			string path = Path.Combine(Path.Combine(Mod.Ctx.Folder, "data"), file);
			if (!File.Exists(path))
			{
				return null;
			}
			string[] lines = File.ReadAllLines(path);
			int n = int.Parse(lines[0].Substring(2), CultureInfo.InvariantCulture);
			Vector3[] v = new Vector3[n];
			Vector2[] uv = new Vector2[n];
			float minY = 1e9f, maxY = -1e9f;
			for (int i = 0; i < n; i++)
			{
				string[] c = lines[1 + i].Split(' ');
				float x = float.Parse(c[0], CultureInfo.InvariantCulture), y = float.Parse(c[1], CultureInfo.InvariantCulture), z = float.Parse(c[2], CultureInfo.InvariantCulture);
				// the tree grows along +Z in the mesh; stand it up (the game rotates it -90 degrees about X)
				v[i] = new Vector3(x, z, -y);
				uv[i] = new Vector2(float.Parse(c[3], CultureInfo.InvariantCulture), float.Parse(c[4], CultureInfo.InvariantCulture));
				minY = Mathf.Min(minY, v[i].y);
				maxY = Mathf.Max(maxY, v[i].y);
			}
			float scale = 1f / Mathf.Max(1e-5f, maxY - minY);   // one unit tall; placement scales it
			for (int i = 0; i < n; i++)
			{
				v[i] = new Vector3(v[i].x * scale, (v[i].y - minY) * scale, v[i].z * scale);
			}
			int tcount = int.Parse(lines[1 + n].Substring(2), CultureInfo.InvariantCulture);
			int[] tris = new int[tcount * 3];
			for (int i = 0; i < tcount; i++)
			{
				string[] c = lines[2 + n + i].Split(' ');
				tris[i * 3] = int.Parse(c[0], CultureInfo.InvariantCulture);
				tris[i * 3 + 1] = int.Parse(c[2], CultureInfo.InvariantCulture);   // the stand-up rotation mirrors the winding
				tris[i * 3 + 2] = int.Parse(c[1], CultureInfo.InvariantCulture);
			}
			Mesh mesh = new Mesh { name = "ToD_" + file };
			mesh.vertices = v;
			mesh.uv = uv;
			mesh.triangles = tris;
			Vector3[] normals = new Vector3[n];
			for (int i = 0; i < n; i++)
			{
				normals[i] = Vector3.up;   // cards: lit from above like a crown, not by their flat faces
			}
			mesh.normals = normals;
			mesh.RecalculateBounds();
			mesh.hideFlags = HideFlags.HideAndDontSave;
			return mesh;
		}

		private static Material AlphaMaterial(Texture tex, Color tint)
		{
			Shader shader = Shader.Find("Universal Render Pipeline/Lit");
			Material m = shader != null ? new Material(shader) : new Material(Mats.Get(null, Color.white).shader);
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
			if (m.HasProperty("_Cutoff"))
			{
				m.SetFloat("_Cutoff", 0.5f);
			}
			if (m.HasProperty("_AlphaClip"))
			{
				m.SetFloat("_AlphaClip", 1f);
			}
			m.EnableKeyword("_ALPHATEST_ON");
			if (m.HasProperty("_Cull"))
			{
				m.SetFloat("_Cull", 0f);
			}
			if (m.HasProperty("_Smoothness"))
			{
				m.SetFloat("_Smoothness", 0f);
			}
			if (m.HasProperty("_Metallic"))
			{
				m.SetFloat("_Metallic", 0f);
			}
			m.renderQueue = 2450;
			m.enableInstancing = true;
			m.hideFlags = HideFlags.HideAndDontSave;
			return m;
		}

		/// <summary>The game's tree material turned into one that URP can draw instanced (same texture, cut-out, two-sided).</summary>
		private static Material Convert(Material game)
		{
			Material m;
			if (converted.TryGetValue(game, out m) && m != null)
			{
				return m;
			}
			Texture tex = game.HasProperty("_BaseMap") ? game.GetTexture("_BaseMap") : null;
			if (tex == null && game.HasProperty("_MainTex"))
			{
				tex = game.GetTexture("_MainTex");
			}
			Color tint = game.HasProperty("_BaseColor") ? game.GetColor("_BaseColor") : Color.white;
			m = AlphaMaterial(tex, tint);
			converted[game] = m;
			return m;
		}

		private static Set SetFor(string key, Mesh mesh, Material material)
		{
			Set s;
			if (!byKey.TryGetValue(key, out s))
			{
				s = new Set { Mesh = mesh, Material = material };
				byKey[key] = s;
				sets.Add(s);
			}
			return s;
		}

		internal static bool SakuraReady
		{
			get { return sakuraMeshes != null && sakuraMeshes.Length > 0 && sakuraMeshes[0] != null; }
		}

		internal static void PrepareSakura()
		{
			if (SakuraReady)
			{
				return;
			}
			try
			{
				Mesh a = LoadSakura("sakura_1.txt"), b = LoadSakura("sakura_2.txt");
				Texture2D atlas = Mod.Ctx.Sprites.LoadTexture("sakura_atlas.png");
				if (a != null && b != null && atlas != null)
				{
					sakuraMeshes = new[] { a, b };
					sakuraMaterial = AlphaMaterial(atlas, Color.white);
					Mod.Log.LogInfo("The game's cherry trees loaded: " + a.triangles.Length / 3 + " and " + b.triangles.Length / 3 + " triangles");
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not load the cherry trees: " + e.Message);
			}
		}

		// ---- adding trees ---------------------------------------------------------------------------------------------------

		/// <summary>Where one tree's parts are kept, so a felled tree can be taken out and a copy made that topples.</summary>
		internal sealed class Handle
		{
			internal struct Part
			{
				public Set S;
				public long Key;
				public int Index;
				public Matrix4x4 M;
			}

			internal readonly List<Part> Parts = new List<Part>();
			internal Vector3 Root;
		}

		internal static bool Created;

		/// <summary>Takes the tree out of the drawing and returns a hinge at its foot carrying a copy of it, for the fall.</summary>
		internal static Transform Topple(Handle h, int gameLayer)
		{
			GameObject hinge = new GameObject("fallen tree") { layer = gameLayer };
			hinge.transform.position = h.Root;
			foreach (Handle.Part p in h.Parts)
			{
				Matrix4x4[] arr;
				if (p.S.Arrays.TryGetValue(p.Key, out arr) && p.Index < arr.Length)
				{
					arr[p.Index] = Matrix4x4.TRS(h.Root, Quaternion.identity, Vector3.zero);
				}
				GameObject g = new GameObject("part") { layer = gameLayer };
				g.transform.SetParent(hinge.transform, false);
				g.transform.SetPositionAndRotation(p.M.GetColumn(3), p.M.rotation);
				g.transform.localScale = p.M.lossyScale;
				g.AddComponent<MeshFilter>().sharedMesh = p.S.Mesh;
				MeshRenderer r = g.AddComponent<MeshRenderer>();
				r.sharedMaterial = p.S.Material;
				r.shadowCastingMode = ShadowCastingMode.Off;
				r.receiveShadows = false;
			}
			return hinge.transform;
		}

		private static void Add(Set s, Matrix4x4 m, Handle h = null)
		{
			int cx = Mathf.FloorToInt(m.GetColumn(3).x / Cell), cz = Mathf.FloorToInt(m.GetColumn(3).z / Cell);
			List<Matrix4x4> list;
			if (!s.Cells.TryGetValue(Key(cx, cz), out list))
			{
				list = new List<Matrix4x4>();
				s.Cells[Key(cx, cz)] = list;
			}
			if (h != null)
			{
				h.Parts.Add(new Handle.Part { S = s, Key = Key(cx, cz), Index = list.Count, M = m });
			}
			list.Add(m);
		}

		internal static bool AddSakura(Vector3 pos, float yaw, float height, int pick, Handle h = null)
		{
			if (!SakuraReady)
			{
				return false;
			}
			int i = pick % sakuraMeshes.Length;
			Set s = SetFor("sakura" + i, sakuraMeshes[i], sakuraMaterial);
			float w = height * 1.25f;
			if (h != null)
			{
				h.Root = pos;
			}
			Add(s, Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), new Vector3(w, height, w)), h);
			return true;
		}

		internal static bool AddForest(Vector3 pos, float yaw, float height, int pick, Handle h = null)
		{
			List<TreeSource.Model> models = TreeSource.Models;
			if (models.Count == 0)
			{
				return false;
			}
			TreeSource.Model model = models[pick % models.Count];
			if (h != null)
			{
				h.Root = pos;
			}
			float k = height / model.Height;
			Matrix4x4 root = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), Vector3.one * k);
			for (int p = 0; p < model.Parts.Count; p++)
			{
				TreeSource.Part part = model.Parts[p];
				Material mat = part.Materials.Length > 0 ? part.Materials[0] : null;
				if (mat == null)
				{
					continue;
				}
				Set s = SetFor("forest" + (pick % models.Count) + "_" + p, part.Mesh, Convert(mat));
				Add(s, root * Matrix4x4.TRS(part.Pos, part.Rot, part.Scale), h);
			}
			return true;
		}

		internal static void Create(Transform parent, int gameLayer)
		{
			foreach (Set s in sets)
			{
				foreach (KeyValuePair<long, List<Matrix4x4>> kv in s.Cells)
				{
					s.Arrays[kv.Key] = kv.Value.ToArray();
				}
			}
			GameObject go = new GameObject("TreeInstancer") { layer = gameLayer };
			go.transform.SetParent(parent, false);
			TreeInstancer ti = go.AddComponent<TreeInstancer>();
			ti.layer = gameLayer;
			int total = 0;
			foreach (Set s in sets)
			{
				foreach (Matrix4x4[] a in s.Arrays.Values)
				{
					total += a.Length;
				}
			}
			Mod.Log.LogInfo("Instanced trees: " + total + " instances in " + sets.Count + " (mesh, material) sets");
		}

		// ---- drawing ------------------------------------------------------------------------------------------------------------

		internal static bool Hidden;   // the cave culler turns the trees off while the player is deep inside

		internal static TreeInstancer Instance;

		private void Awake()
		{
			Instance = this;
		}

		private void OnEnable()
		{
			RenderPipelineManager.beginCameraRendering += OnBeginCamera;
		}

		private void OnDisable()
		{
			RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
		}

		/// <summary>
		/// Every game camera that is about to render gets its own submission of the trees (first person, third person, the spectator drone,
		/// the debug cameras). Submitting only for Camera.main drew nothing in the third-person view, where that camera is not the one in use.
		/// </summary>
		private void OnBeginCamera(ScriptableRenderContext context, Camera view)
		{
			if (Hidden || view == null || view.cameraType != CameraType.Game)
			{
				return;
			}
			Draw(view);
		}
		/// <summary>Queues the trees for a camera (the game's, every frame; the debug camera's, just before it is rendered by hand).</summary>
		internal void Draw(Camera view)
		{
			if (Time.frameCount != frame)
			{
				frame = Time.frameCount;
				DrawnLast = 0;
			}
			drawing = view;
			Vector3 eye = view.transform.position;
			bool aerial = DrawDistance > 1e6f;
			GeometryUtility.CalculateFrustumPlanes(view, planes);
			foreach (Set s in sets)
			{
				int count = 0;
				Bounds all = new Bounds(eye, Vector3.zero);
				bool any = false;
				foreach (KeyValuePair<long, Matrix4x4[]> kv in s.Arrays)
				{
					int cx = (int)(kv.Key >> 32), cz = (int)(kv.Key & 0xFFFFFFFF);
					Vector3 centre = new Vector3((cx + 0.5f) * Cell, aerial ? 0f : eye.y, (cz + 0.5f) * Cell);   // the render rig (aerial) tests the whole height range
					if ((new Vector2(centre.x - eye.x, centre.z - eye.z)).magnitude > DrawDistance + Cell * 0.75f)
					{
						continue;
					}
					Bounds b = new Bounds(centre, new Vector3(Cell, aerial ? 3000f : 400f, Cell));
					if (!GeometryUtility.TestPlanesAABB(planes, b))
					{
						continue;
					}
					Matrix4x4[] arr = kv.Value;
					for (int i = 0; i < arr.Length; i++)
					{
						Vector3 p = arr[i].GetColumn(3);
						float dx = p.x - eye.x, dz = p.z - eye.z;
						if (dx * dx + dz * dz > DrawDistance * DrawDistance)
						{
							continue;
						}
						scratch[count++] = arr[i];
						if (count == scratch.Length)
						{
							Flush(s, count, b);
							count = 0;
						}
					}
					if (!any)
					{
						all = b;
						any = true;
					}
					else
					{
						all.Encapsulate(b);
					}
				}
				if (count > 0)
				{
					Flush(s, count, all);
				}
			}
		}

		private void Flush(Set s, int count, Bounds bounds)
		{
			RenderParams rp = new RenderParams(s.Material)
			{
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = layer,
				camera = drawing,
				worldBounds = DrawDistance > 1e6f ? new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 1e5f)) : new Bounds(bounds.center, bounds.size + new Vector3(2f * Cell, 400f, 2f * Cell))
			};
			Graphics.RenderMeshInstanced(rp, s.Mesh, 0, scratch, count);
			DrawnLast += count;
		}
	}
}
