using System.Collections.Generic;
using UnityEngine;

namespace HalfGripBombRace
{
	/// <summary>
	/// A block of C4 stuck on a car's driver-side door, with a red and a green bulb on its face. It is a small box mesh drawn unlit with the
	/// art's texture, parented to the car so it follows it, and sized from the car's own width.
	/// </summary>
	internal class C4Visual : MonoBehaviour
	{
		// The block's size in its own units (width, height and depth), before it is scaled to the car. The face texture is 24x16.
		private const float W = 1.5f;

		private const float H = 1f;

		private const float D = 0.4f;

		// Centres of the two sockets on the face texture, in pixels from its top left (the same numbers the art script prints).
		private static readonly Vector2 RedSocket = new Vector2(18f, 5f);

		private static readonly Vector2 GreenSocket = new Vector2(18f, 11f);

		private const float BulbSize = 0.22f;

		private static readonly Color RedOn = new Color(1f, 0.16f, 0.1f, 1f);

		private static readonly Color GreenOn = new Color(0.2f, 1f, 0.3f, 1f);

		private static readonly Color Unlit = new Color(0.12f, 0.1f, 0.1f, 1f);

		private static Mesh boxMesh;

		private static Mesh bulbMesh;

		private Material redMaterial;

		private Material greenMaterial;

		private Transform redBulb;

		private Transform greenBulb;

		/// <summary>
		/// Sticks a block on the door of <paramref name="car"/> on the given side (-1 left, 1 right: the driver's side). <paramref name="skip"/>
		/// lists parts of the car that are not its body (the cabin).
		/// </summary>
		internal static C4Visual Attach(CarControllerNew car, HashSet<Transform> skip, int side)
		{
			Texture2D face = Mod.Ctx.Sprites.LoadTexture("bomb_c4.png");
			Shader plain = Shader.Find("Sprites/Default");
			if (face == null || plain == null || car == null)
			{
				return null;
			}
			if (!Measure(car.transform, skip, out Vector3 min, out Vector3 max, out int layer))
			{
				Mod.Log.LogWarning("No body found on " + car.name + " to put the C4 on");
				return null;
			}

			var root = new GameObject("BombC4");
			root.layer = layer;
			root.transform.SetParent(car.transform, false);
			// Sized from the car's width, whatever scale the car carries.
			float width = Mathf.Max(0.5f, max.x - min.x);
			float size = width * Mathf.Max(0.05f, Mod.C4Width.Value) / W;
			Vector3 lossy = car.transform.lossyScale;
			root.transform.localScale = new Vector3(size / Mathf.Max(0.0001f, lossy.x), size / Mathf.Max(0.0001f, lossy.y), size / Mathf.Max(0.0001f, lossy.z));
			// On the door: halfway along the body and halfway up, a little way out from the side, with its face turned out. The face is the
			// block's back (-z), so it is turned a quarter round to look out of the side it is on.
			int sign = side >= 0 ? 1 : -1;
			float edge = sign > 0 ? max.x : min.x;
			root.transform.localPosition = new Vector3(edge + sign * D * size * 0.3f / Mathf.Max(0.0001f, lossy.x), Mathf.Lerp(min.y, max.y, 0.5f), Mathf.Lerp(min.z, max.z, 0.5f));
			root.transform.localRotation = Quaternion.Euler(0f, sign > 0 ? -90f : 90f, 0f);

			var visual = root.AddComponent<C4Visual>();
			if (boxMesh == null)
			{
				boxMesh = MakeBox();
				bulbMesh = MakeCube();
			}

			AddPart(root.transform, "Block", boxMesh, face, plain, Color.white, Vector3.zero, Vector3.one, layer);
			visual.redBulb = AddBulb(root.transform, "Red", RedSocket, plain, layer, out visual.redMaterial);
			visual.greenBulb = AddBulb(root.transform, "Green", GreenSocket, plain, layer, out visual.greenMaterial);
			visual.SetLights(FuseState.Dim, FuseState.Dim);

			if (Mod.DumpInfo.Value)
			{
				Mod.Log.LogInfo($"C4 on {car.name} ({(sign > 0 ? "right" : "left")} door): body x {min.x:0.0}..{max.x:0.0} y {min.y:0.0}..{max.y:0.0} z {min.z:0.0}..{max.z:0.0}, layer {layer}, size {size:0.00}, car scale {lossy}");
			}
			return visual;
		}

		/// <summary>The bulbs' brightness, 0 (out) to 1 (flash).</summary>
		internal void SetLights(float red, float green)
		{
			Light(redMaterial, redBulb, RedOn, red);
			Light(greenMaterial, greenBulb, GreenOn, green);
		}

		private static void Light(Material material, Transform bulb, Color on, float level)
		{
			if (material == null)
			{
				return;
			}
			level = Mathf.Clamp01(level);
			material.color = Color.Lerp(Unlit, on, level);
			// A flash also swells a little, so it reads from a distance.
			float swell = 1f + 0.6f * level;
			bulb.localScale = new Vector3(BulbSize, BulbSize, BulbSize) * swell;
		}

		private static Transform AddBulb(Transform parent, string name, Vector2 socket, Shader plain, int layer, out Material material)
		{
			// Texture pixels from the top left to a point on the face: the face is at the back (-z) and seen from behind, with +x to the right.
			float x = (socket.x / 24f - 0.5f) * W;
			float y = (0.5f - socket.y / 16f) * H;
			GameObject go = AddPart(parent, name + "Bulb", bulbMesh, null, plain, Unlit, new Vector3(x, y, -D * 0.5f - BulbSize * 0.2f), Vector3.one * BulbSize, layer);
			material = go.GetComponent<MeshRenderer>().sharedMaterial;
			return go.transform;
		}

