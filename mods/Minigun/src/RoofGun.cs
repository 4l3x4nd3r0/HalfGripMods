using System.Collections.Generic;
using UnityEngine;

namespace HalfGripMinigun
{
	/// <summary>
	/// The minigun on the roof: a round base on the roof, a pintle with two arms and an armoured shield, a housing with a motor, a
	/// belt-fed ammo box and a grip bar, and a cluster of six barrels in a heat shroud with plates, clamps and a muzzle ring, built
	/// from boxes and cylinders and stuck to the top of the car's body. The parts are drawn with a lit shader borrowed from the game
	/// and small point-filtered textures (scratched steel, chipped olive paint, brass, black iron), so they are solid, take the
	/// scene's light and share the cars' pixel look. It turns toward where the remote is aimed (with a little lag), the barrels
	/// spin with the gun, and its <see cref="Muzzle"/> is where the shots start. Only the outside camera and other players see it; the
	/// cockpit's own camera does not draw the roof.
	/// </summary>
	internal class RoofGun : MonoBehaviour
	{
		private const float TurnSpeed = 300f;

		private const float SpinSpeed = 1800f;

		private enum Skin
		{
			Steel,
			Dark,
			Olive,
			Brass
		}

		private static readonly Dictionary<Skin, Material> materials = new Dictionary<Skin, Material>();

		private Transform car;

		private Transform turret;

		private Transform barrels;

		private NAutomaticGun gun;

		private MiniHeat heat;

		/// <summary>Where the barrels end. The game points it at the target and fires from it.</summary>
		public Transform Muzzle { get; private set; }

		public static RoofGun Build(NAutomaticGun owner, MiniHeat state)
		{
			Transform car = owner.carLink != null && owner.carLink.cc != null ? owner.carLink.cc.transform : null;
			if (car == null)
			{
				CarControllerNew found = null;
				foreach (CarControllerNew candidate in Object.FindObjectsByType<CarControllerNew>(FindObjectsSortMode.None))
				{
					if (candidate.isPlayer)
					{
						found = candidate;
						break;
					}
				}
				car = found != null ? found.transform : null;
			}
			return BuildOn(car, owner, state);
		}

