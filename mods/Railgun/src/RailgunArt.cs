using System.Collections.Generic;
using UnityEngine;

namespace HalfGripRailgun
{
	/// <summary>
	/// The gun you hold while aiming is a thin 3D model the game extrudes from the gun's picture. The shotgun's model is not laid out
	/// like the SMG's (its picture runs along x and y, with the thickness on z, and at a scale of one unit for the whole gun), and the
	/// game's mesh is not readable (so its shape cannot be copied, only its box). The railgun's picture is extruded into that box: the
	/// axes come from the box's sizes (length, height, thickness), the directions from how the model sits under the aim transform
	/// (the muzzle must point where the gun aims, the top up). The model has one texture per charge step: the coils light one by one
	/// as the charge builds, with arcs flickering between the spark plugs.
	/// </summary>
	internal static class RailgunArt
	{
		/// <summary>The held body, once it has been replaced: its renderer and the picture of each charge step, made when first needed.</summary>
		internal class HeldBody
		{
			public MeshRenderer Renderer;

			public Material Original;

			public Color32[] Plain;

			public Color32[][] Coils;

			private Texture2D live;

			private int shown = -1;

			/// <summary>Shows this charge (0..6 capacitors, the one filling lit from the bottom up), the arcs' shape (0 = none) and the ammo lamp.</summary>
			public void Show(float capacitors, int variant, bool loaded, int shots)
			{
				if (Renderer == null)
				{
					return;
				}
				int full = Mathf.Min(RailLayout.Coils, (int)(capacitors + 0.0001f));
				if (full < 2)
				{
					variant = 0;
				}
				int key = ((RailPixels.Step(capacitors) * 4 + variant) * 2 + (loaded ? 1 : 0)) * 10 + Mathf.Clamp(shots, 0, 9);
				if (key == shown)
				{
					return;
				}
				shown = key;
				if (live == null)
				{
					live = RailPixels.Texture((Color32[])Plain.Clone(), "railgun held");
					var material = new Material(Original) { name = "Railgun held", hideFlags = HideFlags.HideAndDontSave };
					material.SetTexture("_BaseMap", live);
					material.SetTexture("_MainTex", live);
					Renderer.sharedMaterial = material;
				}
				Color32[] pixels = (Color32[])Plain.Clone();
				if (Coils != null)
				{
					RailPixels.Over(pixels, RailPixels.Fill(Coils, capacitors, variant));
				}
				RailPixels.Over(pixels, RailPixels.Lamp(loaded));
				RailPixels.Over(pixels, RailPixels.Digit(shots));
				live.SetPixels32(pixels);
				live.Apply();
			}
		}

		private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

		// The held model's pictures by size: the body with the pump in its place (258x48), the pump (33x15) and a shell (12x24).
		private static string FileFor(int width, int height)
		{
			if (width == 258 && height == 48)
			{
				return "railgun_held.png";
			}
			if (width == 33 && height == 15)
			{
				return "railgun_pump.png";
			}
			if (width == 12 && height == 24)
			{
				return "railgun_ball.png";
			}
			return null;
		}

		public static HeldBody ReskinHeldModel(Gun gun)
		{
			if (gun.aimModel == null)
			{
				Mod.Log.LogWarning("The shotgun has no aim model, so the held Railgun keeps the shotgun's shape");
				return null;
			}
			HeldBody body = null;
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
					Mod.Log.LogInfo($"[held] '{renderer.name}' mesh='{source.name}' size={source.bounds.size} center={source.bounds.center} verts={source.vertexCount} readable={source.isReadable} texture='{sourceTexture.name}' {sourceTexture.width}x{sourceTexture.height} -> {(file ?? "kept")}");
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
				string key = file + "|" + source.bounds.center + source.bounds.size + "|" + Mod.HeldScale.Value + "|" + gun.aimModel.InverseTransformDirection(renderer.transform.right) + gun.aimModel.InverseTransformDirection(renderer.transform.up);
				if (!meshes.TryGetValue(key, out mesh) || mesh == null)
				{
					mesh = Extrude(art, source, renderer, gun.aimModel, out reason);
					if (mesh != null)
					{
						meshes[key] = mesh;
					}
				}
				if (mesh == null)
				{
					// The picture is swapped but the shape is the shotgun's.
					Mod.Log.LogWarning($"Held part '{renderer.name}': {reason}; only its picture is swapped, the shape stays the shotgun's");
				}
				else
				{
					filter.sharedMesh = mesh;
					Mod.Log.LogInfo($"Held part '{renderer.name}' built from {file} ({(source.isReadable ? "from the game's mesh" : "own mesh in the game's box")})");
				}
				if (file == "railgun_held.png")
				{
					body = new HeldBody { Renderer = renderer, Original = original, Plain = art.GetPixels32(), Coils = RailPixels.LoadCoils() };
					body.Show(0f, 0, false, 0);
				}
				else
				{
					var material = new Material(original) { name = "Railgun " + file, hideFlags = HideFlags.HideAndDontSave };
					material.SetTexture("_BaseMap", art);
					material.SetTexture("_MainTex", art);
					renderer.sharedMaterial = material;
				}
				swapped++;
			}
			Mod.Log.LogInfo("Held Railgun model: replaced " + swapped + " parts");
			return body;
		}

		// Extrudes the picture into the box of the game's own mesh, along the axes and in the directions that suit the aim.
		private static Mesh Extrude(Texture2D art, Mesh source, MeshRenderer renderer, Transform aim, out string reason)
		{
			reason = null;
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
			float scale = Mathf.Clamp(Mod.HeldScale.Value, 0.2f, 1.5f);

			// The picture is first built flat in pixel units (x right, z down the rows, y the thickness, one pixel thick), then moved
			// into the game's box.
			Mesh flat = SpriteMesh.Build(art, new SpriteMesh.Frame { x0 = 0f, xDir = 1f, dx = 1f, z0 = 0f, dz = 1f, yMin = -0.5f, yMax = 0.5f }, "Railgun flat");
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
				p[t] = b.center[t] + fv[i].y * b.size[t];
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
			var mesh = new Mesh { name = "Railgun held body", hideFlags = HideFlags.HideAndDontSave };
			mesh.indexFormat = nv.Length > 60000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
			mesh.vertices = nv;
			mesh.normals = nn;
			mesh.uv = flat.uv;
			mesh.triangles = tris;
			mesh.RecalculateBounds();
			Mod.Log.LogInfo($"Held body extruded: width axis {w} (sign {sw}), height axis {h} (sign {sh}), thickness axis {t}, pixel {pixelW:0.#####}, mirrored={mirrored}");
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
