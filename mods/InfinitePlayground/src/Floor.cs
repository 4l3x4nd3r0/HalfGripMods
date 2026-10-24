using HarmonyLib;
using UnityEngine;

namespace HalfGripInfinitePlayground
{
	// The wheels cast against every layer except Ignore Raycast (CarControllerNew.CastGround), and a collider with no HandlingModifier
	// gives plain grip, so a box collider is drivable road. Hold R still resets to the playground's own roads.
	[HarmonyPatch(typeof(Playground), "Awake")]
	internal static class AddFloor
	{
		private static void Postfix(Playground __instance)
		{
			if (!Mod.FloorEnabled.Value)
			{
				return;
			}
			// The car is more than 1000 m from the path soon after leaving, which would nag "Hold R to reset".
			PlayerResetLogic.noHints = true;
			__instance.gameObject.AddComponent<EndlessFloor>();
		}
	}

	[HarmonyPatch(typeof(Playground), "OnDisable")]
	internal static class RemoveHints
	{
		private static void Postfix()
		{
			PlayerResetLogic.noHints = false;
		}
	}

	// A static floor that follows the car in x/z and never moves in y. It snaps to whole checker pairs so the pattern stays fixed in the world.
	// Coordinates grow with distance, but even 60 km out float precision is a few millimetres, fine for a speed test.
	internal class EndlessFloor : MonoBehaviour
	{
		private Transform floor;
		private Transform car;
		private float height;
		private float snap;

		private void Start()
		{
			CarControllerNew player = null;
			foreach (CarControllerNew candidate in FindObjectsByType<CarControllerNew>(FindObjectsSortMode.None))
			{
				if (candidate.isPlayer)
				{
					player = candidate;
					break;
				}
			}
			if (player == null)
			{
				Mod.Log.LogWarning("No player car in the playground, no floor.");
				return;
			}
			car = player.transform;

			Renderer road = null;
			height = car.position.y - 1f;
			RaycastHit[] hits = Physics.RaycastAll(car.position + Vector3.up, Vector3.down, 50f, -5, QueryTriggerInteraction.Ignore);
			float nearest = float.MaxValue;
			foreach (RaycastHit hit in hits)
			{
				if (hit.rigidbody == player.rb || hit.collider.transform.IsChildOf(car) || hit.distance >= nearest)
				{
					continue;
				}
				nearest = hit.distance;
				height = hit.point.y;
				road = hit.collider.GetComponent<Renderer>();
			}
			if (nearest == float.MaxValue)
			{
				Mod.Log.LogWarning("No ground under the car, putting the floor 1 m below it.");
			}
			height += Mod.HeightOffset.Value;

			float size = Mathf.Max(100f, Mod.Size.Value);
			float tile = Mathf.Max(0.5f, Mod.TileSize.Value);
			snap = tile * 2f;

			GameObject go = new GameObject("InfinitePlayground Floor");
			go.transform.SetParent(transform, false);
			floor = go.transform;
			floor.position = new Vector3(Snap(car.position.x), height, Snap(car.position.z));

			BoxCollider box = go.AddComponent<BoxCollider>();
			box.size = new Vector3(size, 1f, size);
			box.center = new Vector3(0f, -0.5f, 0f);

			GameObject visual = new GameObject("Visual");
			visual.transform.SetParent(floor, false);
			visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
			visual.AddComponent<MeshFilter>().sharedMesh = Quad(size);
			Material material = Material(road);
			if (material != null)
			{
				Texture2D checker = Checker();
				float repeat = size / snap;
				material.SetTexture("_BaseMap", checker);
				material.SetTexture("_MainTex", checker);
				material.SetTextureScale("_BaseMap", new Vector2(repeat, repeat));
				material.SetTextureScale("_MainTex", new Vector2(repeat, repeat));
				visual.AddComponent<MeshRenderer>().sharedMaterial = material;
			}
			Mod.Log.LogInfo("Endless floor at height " + height.ToString("0.00") + ", " + size.ToString("0") + " m square");
		}

		private void FixedUpdate()
		{
			if (floor != null && car != null)
			{
				floor.position = new Vector3(Snap(car.position.x), height, Snap(car.position.z));
			}
		}

		private float Snap(float value)
		{
			return Mathf.Round(value / snap) * snap;
		}

		private static Material Material(Renderer road)
		{
			if (road != null && road.sharedMaterial != null)
			{
				return new Material(road.sharedMaterial);
			}
			Shader plain = Shader.Find("Sprites/Default");
			return plain != null ? new Material(plain) : null;
		}

		// One checker pair: 2x2 pixels, point-filtered, repeating.
		private static Texture2D Checker()
		{
			Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Repeat
			};
			Color dark = new Color(0.30f, 0.30f, 0.32f, 1f);
			Color light = new Color(0.55f, 0.55f, 0.58f, 1f);
			texture.SetPixels(new[] { dark, light, light, dark });
			texture.Apply();
			return texture;
		}

		private static Mesh Quad(float size)
		{
			float h = size / 2f;
			Mesh mesh = new Mesh { name = "InfinitePlayground Quad" };
			mesh.vertices = new[] { new Vector3(-h, -h, 0f), new Vector3(h, -h, 0f), new Vector3(h, h, 0f), new Vector3(-h, h, 0f) };
			mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
			mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
			mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
			mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, size, 1f));
			return mesh;
		}
	}
}
