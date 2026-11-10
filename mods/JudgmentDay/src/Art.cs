using System.Collections.Generic;
using UnityEngine;

namespace JudgmentDay
{
	/// <summary>
	/// The gun you hold while aiming is a thin 3D model the game extrudes from the gun's picture. The shotgun's model is not readable
	/// (only its box is known), so the Judgment Day picture is extruded into that box: the axes come from the box's sizes, the
	/// directions from how the model sits under the aim transform (the muzzle must point where the gun aims, the top up). Same method
	/// as the Railgun's.
	/// </summary>
	internal static class Art
	{
		private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

		private static readonly Dictionary<string, Mapping> maps = new Dictionary<string, Mapping>();

		/// <summary>How a picture pixel maps into the held model's box: which axes the picture's width, height and thickness run along.</summary>
		internal class Mapping
		{
			public int W;

			public int H;

			public int T;

			public float SW;

			public float SH;

			public Bounds Box;

			public int PicW;

			public int PicH;

			/// <summary>The model-space point of a picture pixel (x along the row, y down the rows), on the middle of the thickness.</summary>
			public Vector3 Point(float px, float py)
			{
				float u = px / PicW;
				float vUp = 1f - py / PicH;
				var p = new Vector3();
				p[W] = Box.min[W] + (SW > 0f ? u : 1f - u) * Box.size[W];
				p[H] = Box.min[H] + (SH > 0f ? vUp : 1f - vUp) * Box.size[H];
				p[T] = Box.center[T];
				return p;
			}
		}

		/// <summary>The lever of the held gun: a mesh of its own that turns about the hinge, so the spin-cock can open and close it.</summary>
		internal class HeldLever
		{
			public Transform Pivot;

			public Vector3 Axis;

			public float Sign;

			/// <summary>Turns the lever this many degrees clockwise as seen in the picture (0 = closed).</summary>
			public void Set(float degrees)
			{
				if (Pivot != null)
				{
					Pivot.localRotation = Quaternion.AngleAxis(Sign * degrees, Axis);
				}
			}

			public void Remove()
			{
				if (Pivot != null)
				{
					Object.Destroy(Pivot.gameObject);
				}
			}
		}

		internal class HeldParts
		{
			public MeshRenderer Body;

			public HeldLever Lever;

		}

		// The held model's pictures by size: the body without its lever (258x48); the lever is a mesh of its own. The pump part (33x15) is hidden: this gun has no pump. The shell stays the game's.
		private static string FileFor(int width, int height)
		{
			if (width == 258 && height == 48)
			{
				return "jd_body.png";
			}
			return null;
		}