		/// <summary>
		/// Builds the gun on a car model: the race car, or the preview car in the garage (where there is no gun to follow, so it
		/// just rests, pointing forward).
		/// </summary>
		public static RoofGun BuildOn(Transform car, NAutomaticGun owner, MiniHeat state)
		{
			if (car == null)
			{
				Mod.Log.LogWarning("No car to build the roof gun on");
				return null;
			}
			// A gun built earlier is taken off first, so it is not measured as part of the car.
			Transform old = car.Find("MinigunRoof");
			if (old != null)
			{
				DestroyImmediate(old.gameObject);
			}
			var skip = new HashSet<Transform>();
			CameraSwitch cameras = Object.FindFirstObjectByType<CameraSwitch>();
			if (cameras != null && cameras.interiorModels != null)
			{
				foreach (Transform t in cameras.interiorModels)
				{
					if (t != null)
					{
						skip.Add(t);
					}
				}
			}
			if (!Measure(car, skip, out Vector3 min, out Vector3 max, out int layer))
			{
				Mod.Log.LogWarning("No body found on " + car.name + " to put the roof gun on");
				return null;
			}
			if (!EnsureMaterials(car))
			{
				Mod.Log.LogWarning("No shader to build the roof gun with");
				return null;
			}

			var root = new GameObject("MinigunRoof");
			root.layer = layer;
			RoofGun roof = root.AddComponent<RoofGun>();
			roof.car = car;
			roof.gun = owner;
			roof.heat = state;
			// Top of the body, a little behind the middle. The gun is sized against the car (about 13 long in a normal model) rather than in absolute units.
			float length = max.z - min.z;
			float unit = Mathf.Max(0.5f, length / 13f);
			root.transform.SetParent(car, false);
			root.transform.localPosition = new Vector3((min.x + max.x) * 0.5f, max.y - 0.05f * unit, min.z + length * 0.42f);
			root.transform.localRotation = Quaternion.identity;

			Transform r = root.transform;
			// The mount: a wide plate on the roof, a short column, bolts on the plate and the swivel ring.
			roof.Part(PrimitiveType.Cylinder, r, "plate", new Vector3(0f, 0.08f, 0f) * unit, new Vector3(2.0f, 0.08f, 2.0f) * unit, Skin.Dark, layer);
			for (int i = 0; i < 6; i++)
			{
				float a = i * 60f * Mathf.Deg2Rad;
				roof.Part(PrimitiveType.Cylinder, r, "bolt " + i, new Vector3(Mathf.Cos(a) * 0.85f, 0.18f, Mathf.Sin(a) * 0.85f) * unit, new Vector3(0.14f, 0.05f, 0.14f) * unit, Skin.Steel, layer);
			}
			roof.Part(PrimitiveType.Cylinder, r, "column", new Vector3(0f, 0.45f, 0f) * unit, new Vector3(0.9f, 0.3f, 0.9f) * unit, Skin.Steel, layer);
			roof.Part(PrimitiveType.Cylinder, r, "ring", new Vector3(0f, 0.78f, 0f) * unit, new Vector3(1.4f, 0.07f, 1.4f) * unit, Skin.Dark, layer);

			roof.turret = new GameObject("turret").transform;
			roof.turret.SetParent(r, false);
			roof.turret.gameObject.layer = layer;
			roof.turret.localPosition = new Vector3(0f, 1.45f, 0f) * unit;
			Transform t2 = roof.turret;
			Quaternion alongZ = Quaternion.Euler(90f, 0f, 0f);
			// The yoke, two arms that hold the housing, the housing and its top plate.
			roof.Part(PrimitiveType.Cube, t2, "yoke", new Vector3(0f, -0.4f, -0.1f) * unit, new Vector3(1.3f, 0.35f, 0.9f) * unit, Skin.Dark, layer);
			roof.Part(PrimitiveType.Cube, t2, "arm left", new Vector3(-0.62f, -0.12f, 0f) * unit, new Vector3(0.12f, 0.75f, 0.5f) * unit, Skin.Dark, layer);
			roof.Part(PrimitiveType.Cube, t2, "arm right", new Vector3(0.62f, -0.12f, 0f) * unit, new Vector3(0.12f, 0.75f, 0.5f) * unit, Skin.Dark, layer);
			roof.Part(PrimitiveType.Cube, t2, "housing", Vector3.zero, new Vector3(0.9f, 0.8f, 1.7f) * unit, Skin.Steel, layer);
			roof.Part(PrimitiveType.Cube, t2, "top plate", new Vector3(0f, 0.45f, -0.1f) * unit, new Vector3(0.75f, 0.1f, 1.4f) * unit, Skin.Olive, layer);
			// The ammo box on the right, and a belt of brass links from it up into the housing.
			roof.Part(PrimitiveType.Cube, t2, "ammo box", new Vector3(0.75f, -0.1f, -0.25f) * unit, new Vector3(0.55f, 0.65f, 0.9f) * unit, Skin.Olive, layer);
			roof.Part(PrimitiveType.Cube, t2, "box lid", new Vector3(0.75f, 0.25f, -0.25f) * unit, new Vector3(0.6f, 0.07f, 0.95f) * unit, Skin.Dark, layer);
			for (int i = 0; i < 7; i++)
			{
				float k = i / 6f;
				var link = new Vector3(0.72f - 0.3f * k, 0.28f + 0.2f * Mathf.Sin(k * Mathf.PI * 0.5f), -0.35f + 0.3f * k);
				roof.Part(PrimitiveType.Cube, t2, "link " + i, link * unit, new Vector3(0.14f, 0.1f, 0.16f) * unit, Skin.Brass, layer, Quaternion.Euler(0f, 0f, -30f * (1f - k)));
			}
			// The armoured shield in front of the housing, with a rim and a slit.
			roof.Part(PrimitiveType.Cube, t2, "shield", new Vector3(0f, 0.1f, 0.95f) * unit, new Vector3(1.9f, 1.25f, 0.12f) * unit, Skin.Olive, layer);
			roof.Part(PrimitiveType.Cube, t2, "shield rim", new Vector3(0f, 0.74f, 0.95f) * unit, new Vector3(1.95f, 0.1f, 0.16f) * unit, Skin.Dark, layer);
			roof.Part(PrimitiveType.Cube, t2, "shield side left", new Vector3(-0.97f, 0.1f, 0.95f) * unit, new Vector3(0.1f, 1.25f, 0.16f) * unit, Skin.Dark, layer);
			roof.Part(PrimitiveType.Cube, t2, "shield side right", new Vector3(0.97f, 0.1f, 0.95f) * unit, new Vector3(0.1f, 1.25f, 0.16f) * unit, Skin.Dark, layer);
			// Behind the housing: the motor, and a grip bar with two handles.
			roof.Part(PrimitiveType.Cylinder, t2, "motor", new Vector3(0f, 0f, -1.15f) * unit, new Vector3(0.6f, 0.3f, 0.6f) * unit, Skin.Dark, layer, alongZ);
			roof.Part(PrimitiveType.Cylinder, t2, "motor cap", new Vector3(0f, 0f, -1.48f) * unit, new Vector3(0.45f, 0.04f, 0.45f) * unit, Skin.Steel, layer, alongZ);
			roof.Part(PrimitiveType.Cube, t2, "grip bar", new Vector3(0f, 0.1f, -1.7f) * unit, new Vector3(1.1f, 0.1f, 0.1f) * unit, Skin.Dark, layer);
			roof.Part(PrimitiveType.Cube, t2, "handle left", new Vector3(-0.52f, 0.1f, -1.48f) * unit, new Vector3(0.1f, 0.1f, 0.5f) * unit, Skin.Dark, layer);
			roof.Part(PrimitiveType.Cube, t2, "handle right", new Vector3(0.52f, 0.1f, -1.48f) * unit, new Vector3(0.1f, 0.1f, 0.5f) * unit, Skin.Dark, layer);

			roof.barrels = new GameObject("barrels").transform;
			roof.barrels.SetParent(t2, false);
			roof.barrels.gameObject.layer = layer;
			roof.barrels.localPosition = new Vector3(0f, 0.05f, 1.05f * unit);
			Transform b = roof.barrels;
			for (int i = 0; i < 6; i++)
			{
				float angle = i * 60f * Mathf.Deg2Rad;
				Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.27f * unit;
				roof.Part(PrimitiveType.Cylinder, b, "barrel " + i, offset + new Vector3(0f, 0f, 1.0f * unit), new Vector3(0.15f, 1.0f, 0.15f) * unit, Skin.Dark, layer, alongZ);
			}
			// Rear plate, a heat shroud round the first third, three clamps, a front plate and a muzzle ring.
			roof.Part(PrimitiveType.Cylinder, b, "rear plate", new Vector3(0f, 0f, 0.1f * unit), new Vector3(0.95f, 0.05f, 0.95f) * unit, Skin.Steel, layer, alongZ);
			roof.Part(PrimitiveType.Cylinder, b, "heat shroud", new Vector3(0f, 0f, 0.5f * unit), new Vector3(0.88f, 0.34f, 0.88f) * unit, Skin.Olive, layer, alongZ);
			roof.Part(PrimitiveType.Cylinder, b, "shroud band", new Vector3(0f, 0f, 0.86f * unit), new Vector3(0.92f, 0.03f, 0.92f) * unit, Skin.Dark, layer, alongZ);
			roof.Part(PrimitiveType.Cylinder, b, "clamp 1", new Vector3(0f, 0f, 1.05f * unit), new Vector3(0.78f, 0.05f, 0.78f) * unit, Skin.Steel, layer, alongZ);
			roof.Part(PrimitiveType.Cylinder, b, "clamp 2", new Vector3(0f, 0f, 1.45f * unit), new Vector3(0.74f, 0.05f, 0.74f) * unit, Skin.Steel, layer, alongZ);
			roof.Part(PrimitiveType.Cylinder, b, "front plate", new Vector3(0f, 0f, 1.9f * unit), new Vector3(0.9f, 0.05f, 0.9f) * unit, Skin.Steel, layer, alongZ);
			roof.Part(PrimitiveType.Cylinder, b, "muzzle ring", new Vector3(0f, 0f, 1.98f * unit), new Vector3(0.82f, 0.04f, 0.82f) * unit, Skin.Dark, layer, alongZ);

			// Where the shots start: past the end of the barrels, on the turret (it does not spin with the barrels).
			var muzzle = new GameObject("muzzle").transform;
			muzzle.SetParent(roof.turret, false);
			muzzle.gameObject.layer = layer;
			muzzle.localPosition = new Vector3(0f, 0.05f, (1.05f + 2.05f) * unit);
			roof.Muzzle = muzzle;
			Mod.Log.LogInfo($"Roof gun on '{car.name}': body {min} to {max}, unit {unit:0.00}, layer {layer}");
			return roof;
		}

