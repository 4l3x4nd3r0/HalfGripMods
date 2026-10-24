using System.Collections.Generic;
using UnityEngine;

namespace HalfGripRpg
{
	/// <summary>A black burn mark left on the ground where a rocket went off. It is darker for a hit on open ground than for one that went off on a car.</summary>
	internal class Scorch : MonoBehaviour
	{
		// Seconds a mark stays at full strength, then takes to fade away.
		private const float Linger = 40f;

		private const float FadeTime = 25f;

		private const int MaxMarks = 14;

		private static readonly List<Scorch> marks = new List<Scorch>();

		private static Mesh quad;

		private static Material material;

		private MeshRenderer drawn;

		private Material own;

		private float strength;

		private float age;

		private static Texture2D burnTexture;

		/// <summary>
		/// Looks for the surface under the blast and burns it. <paramref name="strength"/> is how dark the mark is, 0 to 1;
		/// <paramref name="size"/> is its width in game units.
		/// </summary>
		public static void Place(Vector3 at, Vector3 surfaceNormal, bool onCar, int groundMask, float size, float strength)
		{
			if (Surface() == null)
			{
				return;
			}
			RaycastHit hit;
			bool found = false;
			if (!onCar && surfaceNormal.sqrMagnitude > 0.5f)
			{
				// It went off against something: mark that, if there is a surface right there.
				found = Physics.Raycast(at + surfaceNormal * 0.5f, -surfaceNormal, out hit, 3f, groundMask, QueryTriggerInteraction.Ignore);
			}
			else
			{
				hit = default;
			}
			if (!found)
			{
				// In the air, or on a car: mark the road underneath.
				found = Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out hit, 80f, groundMask, QueryTriggerInteraction.Ignore);
			}
			if (!found)
			{
				return;
			}

			var go = new GameObject("RpgScorch");
			go.transform.position = hit.point + hit.normal * 0.06f;
			// The quad faces -z, so look the other way along the surface normal; a random twist so no two marks match.
			go.transform.rotation = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
			go.transform.localScale = new Vector3(size, size, 1f);
			go.AddComponent<MeshFilter>().sharedMesh = Quad();
			Scorch mark = go.AddComponent<Scorch>();
			mark.drawn = go.AddComponent<MeshRenderer>();
			mark.own = new Material(Surface()) { hideFlags = HideFlags.HideAndDontSave };
			mark.drawn.sharedMaterial = mark.own;
			mark.drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			mark.drawn.receiveShadows = false;
			mark.strength = strength;
			mark.Apply(1f);

			marks.Add(mark);
			while (marks.Count > MaxMarks)
			{
				if (marks[0] != null)
				{
					Destroy(marks[0].gameObject);
				}
				marks.RemoveAt(0);
			}
		}

		private void Update()
		{
			age += Time.deltaTime;
			if (age < Linger)
			{
				return;
			}
			float left = 1f - (age - Linger) / FadeTime;
			if (left <= 0f)
			{
				marks.Remove(this);
				Destroy(own);
				Destroy(gameObject);
				return;
			}
			Apply(left);
		}

		private void Apply(float fade)
		{
			own.color = new Color(1f, 1f, 1f, strength * fade);
		}

		private static Mesh Quad()
		{
			if (quad == null)
			{
				quad = new Mesh { name = "RPG scorch quad", hideFlags = HideFlags.HideAndDontSave };
				quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
				quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
				quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
				quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
			}
			return quad;
		}

		private static Material Surface()
		{
			if (material == null)
			{
				Shader shader = Shader.Find("Sprites/Default");
				if (shader == null)
				{
					Mod.Log.LogWarning("No Sprites/Default shader in this build, so rockets leave no burn marks");
					return null;
				}
				material = new Material(shader) { name = "RpgScorch", hideFlags = HideFlags.HideAndDontSave };
				material.mainTexture = BurnTexture();
			}
			return material;
		}

		// A ragged, near-black blot: dense in the middle, with a rough edge and soot thrown out in streaks.
		private static Texture2D BurnTexture()
		{
			if (burnTexture != null)
			{
				return burnTexture;
			}
			const int size = 128;
			const int grid = 12;
			var rng = new System.Random(1911);
			var lattice = new float[grid + 1, grid + 1];
			for (int y = 0; y <= grid; y++)
			{
				for (int x = 0; x <= grid; x++)
				{
					lattice[x, y] = (float)rng.NextDouble();
				}
			}
			float Noise(float u, float v)
			{
				float gx = u * grid;
				float gy = v * grid;
				int x0 = Mathf.Clamp((int)gx, 0, grid - 1);
				int y0 = Mathf.Clamp((int)gy, 0, grid - 1);
				float fx = Mathf.SmoothStep(0f, 1f, gx - x0);
				float fy = Mathf.SmoothStep(0f, 1f, gy - y0);
				float a = Mathf.Lerp(lattice[x0, y0], lattice[x0 + 1, y0], fx);
				float b = Mathf.Lerp(lattice[x0, y0 + 1], lattice[x0 + 1, y0 + 1], fx);
				return Mathf.Lerp(a, b, fy);
			}

			burnTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
			{
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				hideFlags = HideFlags.HideAndDontSave
			};
			var pixels = new Color32[size * size];
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float u = x / (size - 1f);
					float v = y / (size - 1f);
					float dx = u - 0.5f;
					float dy = v - 0.5f;
					float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
					float angle = Mathf.Atan2(dy, dx);
					// A rough edge: the blot's radius wanders with the angle, and soot streaks run out along some angles.
					float edge = 0.62f + 0.22f * (Noise(Mathf.Cos(angle) * 0.5f + 0.5f, Mathf.Sin(angle) * 0.5f + 0.5f) - 0.5f) * 2f;
					float streak = Mathf.Pow(Noise(angle / 6.2832f + 0.5f, 0.31f), 3f) * 0.5f;
					float body = Mathf.Clamp01((edge + streak - r) / 0.3f);
					float grain = 0.75f + 0.25f * Noise(u * 0.99f, v * 0.99f);
					float alpha = Mathf.Clamp01(body * grain * 1.2f);
					byte shade = (byte)(14 + 18 * Noise(v, u));
					pixels[y * size + x] = new Color32(shade, shade, shade, (byte)(alpha * 255f));
				}
			}
			burnTexture.SetPixels32(pixels);
			burnTexture.Apply();
			return burnTexture;
		}
	}
}