		// The lever as its own held mesh: its picture is a 258x48 layer with the loop in place, extruded into the same box as the body so it lines up,
		// and hung under a pivot object placed at the hinge so turning the pivot turns the lever about it.
		private static HeldLever BuildHeldPart(Gun gun, MeshRenderer body, Mesh source, Material original, string bodyKey, string layerFile, float hingeX, float hingeY, string label)
		{
			if (!maps.TryGetValue(bodyKey, out Mapping map) || map == null)
			{
				Mod.Log.LogWarning("The held body has no picture mapping, so the held lever stays baked in the body");
				return null;
			}
			Texture2D art = Mod.Ctx.Sprites.LoadTexture(layerFile);
			if (art == null || art.width != 258 || art.height != 48)
			{
				Mod.Log.LogWarning(layerFile + " is missing or not 258x48: no held " + label);
				return null;
			}
			string key = layerFile + "|" + bodyKey;
			if (!meshes.TryGetValue(key, out Mesh mesh) || mesh == null)
			{
				mesh = Extrude(art, source, body, gun.aimModel, out string reason, out Mapping ignored, 1.12f);
				if (mesh == null)
				{
					Mod.Log.LogWarning("Held " + label + ": " + reason);
					return null;
				}
				meshes[key] = mesh;
			}
			var pivot = new GameObject("jd held " + label + " pivot");
			pivot.layer = body.gameObject.layer;
			pivot.transform.SetParent(body.transform, false);
			Vector3 hinge = map.Point(hingeX, hingeY);
			pivot.transform.localPosition = hinge;
			var part = new GameObject("jd held " + label);
			part.layer = body.gameObject.layer;
			part.transform.SetParent(pivot.transform, false);
			part.transform.localPosition = -hinge;
			part.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = part.AddComponent<MeshRenderer>();
			var material = new Material(original) { name = "Judgment Day " + label, hideFlags = HideFlags.HideAndDontSave };
			material.SetTexture("_BaseMap", art);
			material.SetTexture("_MainTex", art);
			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = body.shadowCastingMode;
			renderer.receiveShadows = body.receiveShadows;

			// Which way is "clockwise in the picture" about the thickness axis in model space: a point right of the hinge must move down the picture.
			Vector3 axis = map.T == 0 ? Vector3.right : map.T == 1 ? Vector3.up : Vector3.forward;
			Vector3 toPoint = map.Point(hingeX + 20f, hingeY) - hinge;
			Vector3 down = map.Point(hingeX, hingeY + 1f) - map.Point(hingeX, hingeY);
			Vector3 moved = Quaternion.AngleAxis(5f, axis) * toPoint - toPoint;
			float sign = Vector3.Dot(moved, down) >= 0f ? 1f : -1f;
			Mod.Log.LogInfo($"Held {label} built: hinge {hinge}, axis {map.T}, clockwise sign {sign}");
			return new HeldLever { Pivot = pivot.transform, Axis = axis, Sign = sign };
		}

		/// <summary>Swaps the held model's pictures and shapes. Returns the body (the one the flip turns about) and the held lever.</summary>
		public static HeldParts ReskinHeldModel(Gun gun)
		{
			if (gun.aimModel == null)
			{
				Mod.Log.LogWarning("The shotgun has no aim model, so the held Judgment Day keeps the shotgun's shape");
				return null;
			}
			MeshRenderer bodyRenderer = null;
			HeldLever heldLever = null;
			int swapped = 0;
			foreach (MeshRenderer renderer in gun.aimModel.GetComponentsInChildren<MeshRenderer>(true))
			{
				MeshFilter filter = renderer.GetComponent<MeshFilter>();
				Material original = renderer.sharedMaterial;
				if (filter == null || filter.sharedMesh == null || original == null || original.mainTexture == null)
				{
					continue;
				}
				Texture sourceTexture = original.mainTexture;
				Mesh source = filter.sharedMesh;
				string file = FileFor(sourceTexture.width, sourceTexture.height);
				if (Mod.DumpInfo.Value)
				{
					Mod.Log.LogInfo($"[held] '{renderer.name}' mesh='{source.name}' size={source.bounds.size} center={source.bounds.center} readable={source.isReadable} texture='{sourceTexture.name}' {sourceTexture.width}x{sourceTexture.height} -> {(file ?? "kept")}");
				}
				if (sourceTexture.width == 33 && sourceTexture.height == 15)
				{
					// The pump's held part: a lever gun has none.
					renderer.enabled = false;
					swapped++;
					continue;
				}
				if (file == null)
				{
					continue;
				}
				Texture2D art = Mod.Ctx.Sprites.LoadTexture(file);
				if (art == null || art.width != sourceTexture.width || art.height != sourceTexture.height)
				{
					Mod.Log.LogWarning("Held part '" + renderer.name + "': " + file + " is missing or the wrong size");
					continue;
				}

				Mesh mesh;
				string reason = null;
				string key = file + "|" + source.bounds.center + source.bounds.size + "|" + gun.aimModel.InverseTransformDirection(renderer.transform.right) + gun.aimModel.InverseTransformDirection(renderer.transform.up);
				if (!meshes.TryGetValue(key, out mesh) || mesh == null)
				{
					mesh = Extrude(art, source, renderer, gun.aimModel, out reason, out Mapping mapping);
					if (mesh != null)
					{
						meshes[key] = mesh;
						maps[key] = mapping;
					}
				}
				if (mesh == null)
				{
					Mod.Log.LogWarning($"Held part '{renderer.name}': {reason}; only its picture is swapped, the shape stays the shotgun's");
				}
				else
				{
					filter.sharedMesh = mesh;
					Mod.Log.LogInfo($"Held part '{renderer.name}' built from {file}");
				}
				var material = new Material(original) { name = "Judgment Day " + file, hideFlags = HideFlags.HideAndDontSave };
				material.SetTexture("_BaseMap", art);
				material.SetTexture("_MainTex", art);
				renderer.sharedMaterial = material;
				if (file == "jd_body.png")
				{
					bodyRenderer = renderer;
					heldLever = BuildHeldPart(gun, renderer, source, original, key, "jd_lever_layer.png", Lever.HingeX, Lever.HingeY, "lever");
				}
				swapped++;
			}
			Mod.Log.LogInfo("Held Judgment Day model: replaced " + swapped + " parts, held lever " + (heldLever != null ? "built" : "not built") + "");
			return new HeldParts { Body = bodyRenderer, Lever = heldLever };
		}