		private GameObject Part(PrimitiveType shape, Transform parent, string name, Vector3 position, Vector3 scale, Skin skin, int layer, Quaternion? rotation = null)
		{
			GameObject part = GameObject.CreatePrimitive(shape);
			part.name = name;
			part.layer = layer;
			Destroy(part.GetComponent<Collider>());
			part.transform.SetParent(parent, false);
			part.transform.localPosition = position;
			part.transform.localScale = scale;
			part.transform.localRotation = rotation ?? Quaternion.identity;
			var renderer = part.GetComponent<MeshRenderer>();
			renderer.sharedMaterial = materials[skin];
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
			renderer.receiveShadows = true;
			return part;
		}

		// One lit material per surface, made once and kept: the game's lit shader (the first of these that is there), a small
		// point-filtered texture and no shine. The shader is found by name, else taken from the car's biggest mesh if it has a base map.
		private static bool EnsureMaterials(Transform car)
		{
			if (materials.Count == 4 && materials[Skin.Steel] != null)
			{
				return true;
			}
			Shader shader = Shader.Find("Universal Render Pipeline/Lit");
			if (shader == null)
			{
				shader = Shader.Find("Universal Render Pipeline/Simple Lit");
			}
			if (shader == null)
			{
				foreach (Shader candidate in Resources.FindObjectsOfTypeAll<Shader>())
				{
					if (candidate.name == "Universal Render Pipeline/Lit" || candidate.name == "Universal Render Pipeline/Simple Lit")
					{
						shader = candidate;
						break;
					}
				}
			}
			if (shader == null)
			{
				float biggest = 0f;
				foreach (MeshRenderer renderer in car.GetComponentsInChildren<MeshRenderer>(false))
				{
					Material m = renderer.sharedMaterial;
					float volume = renderer.localBounds.size.sqrMagnitude;
					if (m != null && m.shader != null && m.HasProperty("_BaseMap") && volume > biggest)
					{
						biggest = volume;
						shader = m.shader;
					}
				}
			}
			if (shader == null)
			{
				shader = Shader.Find("Sprites/Default");
			}
			if (shader == null)
			{
				return false;
			}
			Mod.Log.LogInfo("Roof gun shader: " + shader.name);
			materials.Clear();
			foreach (Skin skin in new[] { Skin.Steel, Skin.Dark, Skin.Olive, Skin.Brass })
			{
				Texture2D texture = Pixels(skin);
				var material = new Material(shader) { name = "Minigun " + skin, hideFlags = HideFlags.HideAndDontSave };
				material.mainTexture = texture;
				if (material.HasProperty("_BaseMap"))
				{
					material.SetTexture("_BaseMap", texture);
				}
				if (material.HasProperty("_BaseColor"))
				{
					material.SetColor("_BaseColor", Color.white);
				}
				if (material.HasProperty("_Smoothness"))
				{
					material.SetFloat("_Smoothness", 0.12f);
				}
				if (material.HasProperty("_Metallic"))
				{
					material.SetFloat("_Metallic", 0f);
				}
				materials[skin] = material;
			}
			return true;
		}

