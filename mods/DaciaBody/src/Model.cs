using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace HalfGripDaciaBody
{
	/// <summary>
	/// The Logan's 3D shape. The game's second car model is a root with a few mesh children (body, hood, glass, lights) and its interior;
	/// when the Dacia's skin is applied the five meshes are swapped for the ones from dacia_model.txt, and put back for any other skin.
	/// Everything else on the model (interior, damage, windshield hiding, health bar) is the game's own and keeps working.
	/// The paint is the player's: the mask texture only says which of the shader's three zones each texel is in.
	/// </summary>
	internal static class Model
	{
		internal const string SkinName = "DaciaSkin";

		// The model index the skin asks for; the stock bodies we copy use the second model, which is the one these meshes replace.
		internal const int ModelIndex = 1;

		// The shape file's "body" meshes are in the model root's local space. The game's body meshes sit under a child turned -90 degrees
		// about x and scaled by 100, so a point (x, y, z) goes into the mesh as (x, -z, y) / 100. "root" meshes (the lights) are used as they are.
		private const float MeshScale = 0.01f;

		private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
		private static bool loaded;
		private static Texture2D baseTexture, colourMask;

		// The paint material is double sided in the stock game. The Dacia's body is not: its panels are single sheets, and seen from inside
		// they would fight with the stock interior, so they are drawn from outside only while the Dacia is fitted.
		// The lamps are the game's emissive material on a much bigger lens than the stock one, so at full strength they bloom white over the
		// bumper and fenders. While the Dacia is fitted their emission is scaled by the LightGlow setting.
		private static readonly Dictionary<(Material, string), Color> originalEmission = new Dictionary<(Material, string), Color>();

		private static readonly Dictionary<Material, float> originalCull = new Dictionary<Material, float>();

		// The game's headlight is a real spot light (143 degree cone, intensity 500, no shadows) at the stock nose. By default it stays where the game
		// has it; the HeadlightSetback setting moves it back while the Dacia is fitted, in case it lights the bumper too hard.
		private static readonly Dictionary<Transform, Vector3> originalLightPositions = new Dictionary<Transform, Vector3>();

		// The game hides its windscreen from the cockpit camera through CameraSwitch.windshields. The Dacia's hood is added to that list, so the
		// driver sees the road over the dash like in the stock car, and it is taken out again for any other body.
		private static readonly HashSet<MeshRenderer> hoodsWeAdded = new HashSet<MeshRenderer>();

		private static readonly Dictionary<MeshFilter, Mesh> originalMeshes = new Dictionary<MeshFilter, Mesh>();
		private static readonly Dictionary<Transform, (Vector3 position, Quaternion rotation, Vector3 scale)> originalPoses = new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();

		// Which stock child takes which mesh. The light units are given an identity pose so their meshes can be authored in the model's own space.
		private static readonly (string child, string mesh, bool resetPose)[] Swaps =
		{
			("car2.001", "shell", false),
			("car2.003", "hood", false),
			("Cube.001", "glass", false),
			("HeadlightsCar2", "headlights", true),
			("TailLightsCar2", "taillights", true)
		};

		internal static bool IsDacia(CarSkin skin)
		{
			return skin != null && skin.name != null && skin.name.StartsWith(SkinName, StringComparison.Ordinal);
		}

		internal static bool Ready => meshes.ContainsKey("shell") && baseTexture != null && colourMask != null;

		/// <summary>Loads the shape and the two paint textures once. Returns false, with the reason logged, when something is missing.</summary>
		internal static bool Load()
		{
			if (loaded)
			{
				return Ready;
			}
			loaded = true;
			string dir = Mod.Ctx.Folder;
			baseTexture = LoadPng(Path.Combine(dir, "dacia_base.png"), "DaciaBase");
			colourMask = LoadPng(Path.Combine(dir, "dacia_mask.png"), "DaciaMask");
			string path = Path.Combine(dir, "dacia_model.txt");
			if (!File.Exists(path))
			{
				Mod.Log.LogWarning("Missing " + path + ", the Dacia keeps the stock car's shape");
				return false;
			}
			try
			{
				ReadMeshes(path);
			}
			catch (Exception e)
			{
				meshes.Clear();
				Mod.Log.LogError("Could not read the Dacia model: " + e);
			}
			if (!Ready)
			{
				Mod.Log.LogWarning("The Dacia model is incomplete (shape or textures missing), so it keeps the stock car's shape");
			}
			return Ready;
		}

		private static Texture2D LoadPng(string path, string name)
		{
			if (!File.Exists(path))
			{
				Mod.Log.LogWarning("Missing " + path);
				return null;
			}
			Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.HideAndDontSave,
				name = name
			};
			if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
			{
				Mod.Log.LogWarning("Could not read " + path);
				return null;
			}
			return texture;
		}

		/// <summary>The textures the skin carries: the detail layer and the colour mask.</summary>
		internal static bool Textures(out Texture2D baseTex, out Texture2D mask)
		{
			bool ok = Load();
			baseTex = baseTexture;
			mask = colourMask;
			return ok;
		}

		private static void ReadMeshes(string path)
		{
			string name = null;
			bool root = false;
			List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
			List<Vector2> uv = new List<Vector2>();
			List<int> t = new List<int>();
			Action flush = () =>
			{
				if (name == null || v.Count == 0)
				{
					return;
				}
				Mesh mesh = new Mesh { name = "Dacia_" + name, hideFlags = HideFlags.HideAndDontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
				mesh.SetVertices(v);
				mesh.SetNormals(n);
				mesh.SetUVs(0, uv);
				mesh.SetTriangles(t, 0);
				mesh.RecalculateBounds();
				meshes[name] = mesh;
				Mod.Log.LogInfo($"Loaded Dacia mesh '{name}': {v.Count} vertices, {t.Count / 3} triangles");
			};
			foreach (string line in File.ReadLines(path))
			{
				string[] p = line.Split(' ');
				if (p[0] == "mesh")
				{
					flush();
					name = p[1];
					root = p.Length > 2 && p[2] == "root";
					v = new List<Vector3>(); n = new List<Vector3>(); uv = new List<Vector2>(); t = new List<int>();
				}
				else if (p[0] == "v")
				{
					float[] f = new float[8];
					for (int i = 0; i < 8; i++)
					{
						f[i] = float.Parse(p[i + 1], CultureInfo.InvariantCulture);
					}
					v.Add(root ? new Vector3(f[0], f[1], f[2]) : ToMesh(f[0], f[1], f[2]) * MeshScale);
					n.Add(root ? new Vector3(f[3], f[4], f[5]) : ToMesh(f[3], f[4], f[5]));
					uv.Add(new Vector2(f[6], f[7]));
				}
				else if (p[0] == "t")
				{
					t.Add(int.Parse(p[1])); t.Add(int.Parse(p[2])); t.Add(int.Parse(p[3]));
				}
			}
			flush();
		}

		private static Vector3 ToMesh(float x, float y, float z)
		{
			return new Vector3(x, -z, y);
		}

		/// <summary>Fits the Dacia's shape to the controller's second model, or puts the stock one back when the skin is not the Dacia's.</summary>
		internal static void Refit(CarSkinController controller)
		{
			if (controller == null || controller.carModels == null || controller.carModels.Length <= ModelIndex || controller.carModels[ModelIndex] == null)
			{
				return;
			}
			bool dacia = IsDacia(controller.carSkin) && Load();
			Transform root = controller.carModels[ModelIndex].transform;
			MoveHeadlight(controller, dacia && meshes.ContainsKey("shell"));
			foreach ((string child, string meshName, bool resetPose) in Swaps)
			{
				Transform tr = Find(root, child);
				MeshFilter filter = tr != null ? tr.GetComponent<MeshFilter>() : null;
				if (filter == null)
				{
					continue;
				}
				if (!originalMeshes.ContainsKey(filter))
				{
					originalMeshes[filter] = filter.sharedMesh;
					originalPoses[tr] = (tr.localPosition, tr.localRotation, tr.localScale);
				}
				bool use = dacia && meshes.TryGetValue(meshName, out _);
				filter.sharedMesh = use ? meshes[meshName] : originalMeshes[filter];
				if (meshName == "shell" || meshName == "hood")
				{
					SetCull(tr.GetComponent<Renderer>(), use);
				}
				if (meshName == "hood")
				{
					HideHoodInCockpit(tr.GetComponent<MeshRenderer>(), use);
				}
				if (meshName == "headlights" || meshName == "taillights")
				{
					ScaleEmission(tr.GetComponent<Renderer>(), use ? Body.LightGlow : 1f);
				}
				if (resetPose)
				{
					(Vector3 position, Quaternion rotation, Vector3 scale) = originalPoses[tr];
					tr.localPosition = use ? Vector3.zero : position;
					tr.localRotation = use ? Quaternion.identity : rotation;
					tr.localScale = use ? Vector3.one : scale;
				}
			}
		}

		private static void HideHoodInCockpit(MeshRenderer hood, bool hide)
		{
			if (hood == null)
			{
				return;
			}
			CameraSwitch cameraSwitch = UnityEngine.Object.FindFirstObjectByType<CameraSwitch>();
			if (cameraSwitch == null || cameraSwitch.transform.root != hood.transform.root)
			{
				return;
			}
			List<MeshRenderer> list = new List<MeshRenderer>(cameraSwitch.windshields ?? new MeshRenderer[0]);
			if (hide && !list.Contains(hood))
			{
				bool visible = list.Count == 0 || list[0] == null || list[0].enabled;
				list.Add(hood);
				hood.enabled = visible;
				hoodsWeAdded.Add(hood);
				cameraSwitch.windshields = list.ToArray();
				Mod.Log.LogInfo("Hood added to the cockpit-hidden list (" + list.Count + " renderers)");
			}
			else if (!hide && list.Contains(hood) && hoodsWeAdded.Contains(hood))
			{
				list.Remove(hood);
				hood.enabled = true;
				hoodsWeAdded.Remove(hood);
				cameraSwitch.windshields = list.ToArray();
			}
		}

		private static void ScaleEmission(Renderer renderer, float scale)
		{
			if (renderer == null)
			{
				return;
			}
			foreach (Material material in renderer.materials)
			{
				foreach (string property in new[] { "_EmissionColor", "_EmissiveColor" })
				{
					if (material == null || !material.HasProperty(property))
					{
						continue;
					}
					if (!originalEmission.ContainsKey((material, property)))
					{
						originalEmission[(material, property)] = material.GetColor(property);
						Mod.Log.LogInfo($"Lamp material '{material.name}' ({material.shader.name}) {property}={originalEmission[(material, property)]}");
					}
					Color c = originalEmission[(material, property)];
					material.SetColor(property, new Color(c.r * scale, c.g * scale, c.b * scale, c.a));
				}
			}
		}

		private static void SetCull(Renderer renderer, bool back)
		{
			if (renderer == null)
			{
				return;
			}
			foreach (Material material in renderer.materials)
			{
				if (material == null || !material.HasProperty("_Cull"))
				{
					continue;
				}
				if (!originalCull.ContainsKey(material))
				{
					originalCull[material] = material.GetFloat("_Cull");
					Mod.Log.LogInfo($"Paint material '{material.name}' has _Cull={originalCull[material]}");
				}
				material.SetFloat("_Cull", back ? 2f : originalCull[material]);
			}
		}

		private static void MoveHeadlight(CarSkinController controller, bool dacia)
		{
			Transform headlights = controller.transform.Find("Headlights");
			if (headlights == null)
			{
				return;
			}
			foreach (Light light in headlights.GetComponentsInChildren<Light>(true))
			{
				Transform t = light.transform;
				if (!originalLightPositions.ContainsKey(t))
				{
					originalLightPositions[t] = t.localPosition;
				}
				t.localPosition = dacia ? originalLightPositions[t] + new Vector3(0f, 0f, -Body.HeadlightSetback) : originalLightPositions[t];
			}
		}

		private static Transform Find(Transform root, string name)
		{
			foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
			{
				if (t.name == name)
				{
					return t;
				}
			}
			return null;
		}
	}
}