		// Extrudes the picture into the box of the game's own mesh, along the axes and in the directions that suit the aim.
		private static Mesh Extrude(Texture2D art, Mesh source, MeshRenderer renderer, Transform aim, out string reason, out Mapping map, float thick = 1f)
		{
			reason = null;
			map = null;
			Bounds b = source.bounds;
			int w;
			int h;
			float sw;
			float sh;
			if (source.isReadable && source.uv != null && source.uv.Length == source.vertexCount)
			{
				Vector3[] verts = source.vertices;
				Vector2[] uvs = source.uv;
				// How far each axis moves from the picture's left to its right, and from its bottom to its top.
				var across = new float[3];
				var up = new float[3];
				for (int axis = 0; axis < 3; axis++)
				{
					across[axis] = Spread(verts, uvs, axis, true);
					up[axis] = Spread(verts, uvs, axis, false);
				}
				w = Biggest(across, -1);
				h = Biggest(up, w);
				sw = across[w] >= 0f ? 1f : -1f;
				sh = up[h] >= 0f ? 1f : -1f;
			}
			else
			{
				// Not readable: only the box is known. The picture's width runs along the box's longest side and its height along the
				// next. Which way each runs comes from how the model sits under the aim transform, whose +z is where the gun points
				// and whose +y is up: the muzzle (the picture's left) must face forward and the picture's top must face up.
				w = LongestAxis(b.size, -1);
				h = LongestAxis(b.size, w);
				Vector3 alongWidth = aim.InverseTransformDirection(renderer.transform.TransformDirection(Axis(w)));
				Vector3 alongHeight = aim.InverseTransformDirection(renderer.transform.TransformDirection(Axis(h)));
				sw = alongWidth.z < 0f ? 1f : -1f;
				sh = alongHeight.y > 0f ? 1f : -1f;
				Mod.Log.LogInfo($"Held mesh is not readable: width axis {w} runs {alongWidth} under the aim, height axis {h} runs {alongHeight}");
				if (Mathf.Abs(alongWidth.z) < 0.5f)
				{
					Mod.Log.LogWarning("The held gun's length does not run along the aim; the model may sit sideways");
				}
			}
			int t = 3 - w - h;
			float width = b.size[w];
			float height = b.size[h];
			float pixelW = width / art.width;
			float pixelH = height / art.height;
			if (pixelW <= 0f || Mathf.Abs(pixelW - pixelH) > pixelW * 0.08f)
			{
				reason = $"its pixels are {pixelW:0.####} by {pixelH:0.####} on axes {w},{h}";
				return null;
			}
			float scale = Mathf.Clamp(1f, 0.2f, 1.5f);

			// The picture is first built flat in pixel units (x right, z down the rows, y the thickness, one pixel thick), then moved
			// into the game's box.
			Mesh flat = SpriteMesh.Build(art, new SpriteMesh.Frame { x0 = 0f, xDir = 1f, dx = 1f, z0 = 0f, dz = 1f, yMin = -0.5f, yMax = 0.5f }, "JudgmentDay flat");
			Vector3[] fv = flat.vertices;
			Vector3[] fn = flat.normals;
			var nv = new Vector3[fv.Length];
			var nn = new Vector3[fv.Length];
			for (int i = 0; i < fv.Length; i++)
			{
				float u = fv[i].x / art.width;
				float vUp = 1f - fv[i].z / art.height;
				Vector3 p = new Vector3();
				p[w] = b.min[w] + (sw > 0f ? u : 1f - u) * width;
				p[h] = b.min[h] + (sh > 0f ? vUp : 1f - vUp) * height;
				p[t] = b.center[t] + fv[i].y * b.size[t] * thick;
				// Smaller (or bigger) about the middle of the game's box.
				nv[i] = b.center + (p - b.center) * scale;
				Vector3 n = new Vector3();
				n[w] = fn[i].x * sw;
				n[h] = fn[i].z * -sh;
				n[t] = fn[i].y;
				nn[i] = n;
			}
			// A mirror image turns the triangles inside out: turn them back.
			Vector3 ex = Axis(w) * sw;
			Vector3 ey = Axis(t);
			Vector3 ez = Axis(h) * -sh;
			bool mirrored = Vector3.Dot(Vector3.Cross(ex, ey), ez) < 0f;
			int[] tris = flat.triangles;
			if (mirrored)
			{
				for (int i = 0; i + 2 < tris.Length; i += 3)
				{
					int swap = tris[i + 1];
					tris[i + 1] = tris[i + 2];
					tris[i + 2] = swap;
				}
			}
			var mesh = new Mesh { name = "JudgmentDay held body", hideFlags = HideFlags.HideAndDontSave };
			mesh.indexFormat = nv.Length > 60000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
			mesh.vertices = nv;
			mesh.normals = nn;
			mesh.uv = flat.uv;
			mesh.triangles = tris;
			mesh.RecalculateBounds();
			Mod.Log.LogInfo($"Held body extruded: width axis {w} (sign {sw}), height axis {h} (sign {sh}), thickness axis {t}, pixel {pixelW:0.#####}, mirrored={mirrored}");
			map = new Mapping { W = w, H = h, T = t, SW = sw, SH = sh, Box = b, PicW = art.width, PicH = art.height };
			return mesh;
		}

