using System;
using HalfGripTools;
using HalfGripTools.Meshes;
using UnityEngine;

namespace HalfGripHighway
{
	/// <summary>
	/// A few of Highway's roadside pieces for the Map Maker (<c>ctx.Maps.AddProp</c>): a traffic cone, a concrete barrier and an oil drum, built from the
	/// shared mesh kit. They are solid, and they are the same pieces on any map that places them.
	/// </summary>
	internal static class MapPieces
	{
		internal static void Register(ModContext ctx)
		{
			ctx.Maps.AddProp("highway.cone", "Traffic cone", "Highway", scale => Make("Highway cone", new Color(0.95f, 0.42f, 0.05f), scale, b =>
			{
				b.Cone(Vector3.zero, 0.55f, Vector3.up * 1.7f, 0.08f, 10, 1f, true);
				b.Box(Vector3.up * 0.06f, new Vector3(1.4f, 0.12f, 1.4f), Quaternion.identity, 1f);
			}));
			ctx.Maps.AddProp("highway.barrier", "Concrete barrier", "Highway", scale => Make("Highway barrier", new Color(0.62f, 0.62f, 0.6f), scale, b =>
			{
				b.Box(new Vector3(0f, 0.45f, 0f), new Vector3(6f, 0.9f, 1.1f), Quaternion.identity, 2f);
				b.Box(new Vector3(0f, 0.95f, 0f), new Vector3(6f, 0.5f, 0.5f), Quaternion.identity, 2f);
			}));
			ctx.Maps.AddProp("highway.drum", "Oil drum", "Highway", scale => Make("Highway drum", new Color(0.85f, 0.35f, 0.08f), scale, b =>
			{
				b.Cylinder(Vector3.zero, 0.9f, 2.4f, 12);
			}));
		}

		private static GameObject Make(string name, Color colour, float scale, Action<Batch> shape)
		{
			var batch = new Batch(name, colour, true);
			shape(batch);
			var go = new GameObject(name);
			Mesh mesh = batch.ToMesh();
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Get(null, colour);
			go.AddComponent<MeshCollider>().sharedMesh = mesh;
			go.transform.localScale = Vector3.one * Mathf.Max(0.1f, scale);
			return go;
		}
	}
}
