using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HalfGripHighway
{
	/// <summary>
	/// The Forest map's own pine trees. They are terrain tree prototypes (<c>ForrestTree1..4</c>), loaded only while a scene that uses them
	/// is, so they are picked up when the main menu (whose terrain carries them) loads and kept alive in a hidden holder object.
	/// </summary>
	internal static class TreeSource
	{
		internal sealed class Part
		{
			public Mesh Mesh;
			public Material[] Materials;
			public Vector3 Pos;
			public Quaternion Rot;
			public Vector3 Scale;
		}

		internal sealed class Model
		{
			public string Name;
			public float Height;
			public readonly List<Part> Parts = new List<Part>();
		}

		internal static readonly List<Model> Models = new List<Model>();

		private static GameObject keeper;

		internal static void Capture()
		{
			if (Models.Count > 0)
			{
				return;
			}
			try
			{
				List<GameObject> prefabs = new List<GameObject>();
				foreach (Terrain terrain in Resources.FindObjectsOfTypeAll<Terrain>())
				{
					TerrainData data = terrain.terrainData;
					if (data == null)
					{
						continue;
					}
					foreach (TreePrototype proto in data.treePrototypes)
					{
						if (proto.prefab != null && proto.prefab.name.StartsWith("ForrestTree") && !prefabs.Contains(proto.prefab))
						{
							prefabs.Add(proto.prefab);
						}
					}
				}
				if (prefabs.Count == 0)
				{
					foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
					{
						if (go.name.StartsWith("ForrestTree") && go.transform.parent == null && !prefabs.Contains(go))
						{
							prefabs.Add(go);
						}
					}
				}
				if (prefabs.Count == 0)
				{
					return;
				}
				keeper = new GameObject("HighwayTreeKeeper") { hideFlags = HideFlags.HideAndDontSave };
				keeper.SetActive(false);
				foreach (GameObject prefab in prefabs)
				{
					Model model = Read(prefab);
					if (model != null)
					{
						Models.Add(model);
					}
				}
				Mod.Log.LogInfo("Forest tree models captured: " + string.Join(", ", Models.Select(m => m.Name + " (" + m.Height.ToString("0.0") + " units, " + m.Parts.Count + " parts)").ToArray()));
				if (Models.Count > 0)
				{
					Part first = Models[0].Parts[0];
					Mod.Log.LogInfo("Tree material: " + (first.Materials.Length > 0 && first.Materials[0] != null ? first.Materials[0].name + " / " + first.Materials[0].shader.name : "none"));
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not read the forest trees: " + e.Message);
			}
		}

		private static Model Read(GameObject prefab)
		{
			Renderer[] renderers;
			LODGroup lod = prefab.GetComponent<LODGroup>();
			if (lod != null && lod.lodCount > 0)
			{
				renderers = lod.GetLODs()[0].renderers;
			}
			else
			{
				renderers = prefab.GetComponentsInChildren<MeshRenderer>(true).Where(r => !r.name.Contains("illboard")).Cast<Renderer>().ToArray();
			}

			Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
			Model model = new Model { Name = prefab.name };
			Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
			Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
			foreach (Renderer r in renderers)
			{
				MeshRenderer mr = r as MeshRenderer;
				if (mr == null || !mr.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
				{
					continue;
				}
				Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
				Bounds b = filter.sharedMesh.bounds;
				for (int i = 0; i < 8; i++)
				{
					Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
					Vector3 w = m.MultiplyPoint3x4(corner);
					min = Vector3.Min(min, w);
					max = Vector3.Max(max, w);
				}
				model.Parts.Add(new Part
				{
					Mesh = filter.sharedMesh,
					Materials = mr.sharedMaterials,
					Pos = m.GetColumn(3),
					Rot = m.rotation,
					Scale = m.lossyScale
				});
			}
			model.Height = max.y - min.y;
			if (model.Parts.Count == 0 || model.Height < 0.01f)
			{
				return null;
			}
			// Stand it on the ground, centred over its origin.
			Vector3 shift = new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f);
			foreach (Part part in model.Parts)
			{
				part.Pos -= shift;
				GameObject holder = new GameObject(prefab.name + "_" + part.Mesh.name);
				holder.transform.SetParent(keeper.transform, false);
				holder.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
				holder.AddComponent<MeshRenderer>().sharedMaterials = part.Materials;
			}
			return model;
		}
	}
}
