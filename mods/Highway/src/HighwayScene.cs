using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalfGripHighway
{
	/// <summary>
	/// Turns the Stadium scene into Highway when a Highway race loads. Runs on <c>sceneLoaded</c>: after every Awake and before any Start,
	/// so the lap counter, the grid, the hazards and the reset logic all find the new track.
	/// </summary>
	internal static class HighwayScene
	{
		/// <summary>Heights count this many times over when finding the nearest point of the track.</summary>
		private const float HeightWeight = 4f;

		/// <summary>The track line of the Highway race that is loaded, or null.</summary>
		private static PathManager path;
		private static KDTree weighted;

		private static GameObject root;
		private static readonly List<GameObject> hidden = new List<GameObject>();
		private static Collider rainPlane;

		// ---- the nearest-point patch -------------------------------------------------------------------------------

		internal static bool TryClosest(PathManager from, Vector3 position, out int index)
		{
			index = 0;
			if (weighted == null || from != path)
			{
				return false;
			}
			index = weighted.FindClosestPointIndex(new Vector3(position.x, position.y * HeightWeight, position.z));
			return index >= 0;
		}

		// ---- the dirt shortcuts ------------------------------------------------------------------------------------

		private static readonly List<List<Vector3>> dirtLines = new List<List<Vector3>>();

		/// <summary>The car whose track position the lap manager is measuring right now (set by <see cref="MeasureScope"/>).</summary>
		internal static GameObject MeasuredCar;

		internal static bool Loaded => path != null && dirtLines.Count > 0;

		/// <summary>On a dirt shortcut: <paramref name="along"/> is the direction the race takes it (the lines are built in race order).</summary>
		internal static bool OnDirt(Vector3 p, out Vector3 along)
		{
			along = Vector3.forward;
			float best = (Plan.RaceHalf + 4f) * (Plan.RaceHalf + 4f);
			bool found = false;
			foreach (List<Vector3> line in dirtLines)
			{
				for (int i = 0; i + 1 < line.Count; i++)
				{
					Vector3 a = line[i];
					Vector3 b = line[i + 1];
					float abx = b.x - a.x;
					float abz = b.z - a.z;
					float t = Mathf.Clamp01(((p.x - a.x) * abx + (p.z - a.z) * abz) / Mathf.Max(1e-4f, abx * abx + abz * abz));
					float dx = a.x + abx * t - p.x;
					float dz = a.z + abz * t - p.z;
					float d = dx * dx + dz * dz;
					if (d < best && Mathf.Abs(Mathf.Lerp(a.y, b.y, t) - p.y) < 4f)
					{
						best = d;
						along = new Vector3(abx, 0f, abz).normalized;
						found = true;
					}
				}
			}
			return found;
		}

		// ---- load / unload -----------------------------------------------------------------------------------------

		internal static void OnSceneLoaded(Scene scene)
		{
			path = null;
			weighted = null;
			dirtLines.Clear();
			MeasuredCar = null;
			root = null;
			hidden.Clear();
			rainPlane = null;

			if (Mod.Map == null || RunState.ActiveMap != Mod.Map)
			{
				return;
			}
			TrackManager tm = UnityEngine.Object.FindFirstObjectByType<TrackManager>();
			if (tm == null || tm.playerCarController == null || tm.lapManager == null || tm.lapManager.trackLine == null)
			{
				return;
			}
			try
			{
				Build(scene, tm);
			}
			catch (Exception e)
			{
				Mod.Log.LogError("Highway could not be built: " + e);
			}
		}

		internal static void Unload()
		{
			if (root != null)
			{
				UnityEngine.Object.Destroy(root);
			}
			foreach (GameObject go in hidden)
			{
				if (go != null)
				{
					go.SetActive(true);
				}
			}
			if (rainPlane != null)
			{
				rainPlane.enabled = true;
			}
			hidden.Clear();
			path = null;
			weighted = null;
			dirtLines.Clear();
			MeasuredCar = null;
			root = null;
		}

		// ---- building ----------------------------------------------------------------------------------------------

		private static void Build(Scene scene, TrackManager tm)
		{
			GameObject env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "ENVIROMENT");
			if (env == null)
			{
				throw new InvalidOperationException("this scene has no ENVIROMENT object, so it is not the Stadium scene");
			}

			Transform ground = env.transform.Find("Ground");
			int layer = ground != null ? ground.gameObject.layer : 0;
			Material source = ground != null && ground.TryGetComponent(out MeshRenderer groundRenderer) ? groundRenderer.sharedMaterial : null;

			foreach (string name in new[] { "Ground", "Track", "Decoration/Outside", "Decoration/Bridge", "Decoration/TowerDamaged" })
			{
				Transform t = env.transform.Find(name);
				if (t != null && t.gameObject.activeSelf)
				{
					t.gameObject.SetActive(false);
					hidden.Add(t.gameObject);
				}
			}
			Transform rain = env.transform.Find("Weather/ParticleColliderPlane");
			if (rain != null && rain.TryGetComponent(out rainPlane))
			{
				rainPlane.enabled = false;
			}

			Route route = Route.Build();
			dirtLines.Clear();
			dirtLines.Add(route.DirtEast);
			dirtLines.Add(route.DirtNorth);
			RoadBuilder builder = new RoadBuilder { Gaps = Mod.MedianGaps.Value, GapLength = Mod.MedianGapLength.Value };
			builder.Build(route);

			root = new GameObject("HighwayMap");
			int meshes = 0;
			int triangles = 0;
			foreach (Batch batch in builder.All)
			{
				if (batch.Count == 0)
				{
					continue;
				}
				GameObject go = new GameObject(batch.Name) { layer = layer };
				go.transform.SetParent(root.transform, false);
				Mesh mesh = batch.ToMesh();
				go.AddComponent<MeshFilter>().sharedMesh = mesh;
				go.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(source, batch.Color);
				if (batch.Collide)
				{
					go.AddComponent<MeshCollider>().sharedMesh = mesh;
				}
				if (batch.Wall)
				{
					try
					{
						go.tag = "Wall";
					}
					catch (Exception)
					{
						Mod.Log.LogWarning("The tag Wall does not exist, barriers keep the default tag.");
					}
				}
				if (batch.Name == "slope" || batch.Name == "field")
				{
					HandlingModifier handling = go.AddComponent<HandlingModifier>();
					handling.gripModifier = 0.7f;
					handling.resistanceModifier = 3f;
					handling.background = true;
				}
				else if (batch.Name == "dirt" || batch.Name == "dirt slope")
				{
					// The shortcuts are loose dirt. A tyre's weather handling is applied to a surface's grip, so the player's build decides what this costs.
					HandlingModifier handling = go.AddComponent<HandlingModifier>();
					handling.gripModifier = Mod.DirtGrip.Value;
					handling.resistanceModifier = Mod.DirtDrag.Value;
					handling.background = true;
				}
				meshes++;
				triangles += mesh.triangles.Length / 3;
			}
			Mod.Log.LogInfo("Highway built: " + meshes + " meshes, " + triangles + " triangles, lap " + route.Length.ToString("0") + " m");

			TreeSource.Capture();
			int props = BuildProps(builder.Props, layer, source);
			int containers = Containers.Build(builder.Containers, layer, source, root.transform);
			int trees = BuildTrees(builder.Trees, layer, source);
			Mod.Log.LogInfo("Highway containers (from the Factory): " + containers);
			Mod.Log.LogInfo("Highway extras: " + props + " breakable props (signs and drums), " + trees + " trees from " + TreeSource.Models.Count + " forest models");

			BuildLabels(builder.Labels);

			Rewire(tm, route);
			PlaceGrid(tm, env, route);
			SpreadHazards(route);
			MeasureCar(tm, out Vector3 carCentre, out float carHalfWidth, out float carHalfLength);
			GameObject field = new GameObject("BreakField") { layer = layer };
			field.transform.SetParent(root.transform, false);
			field.AddComponent<BreakField>().Setup(builder.Rails, breakables, PropMaterial(source, new Color(0.66f, 0.68f, 0.72f)), PropMaterial(source, new Color(0.36f, 0.37f, 0.40f)), tm, carCentre, carHalfWidth, carHalfLength, layer);
			Mod.Log.LogInfo("Highway breakables: " + builder.Rails.Count + " guardrail pieces (merged meshes) and " + breakables.Count + " signs and drums, no physics bodies; tested against the cars every physics step");
		}

		/// <summary>Text on the exit gantries.</summary>
		private static void BuildLabels(List<Label> labels)
		{
			if (font == null)
			{
				return;
			}
			foreach (Label l in labels)
			{
				GameObject go = new GameObject("GantryText");
				go.transform.SetParent(root.transform, false);
				go.transform.SetPositionAndRotation(l.Pos, Quaternion.LookRotation(-l.Facing, Vector3.up));
				TextMeshPro text = go.AddComponent<TextMeshPro>();
				text.font = font;
				text.text = l.Text;
				text.color = l.Color;
				text.alignment = TextAlignmentOptions.Center;
				text.rectTransform.sizeDelta = l.Size;
				text.enableAutoSizing = true;
				text.fontSizeMin = 1f;
				text.fontSizeMax = 200f;
				text.overflowMode = TextOverflowModes.Overflow;
			}
		}

		/// <summary>The player car's footprint: the centre of its body in its own space, and half its width and length. Logged to check the lanes.</summary>
		private static void MeasureCar(TrackManager tm, out Vector3 centre, out float halfWidth, out float halfLength)
		{
			centre = Vector3.zero;
			halfWidth = 2.65f;
			halfLength = 7.1f;
			try
			{
				Bounds bounds = default(Bounds);
				bool any = false;
				int interior = LayerMask.NameToLayer("carInterior");
				foreach (MeshRenderer r in tm.playerCarController.GetComponentsInChildren<MeshRenderer>())
				{
					if (r.gameObject.layer == interior)
					{
						continue;
					}
					if (!any)
					{
						bounds = r.bounds;
						any = true;
					}
					else
					{
						bounds.Encapsulate(r.bounds);
					}
				}
				if (any)
				{
					// The car is placed facing +X, so its length is along x.
					centre = tm.playerCarController.transform.InverseTransformPoint(bounds.center);
					halfLength = bounds.size.x * 0.5f;
					halfWidth = bounds.size.z * 0.5f;
					Mod.Log.LogInfo("Player car footprint: " + bounds.size.x.ToString("0.0") + " m long, " + bounds.size.z.ToString("0.0") + " m wide; a lane is " + Plan.Lane.ToString("0.0") + " m");
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not measure the car: " + e.Message);
			}
		}

		private static readonly Dictionary<string, Mesh[]> propMeshes = new Dictionary<string, Mesh[]>();
		private static readonly Dictionary<string, Color[]> propColors = new Dictionary<string, Color[]>();
		private static readonly Dictionary<Color, Material> propMaterials = new Dictionary<Color, Material>();
		private static TMP_FontAsset font;

		private static Material PropMaterial(Material source, Color color)
		{
			if (!propMaterials.TryGetValue(color, out Material m) || m == null)
			{
				m = MakeMaterial(source, color);
				propMaterials[color] = m;
			}
			return m;
		}

		private static readonly List<BreakProp> breakables = new List<BreakProp>();

		/// <summary>
		/// Signs, drums and the like: ordinary objects (meshes shared per kind, sign text as TextMeshPro) with no physics at all. They are
		/// registered with the <see cref="BreakField"/>, which breaks one when a car reaches it.
		/// </summary>
		private static int BuildProps(List<Prop> props, int layer, Material source)
		{
			propMeshes.Clear();
			propColors.Clear();
			propMaterials.Clear();
			breakables.Clear();
			font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name.Contains("m6x11"))
				?? Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
			if (font == null)
			{
				Mod.Log.LogWarning("No TextMeshPro font found, the signs stay blank.");
			}
			int made = 0;
			foreach (Prop prop in props)
			{
				if (!propMeshes.ContainsKey(prop.Kind))
				{
					Dictionary<Color, Batch> batches = new Dictionary<Color, Batch>();
					foreach (PropPart part in prop.Parts)
					{
						if (!batches.TryGetValue(part.Color, out Batch batch))
						{
							batch = new Batch(prop.Kind, part.Color, false);
							batches[part.Color] = batch;
						}
						if (part.Round)
						{
							batch.Disc(part.Center, part.Size.x * 0.5f, part.Size.z, part.Rot);
						}
						else
						{
							batch.Box(part.Center, part.Size, part.Rot);
						}
					}
					propMeshes[prop.Kind] = batches.Values.Select(b => b.ToMesh()).ToArray();
					propColors[prop.Kind] = batches.Keys.ToArray();
				}

				GameObject go = new GameObject(prop.Kind) { layer = layer };
				go.transform.SetParent(root.transform, false);
				go.transform.SetPositionAndRotation(prop.Pos, prop.Rot);
				Mesh[] meshes = propMeshes[prop.Kind];
				Color[] colors = propColors[prop.Kind];
				for (int i = 0; i < meshes.Length; i++)
				{
					GameObject part = new GameObject("part") { layer = layer };
					part.transform.SetParent(go.transform, false);
					part.AddComponent<MeshFilter>().sharedMesh = meshes[i];
					part.AddComponent<MeshRenderer>().sharedMaterial = PropMaterial(source, colors[i]);
				}
				if (font != null)
				{
					foreach (PropText t in prop.Texts)
					{
						AddText(go.transform, t);
					}
				}

				// What a car can run into: the parts whose underside is below the height of a car.
				List<Vector3> centres = new List<Vector3>();
				List<float> radii = new List<float>();
				foreach (PropHit hit in prop.Hits)
				{
					if (hit.Center.y - hit.Size.y * 0.5f < 2.2f)
					{
						centres.Add(prop.Pos + prop.Rot * hit.Center);
						radii.Add(Mathf.Max(hit.Size.x, hit.Size.z) * 0.5f);
					}
				}
				if (centres.Count > 0)
				{
					breakables.Add(new BreakProp
					{
						Go = go,
						Centres = centres.ToArray(),
						Radii = radii.ToArray(),
						Mass = prop.Mass,
						Drag = prop.Kind == "cone" ? 0f : Mod.SignSpeedLoss.Value,
						Shards = prop.Parts.Select(part => new Shard
						{
							Material = PropMaterial(source, part.Color),
							Center = part.Center,
							Size = part.Size,
							Rot = part.Rot
						}).ToArray()
					});
				}
				made++;
			}
			return made;
		}

		private static void AddText(Transform parent, PropText t)
		{
			GameObject go = new GameObject("Text");
			go.transform.SetParent(parent, false);
			go.transform.localPosition = t.Center;
			// Text reads from its -Z side, so it looks away from the side it should be read from.
			go.transform.localRotation = t.Side > 0 ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
			TextMeshPro text = go.AddComponent<TextMeshPro>();
			text.font = font;
			text.text = t.Text;
			text.color = t.Color;
			text.alignment = TextAlignmentOptions.Center;
			text.rectTransform.sizeDelta = t.Size;
			text.enableAutoSizing = true;
			text.fontSizeMin = 1f;
			text.fontSizeMax = 60f;
			text.overflowMode = TextOverflowModes.Overflow;
		}

		/// <summary>The Forest map's own tree models; plain stand-ins if the game's could not be captured.</summary>
		private static int BuildTrees(List<TreeSpot> spots, int layer, Material source)
		{
			GameObject holder = new GameObject("Trees");
			holder.transform.SetParent(root.transform, false);
			List<TreeSource.Model> models = TreeSource.Models;
			if (models.Count > 0)
			{
				foreach (TreeSpot spot in spots)
				{
					TreeSource.Model model = models[spot.Pick % models.Count];
					float k = spot.Height / model.Height;
					GameObject tree = new GameObject(model.Name) { layer = layer };
					tree.transform.SetParent(holder.transform, false);
					tree.transform.SetPositionAndRotation(spot.Pos, Quaternion.Euler(0f, spot.Yaw, 0f));
					tree.transform.localScale = Vector3.one * k;
					foreach (TreeSource.Part part in model.Parts)
					{
						GameObject child = new GameObject(part.Mesh.name) { layer = layer };
						child.transform.SetParent(tree.transform, false);
						child.transform.localPosition = part.Pos;
						child.transform.localRotation = part.Rot;
						child.transform.localScale = part.Scale;
						child.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
						child.AddComponent<MeshRenderer>().sharedMaterials = part.Materials;
					}
				}
				return spots.Count;
			}

			Mod.Log.LogWarning("The forest tree models were not found (they load with the main menu), so plain stand-in trees are used.");
			Batch trunk = new Batch("trunk", new Color(0.35f, 0.24f, 0.14f), false);
			Batch crown = new Batch("crown", new Color(0.12f, 0.34f, 0.16f), false);
			foreach (TreeSpot spot in spots)
			{
				float h = spot.Height;
				trunk.Cylinder(spot.Pos, 0.5f, h * 0.3f, 6);
				for (int i = 0; i < 3; i++)
				{
					float w = h * (0.34f - i * 0.1f);
					crown.Box(spot.Pos + Vector3.up * (h * (0.3f + i * 0.25f) + w * 0.5f), new Vector3(w, w, w), Quaternion.Euler(0f, spot.Yaw + i * 30f, 0f));
				}
			}
			foreach (Batch batch in new[] { trunk, crown })
			{
				GameObject go = new GameObject(batch.Name) { layer = layer };
				go.transform.SetParent(holder.transform, false);
				go.AddComponent<MeshFilter>().sharedMesh = batch.ToMesh();
				go.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(source, batch.Color);
			}
			return spots.Count;
		}

		/// <summary>The game's Stadium material gives the shader (so lighting and shadows work); colour comes from the batch.</summary>
		private static Material MakeMaterial(Material source, Color color)
		{
			Shader shader = source != null ? source.shader : null;
			Material m = shader != null ? new Material(shader) : null;
			if (m == null || (!m.HasProperty("_BaseColor") && !m.HasProperty("_Color")))
			{
				shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
				m = new Material(shader);
			}
			if (m.HasProperty("_BaseColor"))
			{
				m.SetColor("_BaseColor", color);
			}
			if (m.HasProperty("_Color"))
			{
				m.SetColor("_Color", color);
			}
			if (m.HasProperty("_Smoothness"))
			{
				m.SetFloat("_Smoothness", 0.05f);
			}
			if (m.HasProperty("_Metallic"))
			{
				m.SetFloat("_Metallic", 0f);
			}
			m.hideFlags = HideFlags.HideAndDontSave;
			return m;
		}

		/// <summary>Replaces Stadium's track line with the Highway lap, and tells the lap counter, the AI and the scatter about it.</summary>
		private static void Rewire(TrackManager tm, Route route)
		{
			LapManager lap = tm.lapManager;
			PathManager line = lap.trackLine;

			foreach (Transform child in line.transform.Cast<Transform>().ToList())
			{
				child.SetParent(null);
				UnityEngine.Object.Destroy(child.gameObject);
			}
			List<Vector3> control = route.ControlPoints(160f, 30f);
			for (int i = 0; i < control.Count; i++)
			{
				GameObject point = new GameObject("HW" + (i + 1));
				point.transform.SetParent(line.transform, true);
				point.transform.position = control[i];
				Route.Widths(control[i], out float rightHalf, out float leftHalf);
				point.transform.localScale = new Vector3(rightHalf, 1f, leftHalf);
				point.AddComponent<PathControlPointEditorController>();
			}

			AccessTools.Method(typeof(PathManager), "InitializePath").Invoke(line, null);
			Traverse.Create(line).Field("snapLanesToGround").SetValue(false);
			line.BakeRacingLine();

			weighted = new KDTree(line.sampledPoints.ConvertAll(p => new Vector3(p.x, p.y * HeightWeight, p.z)));
			path = line;
			if (Mod.ShowPath.Value && line.TryGetComponent(out LineRenderer renderer))
			{
				renderer.enabled = true;
			}

			// The other track lines belong to Stadium.
			lap.enemyTrackLines = new List<PathManager> { line };
			foreach (PathManager other in UnityEngine.Object.FindObjectsByType<PathManager>(FindObjectsSortMode.None))
			{
				if (other != line)
				{
					other.gameObject.SetActive(false);
					hidden.Add(other.gameObject);
				}
			}

			tm.allowFigureEightTracks = true;
			// A reset looks down from above the line for the road. Under the bridge, 200 m above would find the deck.
			tm.resetRayHeight = 4f;
			tm.resetRayLength = 30f;

			TrackScatter scatter = UnityEngine.Object.FindFirstObjectByType<TrackScatter>();
			if (scatter != null)
			{
				scatter.path = line;
			}

			Mod.Log.LogInfo("Highway track line: " + control.Count + " control points, " + line.sampledPoints.Count + " samples, " + line.trackLength.ToString("0") + " m, " + line.lanes.Count + " lanes");
		}

		/// <summary>Six slots on the two racing lanes of the bridge highway, behind the start line, facing east.</summary>
		private static void PlaceGrid(TrackManager tm, GameObject env, Route route)
		{
			Transform container = tm.startingPositionsContainer.transform;
			Transform playerSlot = container.Cast<Transform>().FirstOrDefault(t => t.name == "PlayerPosition");
			CarControllerNew car = tm.playerCarController;
			float lift = playerSlot != null ? car.transform.position.y - playerSlot.position.y : 0.5f;

			float lane1 = -(Plan.MedianHalf + Plan.Inner + Plan.Lane * 0.5f);
			float lane2 = lane1 - Plan.Lane;
			int n = 0;
			foreach (Transform slot in container.Cast<Transform>().ToList())
			{
				if (slot == playerSlot)
				{
					continue;
				}
				if (n >= 5)
				{
					slot.gameObject.SetActive(false);
					continue;
				}
				slot.position = new Vector3(Route.StartX - 30f * (n + 1), 0f, n % 2 == 0 ? lane1 : lane2);
				n++;
			}
			Vector3 playerAt = new Vector3(Route.StartX - 30f * 6f, 0f, lane2);
			if (playerSlot != null)
			{
				playerSlot.position = playerAt;
			}

			Transform ct = car.transform;
			AudioListener listener = car.GetComponentInChildren<AudioListener>(true);
			bool listenerOn = listener != null && listener.enabled;
			if (listenerOn)
			{
				listener.enabled = false;
			}
			Vector3 target = playerAt + Vector3.up * lift;
			ct.position = target;
			Rigidbody body = car.rb != null ? car.rb : car.GetComponent<Rigidbody>();
			if (body != null)
			{
				body.position = target;
				body.linearVelocity = Vector3.zero;
				body.angularVelocity = Vector3.zero;
			}
			if (listenerOn)
			{
				listener.enabled = true;
			}
			Physics.SyncTransforms();

			Transform semaphore = env.transform.Find("RaceStarterSemaphore");
			if (semaphore != null)
			{
				semaphore.position = new Vector3(Route.StartX - 22f, semaphore.position.y, lane1 - Plan.Lane * 0.5f);
			}
		}

		/// <summary>Chicken and trap spots all round the lap, away from the grid.</summary>
		private static void SpreadHazards(Route route)
		{
			HazardsSpawner_FINAL spawner = UnityEngine.Object.FindFirstObjectByType<HazardsSpawner_FINAL>();
			if (spawner == null)
			{
				return;
			}
			System.Random rng = new System.Random(77);
			List<GameObject> spots = new List<GameObject>();
			int count = 60;
			for (int i = 0; i < count; i++)
			{
				float s = 400f + (route.Cum[route.Cum.Length - 1] - 900f) * i / (count - 1);
				Geo.PointAt(route.Dense, route.Cum, s, out Vector3 pos, out Vector3 right);
				GameObject spot = new GameObject("HazardSpot" + i);
				spot.transform.SetParent(root.transform, false);
				spot.transform.position = pos + right * (float)(rng.NextDouble() * 12.0 - 6.0);
				spots.Add(spot);
			}
			Traverse.Create(spawner).Field("spawnPositions").SetValue(spots);
		}
	}
}
