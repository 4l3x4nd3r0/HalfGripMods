using System.Collections.Generic;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>
	/// Makes the cave feel like a cave: when the player's car is inside it the world's sun and the ambient light fade almost out and a
	/// cool wet mist closes in (the game's weather applies once and never changes), and every car's headlights come on. Leaving it
	/// puts the weather back exactly as it was. The cockpit has its own light and is left alone.
	/// </summary>
	internal sealed class CaveAtmosphere : MonoBehaviour
	{
		/// <summary>Set by the debug tour to force the cave (1) or the outside (0) for a picture.</summary>
		internal static float? Override;
		/// <summary>How deep inside the cave the player is (0 outside), updated every frame.</summary>
		internal static float PlayerDepth;

		private TrackManager track;
		private Light sun;
		private HeadLightsController[] heads = new HeadLightsController[0];
		private readonly Dictionary<HeadLightsController, bool> wasOn = new Dictionary<HeadLightsController, bool>();
		private float amount;
		private bool captured;

		private float sunIntensity;
		private Color sunColor, skyColor, equatorColor, groundColor, fogColor;
		private float ambientIntensity, fogStart, fogEnd;
		private bool fogOn;

		internal void Setup(TrackManager tm, int layer)
		{
			track = tm;
			Override = null;
			WeatherController weather = FindFirstObjectByType<WeatherController>();
			sun = weather != null ? weather.directionalLight : null;
			if (sun == null)
			{
				foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
				{
					if (l.type == LightType.Directional && (sun == null || l.intensity > sun.intensity))
					{
						sun = l;
					}
				}
			}
			heads = FindObjectsByType<HeadLightsController>(FindObjectsSortMode.None);
			foreach (HeadLightsController h in heads)
			{
				wasOn[h] = h.spotlight != null && h.spotlight.activeSelf;
			}
		}

		/// <summary>
		/// Where the player is looking from: the camera that draws the game (the cockpit, the spectator drone, the wreck cam), else the
		/// player's car. False when there is neither (the car is gone after a wreck): then nothing keeps the cave state.
		/// </summary>
		internal static bool Observer(TrackManager tm, out Vector3 pos)
		{
			Camera c = Camera.main;
			if (c != null && c.isActiveAndEnabled)
			{
				pos = c.transform.position;
				return true;
			}
			if (tm != null && tm.playerCarController != null)
			{
				pos = tm.playerCarController.transform.position;
				return true;
			}
			pos = Vector3.zero;
			return false;
		}

		private void LateUpdate()
		{
			if (track == null)
			{
				return;
			}
			Vector3 observer;
			bool have = Observer(track, out observer);
			float target = Override.HasValue ? Override.Value : (have ? Cave.Depth(observer) : 0f);
			PlayerDepth = target;
			amount = Override.HasValue ? target : Mathf.MoveTowards(amount, target, Time.deltaTime * 1.3f);   // the depth already ramps over 100 units; this only smooths a jump (a wreck, the camera cutting)

			if (amount > 0.002f)
			{
				if (!captured)
				{
					Capture();
				}
				float dark = Mathf.SmoothStep(0f, 1f, amount);
				if (sun != null)
				{
					sun.intensity = sunIntensity * Mathf.Lerp(1f, 0.02f, dark);
				}
				float amb = Mathf.Lerp(1f, 0.55f, dark);
				Color tint = new Color(0.62f, 0.82f, 0.92f);
				RenderSettings.ambientSkyColor = Color.Lerp(skyColor, skyColor * amb * tint, dark);
				RenderSettings.ambientEquatorColor = Color.Lerp(equatorColor, equatorColor * amb * tint, dark);
				RenderSettings.ambientGroundColor = Color.Lerp(groundColor, groundColor * amb * tint, dark);
				RenderSettings.ambientIntensity = Mathf.Lerp(ambientIntensity, ambientIntensity * 0.9f, dark);
				RenderSettings.fog = true;
				RenderSettings.fogColor = Color.Lerp(fogColor, new Color(0.03f, 0.05f, 0.06f), dark);
				// the mist closes in late: the hill behind the mouth stays visible for the first tens of units
				float mist = dark * dark;
				RenderSettings.fogStartDistance = Mathf.Lerp(fogStart, 4f, mist);
				RenderSettings.fogEndDistance = Mathf.Lerp(fogEnd, 560f, mist);
			}
			else if (captured)
			{
				Restore();
			}

			foreach (HeadLightsController h in heads)
			{
				if (h == null || h.spotlight == null)
				{
					continue;
				}
				bool inside = Override.HasValue ? Override.Value > 0.5f : Cave.Depth(h.transform.position) > 0.2f;
				h.spotlight.SetActive(inside || wasOn[h]);
			}
		}

		private void Capture()
		{
			captured = true;
			sunIntensity = sun != null ? sun.intensity : 1f;
			sunColor = sun != null ? sun.color : Color.white;
			skyColor = RenderSettings.ambientSkyColor;
			equatorColor = RenderSettings.ambientEquatorColor;
			groundColor = RenderSettings.ambientGroundColor;
			ambientIntensity = RenderSettings.ambientIntensity;
			fogOn = RenderSettings.fog;
			fogColor = RenderSettings.fogColor;
			fogStart = RenderSettings.fogStartDistance;
			fogEnd = RenderSettings.fogEndDistance;
		}

		private void Restore()
		{
			captured = false;
			if (sun != null)
			{
				sun.intensity = sunIntensity;
				sun.color = sunColor;
			}
			RenderSettings.ambientSkyColor = skyColor;
			RenderSettings.ambientEquatorColor = equatorColor;
			RenderSettings.ambientGroundColor = groundColor;
			RenderSettings.ambientIntensity = ambientIntensity;
			RenderSettings.fog = fogOn;
			RenderSettings.fogColor = fogColor;
			RenderSettings.fogStartDistance = fogStart;
			RenderSettings.fogEndDistance = fogEnd;
		}

		private void OnDestroy()
		{
			if (captured)
			{
				Restore();
			}
			Override = null;
		}
	}

	/// <summary>Drops of water that fall from the tips of stalactites near the player and splash out of sight on the floor.</summary>
	internal sealed class Drips : MonoBehaviour
	{
		private sealed class Drop
		{
			public GameObject Go;
			public Vector3 Floor;
			public float Wait;
			public float Speed;
			public bool Falling;
		}

		private readonly List<Drop> drops = new List<Drop>();
		private bool hidden;
		private List<Vector3[]> spots;
		private Transform player;
		private System.Random rng = new System.Random(88);

		internal void Setup(List<Vector3[]> dripSpots, int layer)
		{
			spots = dripSpots;
			Batch shape = new Batch("drop", Tex.White, Color.white, false);
			shape.Cone(Vector3.zero, 0.28f, Vector3.up * 1.3f, 0f, 5, 4f);
			Mesh mesh = shape.ToMesh();
			Material material = Mats.Get(Tex.White, new Color(0.7f, 0.85f, 1f), false, 0.95f, new Color(0.15f, 0.25f, 0.3f));
			for (int i = 0; i < 40; i++)
			{
				GameObject go = new GameObject("drop") { layer = layer };
				go.transform.SetParent(transform, false);
				go.AddComponent<MeshFilter>().sharedMesh = mesh;
				go.AddComponent<MeshRenderer>().sharedMaterial = material;
				go.SetActive(false);
				drops.Add(new Drop { Go = go, Wait = (float)rng.NextDouble() * 3f });
			}
		}

		private void Update()
		{
			if (player == null)
			{
				CarControllerNew car = FindFirstObjectByType<CarControllerNew>();
				if (car == null)
				{
					return;
				}
				player = car.transform;
			}
			if (CaveAtmosphere.PlayerDepth < 0.05f && !CaveAtmosphere.Override.HasValue)
			{
				// outside the cave: put the drops away, or they hang in the air where they were, visible through the mouths
				if (!hidden)
				{
					hidden = true;
					foreach (Drop h in drops)
					{
						h.Falling = false;
						h.Wait = (float)rng.NextDouble() * 3f;
						h.Go.SetActive(false);
					}
				}
				return;
			}
			hidden = false;
			float dt = Time.deltaTime;
			foreach (Drop d in drops)
			{
				if (!d.Falling)
				{
					d.Wait -= dt;
					if (d.Wait > 0f)
					{
						continue;
					}
					Vector3[] pick = null;
					for (int tries = 0; tries < 8; tries++)
					{
						Vector3[] s = spots[rng.Next(spots.Count)];
						if ((s[0] - player.position).sqrMagnitude < 140f * 140f)
						{
							pick = s;
							break;
						}
					}
					if (pick == null)
					{
						d.Wait = 1f;
						continue;
					}
					d.Go.transform.position = pick[0];
					d.Floor = pick[1];
					d.Speed = 0f;
					d.Falling = true;
					d.Go.SetActive(true);
				}
				else
				{
					d.Speed += 38f * dt;
					Vector3 pos = d.Go.transform.position;
					pos.y -= d.Speed * dt;
					d.Go.transform.position = pos;
					if (pos.y <= d.Floor.y + 0.2f)
					{
						d.Falling = false;
						d.Go.SetActive(false);
						d.Wait = 0.4f + (float)rng.NextDouble() * 3.5f;
					}
				}
			}
		}
	}
}