		// A 16x16 texture in the game's pixel style: a base colour with noise, and the marks of the surface.
		private static Texture2D Pixels(Skin skin)
		{
			const int size = 16;
			var random = new System.Random(1000 + (int)skin);
			Color baseColour;
			float noise;
			switch (skin)
			{
				case Skin.Steel:
					baseColour = new Color32(98, 103, 100, 255);
					noise = 0.05f;
					break;
				case Skin.Dark:
					baseColour = new Color32(40, 42, 45, 255);
					noise = 0.03f;
					break;
				case Skin.Olive:
					baseColour = new Color32(70, 78, 58, 255);
					noise = 0.04f;
					break;
				default:
					baseColour = new Color32(164, 126, 48, 255);
					noise = 0.04f;
					break;
			}
			var pixels = new Color[size * size];
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float n = ((float)random.NextDouble() - 0.5f) * 2f * noise;
					Color c = baseColour + new Color(n, n, n, 0f);
					float roll = (float)random.NextDouble();
					if (skin == Skin.Steel && roll < 0.08f)
					{
						c *= 0.72f;
					}
					else if (skin == Skin.Olive && roll < 0.07f)
					{
						c = new Color32(112, 72, 42, 255);
					}
					else if (skin == Skin.Olive && roll > 0.97f)
					{
						c = new Color32(150, 152, 128, 255);
					}
					else if (skin == Skin.Dark && roll > 0.95f)
					{
						c = new Color32(78, 80, 84, 255);
					}
					else if (skin == Skin.Brass && x % 4 == 0)
					{
						c *= 0.78f;
					}
					c.a = 1f;
					pixels[y * size + x] = c;
				}
			}
			if (skin == Skin.Steel)
			{
				// a couple of bright scratches
				for (int x = 3; x < 11; x++)
				{
					pixels[5 * size + x] = new Color32(150, 155, 152, 255);
				}
				for (int x = 7; x < 14; x++)
				{
					pixels[12 * size + x] = new Color32(138, 142, 140, 255);
				}
			}
			if (skin == Skin.Olive)
			{
				// rivets in the corners
				foreach (int p in new[] { 1 * size + 1, 1 * size + 14, 14 * size + 1, 14 * size + 14 })
				{
					pixels[p] = new Color32(170, 172, 150, 255);
				}
			}
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
			{
				name = "minigun " + skin,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Repeat,
				hideFlags = HideFlags.HideAndDontSave
			};
			texture.SetPixels(pixels);
			texture.Apply(true, true);
			return texture;
		}

		private void LateUpdate()
		{
			if (turret == null || car == null)
			{
				return;
			}
			// Aimed: toward where the remote points. Otherwise: straight ahead of the car.
			Vector3 direction = gun != null && gun.grabbed ? gun.AimForward : car.forward;
			if (direction.sqrMagnitude < 0.001f)
			{
				direction = car.forward;
			}
			Quaternion want = Quaternion.LookRotation(direction.normalized, car.up);
			turret.rotation = Quaternion.RotateTowards(turret.rotation, want, TurnSpeed * Time.deltaTime);
			// The game turns the muzzle to face each shot's target; it goes back to facing along the turret.
			if (Muzzle != null)
			{
				Muzzle.localRotation = Quaternion.identity;
			}
			if (barrels != null && heat != null)
			{
				barrels.Rotate(0f, 0f, SpinSpeed * heat.Spin * Time.deltaTime, Space.Self);
			}
		}

		// The extent of the car's body in the car's own space, and the layer of its biggest mesh.
		internal static bool Measure(Transform car, HashSet<Transform> skip, out Vector3 min, out Vector3 max, out int layer)
		{
			min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
			max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
			layer = car.gameObject.layer;
			float biggest = 0f;
			bool found = false;
			foreach (MeshRenderer renderer in car.GetComponentsInChildren<MeshRenderer>(false))
			{
				if (!renderer.enabled || Inside(renderer.transform, skip))
				{
					continue;
				}
				// The mesh's own box (in the renderer's space), carried through the transforms: the same answer however the car is turned.
				// The renderer's world bounds are an axis-aligned box round the turned mesh, which grows and shrinks as the preview car spins
				// in the garage, and the turret was sized to it.
				Bounds b = renderer.localBounds;
				Vector3 lossy = renderer.transform.lossyScale;
				if (Vector3.Scale(b.size, lossy).sqrMagnitude < 0.05f)
				{
					continue;
				}
				for (int i = 0; i < 8; i++)
				{
					Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
					Vector3 local = car.InverseTransformPoint(renderer.transform.TransformPoint(corner));
					min = Vector3.Min(min, local);
					max = Vector3.Max(max, local);
				}
				float volume = Mathf.Abs(b.size.x * b.size.y * b.size.z * lossy.x * lossy.y * lossy.z);
				if (volume > biggest)
				{
					biggest = volume;
					layer = renderer.gameObject.layer;
				}
				found = true;
			}
			return found;
		}

		private static bool Inside(Transform t, HashSet<Transform> skip)
		{
			for (Transform p = t; p != null; p = p.parent)
			{
				if (skip.Contains(p))
				{
					return true;
				}
			}
			return false;
		}
	}
}