		private static int LongestAxis(Vector3 size, int skip)
		{
			int best = -1;
			for (int i = 0; i < 3; i++)
			{
				if (i != skip && (best < 0 || size[i] > size[best]))
				{
					best = i;
				}
			}
			return best;
		}

		private static Vector3 Axis(int i)
		{
			return i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
		}

		// The average position on an axis of the vertices in the high end of a UV axis, minus that of the low end.
		private static float Spread(Vector3[] verts, Vector2[] uvs, int axis, bool horizontal)
		{
			float low = 0f;
			float high = 0f;
			int nl = 0;
			int nh = 0;
			for (int i = 0; i < verts.Length; i++)
			{
				float c = horizontal ? uvs[i].x : uvs[i].y;
				if (c < 0.25f)
				{
					low += verts[i][axis];
					nl++;
				}
				else if (c > 0.75f)
				{
					high += verts[i][axis];
					nh++;
				}
			}
			return nl > 0 && nh > 0 ? high / nh - low / nl : 0f;
		}

		private static int Biggest(float[] values, int skip)
		{
			int best = -1;
			for (int i = 0; i < 3; i++)
			{
				if (i != skip && (best < 0 || Mathf.Abs(values[i]) > Mathf.Abs(values[best])))
				{
					best = i;
				}
			}
			return best;
		}
	}
}