		private static GameObject AddPart(Transform parent, string name, Mesh mesh, Texture2D texture, Shader plain, Color colour, Vector3 position, Vector3 scale, int layer)
		{
			var go = new GameObject(name);
			go.layer = layer;
			go.transform.SetParent(parent, false);
			go.transform.localPosition = position;
			go.transform.localScale = scale;
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer renderer = go.AddComponent<MeshRenderer>();
			var material = new Material(plain) { name = "BombC4 " + name, hideFlags = HideFlags.HideAndDontSave, color = colour };
			if (texture != null)
			{
				material.mainTexture = texture;
			}
			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			return go;
		}

		// The extent of the car's body in the car's own space (x right, y up, z forward), and the layer it is drawn on (that of its biggest mesh).
		private static bool Measure(Transform car, HashSet<Transform> skip, out Vector3 min, out Vector3 max, out int layer)
		{
			min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
			max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
			layer = car.gameObject.layer;
			float biggest = 0f;
			bool found = false;
			foreach (MeshRenderer renderer in car.GetComponentsInChildren<MeshRenderer>(false))
			{
				if (!renderer.enabled || InsideAny(renderer.transform, skip))
				{
					continue;
				}
				Bounds b = renderer.bounds;
				if (b.size.sqrMagnitude < 0.05f)
				{
					continue;
				}
				for (int i = 0; i < 8; i++)
				{
					Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
					Vector3 local = car.InverseTransformPoint(corner);
					min = Vector3.Min(min, local);
					max = Vector3.Max(max, local);
				}
				float volume = b.size.x * b.size.y * b.size.z;
				if (volume > biggest)
				{
					biggest = volume;
					layer = renderer.gameObject.layer;
				}
				found = true;
			}
			return found;
		}

		private static bool InsideAny(Transform t, HashSet<Transform> skip)
		{
			if (skip == null)
			{
				return false;
			}
			for (Transform p = t; p != null; p = p.parent)
			{
				if (skip.Contains(p))
				{
					return true;
				}
			}
			return false;
		}

		// A box W x H x D whose back face (-z) shows the whole texture, and whose other faces show one plain pixel of it.
		private static Mesh MakeBox()
		{
			float x = W * 0.5f, y = H * 0.5f, z = D * 0.5f;
			var verts = new List<Vector3>();
			var uvs = new List<Vector2>();
			var tris = new List<int>();
			Vector2 plain = new Vector2(1.5f / 24f, 1.5f / 16f);

			void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool textured)
			{
				int i = verts.Count;
				verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
				if (textured)
				{
					uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
				}
				else
				{
					uvs.Add(plain); uvs.Add(plain); uvs.Add(plain); uvs.Add(plain);
				}
				tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
				tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
			}

			// Back face: bottom left, bottom right, top right, top left as seen from behind the car.
			Quad(new Vector3(-x, -y, -z), new Vector3(x, -y, -z), new Vector3(x, y, -z), new Vector3(-x, y, -z), true);
			Quad(new Vector3(-x, -y, z), new Vector3(x, -y, z), new Vector3(x, y, z), new Vector3(-x, y, z), false);
			Quad(new Vector3(-x, y, -z), new Vector3(x, y, -z), new Vector3(x, y, z), new Vector3(-x, y, z), false);
			Quad(new Vector3(-x, -y, -z), new Vector3(x, -y, -z), new Vector3(x, -y, z), new Vector3(-x, -y, z), false);
			Quad(new Vector3(-x, -y, -z), new Vector3(-x, y, -z), new Vector3(-x, y, z), new Vector3(-x, -y, z), false);
			Quad(new Vector3(x, -y, -z), new Vector3(x, y, -z), new Vector3(x, y, z), new Vector3(x, -y, z), false);
			return Build("BombC4Box", verts, uvs, tris);
		}

		private static Mesh MakeCube()
		{
			var verts = new List<Vector3>();
			var uvs = new List<Vector2>();
			var tris = new List<int>();
			float h = 0.5f;
			void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
			{
				int i = verts.Count;
				verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
				for (int k = 0; k < 4; k++)
				{
					uvs.Add(Vector2.zero);
				}
				tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
				tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
			}
			Quad(new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(-h, h, -h));
			Quad(new Vector3(-h, -h, h), new Vector3(h, -h, h), new Vector3(h, h, h), new Vector3(-h, h, h));
			Quad(new Vector3(-h, h, -h), new Vector3(h, h, -h), new Vector3(h, h, h), new Vector3(-h, h, h));
			Quad(new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, -h, h), new Vector3(-h, -h, h));
			Quad(new Vector3(-h, -h, -h), new Vector3(-h, h, -h), new Vector3(-h, h, h), new Vector3(-h, -h, h));
			Quad(new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(h, h, h), new Vector3(h, -h, h));
			return Build("BombC4Bulb", verts, uvs, tris);
		}

		private static Mesh Build(string name, List<Vector3> verts, List<Vector2> uvs, List<int> tris)
		{
			var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
			mesh.SetVertices(verts);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(tris, 0);
			// The sprite shader multiplies by the vertex colour, so say it is white rather than leave it to a default.
			var colours = new Color32[verts.Count];
			for (int i = 0; i < colours.Length; i++)
			{
				colours[i] = new Color32(255, 255, 255, 255);
			}
			mesh.colors32 = colours;
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}
	}
}
