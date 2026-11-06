using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TempleOfDrift
{
	/// <summary>A place where nothing else (trees) may stand.</summary>
	internal sealed class Keepout
	{
		public Vector2 C;
		public float R;
	}

	/// <summary>A line of text on a sign (TextMeshPro, the game's pixel font).</summary>
	internal sealed class Label
	{
		public Vector3 Pos;
		public Vector3 Facing;
		public string Text;
		public Color Color;
		public Vector2 Size;
	}

	/// <summary>
	/// Turns the Stadium scene into Temple of Drift when one of its races loads. Runs on <c>sceneLoaded</c>: after every Awake and before any
	/// Start, so the lap counter, the grid, the drops and the reset logic all find the new track.
	/// </summary>
	internal static class SceneBuilder
	{
		private static GameObject root;
		private static readonly List<GameObject> hidden = new List<GameObject>();
		private static Collider rainPlane;

		private static PhysicsMaterial frictionless;

		/// <summary>No friction and no bounce: a car touching it slides along instead of catching.</summary>
		private static PhysicsMaterial Frictionless
		{
			get
			{
				if (frictionless == null)
				{
					frictionless = new PhysicsMaterial("ToDFrictionless")
					{
						dynamicFriction = 0f,
						staticFriction = 0f,
						bounciness = 0f,
						frictionCombine = PhysicsMaterialCombine.Minimum,
						bounceCombine = PhysicsMaterialCombine.Minimum
					};
				}
				return frictionless;
			}
		}
		private static TMP_FontAsset font;

		internal static bool Active
		{
			get { return root != null; }
		}

		internal static void OnSceneLoaded(Scene scene)
		{
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
				Mod.Log.LogError("Temple of Drift could not be built: " + e);
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
			root = null;
		}

		// ---- building -----------------------------------------------------------------------------------------------

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
			Mats.Reset(source);

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

			root = new GameObject("TempleOfDriftMap");
			PerfCuller.Reset();
			int triangles = 0;

			// The portals: the terrain stays open where a portal block stands.
			List<HoleRect> holes = Cave.Holes();
			int chunks = Mountain.Build(root.transform, layer, holes, out int terrainTris);
			triangles += terrainTris;

			// everything below the mountain's rim
			Batch far = new Batch("far ground", Tex.Grass, new Color(0.8f, 0.8f, 0.8f), false);
			float low = Data.Heights.Min() - 2f;
			far.Quad(new Vector3(-9000f, low, -9000f), new Vector3(-9000f, low, 9000f), new Vector3(9000f, low, 9000f), new Vector3(9000f, low, -9000f), Vector3.up, 60f);

			List<Batch> batches = new List<Batch> { far };
			Roads roads = new Roads();
			roads.Build();
			batches.AddRange(roads.All);

			List<Keepout> keepouts = new List<Keepout>();
			List<Label> labels = new List<Label>();
			Landmarks.Build(batches, keepouts, labels);
			Cave.Signs.Clear();
			int lights = Cave.Build(batches, root.transform, layer, source);
			labels.AddRange(Cave.Signs);

			TreeInstancer.Reset();
			TreeInstancer.PrepareSakura();
			int trees = Forest.Build(batches, keepouts, Mod.TreeCount.Value);
			TreeInstancer.Create(root.transform, layer);
			Forest.BuildSolids(root.transform, layer);

			int meshes = 0;
			foreach (Batch batch in batches)
			{
				if (batch.Count == 0)
				{
					continue;
				}
				triangles += Spawn(batch, root.transform, layer);
				meshes++;
			}
			Mod.Log.LogInfo("Temple of Drift built: " + chunks + " terrain chunks, " + meshes + " meshes, " + triangles + " triangles, " + lights + " cave lights, lap " + Data.Lap.ToString("0") + " units");

			BuildLabels(labels, layer);
			Mod.Log.LogInfo("Temple of Drift trees: " + trees);

			Rewire(tm);
			PlaceGrid(tm, env);
			SpreadHazards();

			// the guardrail: breaks when a car touches it, no physics bodies (as on Highway)
			MeasureCar(tm, out Vector3 carCentre, out float carHalfWidth, out float carHalfLength);
			GameObject field = new GameObject("BreakField") { layer = layer };
			field.transform.SetParent(root.transform, false);
			field.AddComponent<BreakField>().Setup(roads.Rails, tm, carCentre, carHalfWidth, carHalfLength, layer);

			// inside the cave it gets dark and damp; water drips from the stalactites
			GameObject atmosphere = new GameObject("CaveAtmosphere");
			atmosphere.transform.SetParent(root.transform, false);
			atmosphere.AddComponent<CaveAtmosphere>().Setup(tm, layer);
			Cave.StartDrips(root.transform, layer);
			new GameObject("PerfCuller").transform.SetParent(root.transform, false);
			root.transform.Find("PerfCuller").gameObject.AddComponent<PerfCuller>().Setup(tm);
		}

		/// <summary>Makes the object, renderer, collider and surface behaviour of a batch. Returns its triangle count.</summary>
		private static int Spawn(Batch batch, Transform parent, int layer)
		{
			GameObject go = new GameObject(batch.Name) { layer = layer };
			go.transform.SetParent(parent, false);
			Mesh mesh = batch.ToMesh();
			if (!batch.Invisible)
			{
				go.AddComponent<MeshFilter>().sharedMesh = mesh;
				MeshRenderer mr = go.AddComponent<MeshRenderer>();
				mr.sharedMaterial = Mats.Get(batch.Tex, batch.Tint, batch.TwoSided, batch.Smooth, batch.Emission);
				mr.shadowCastingMode = batch.CastShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
				mr.receiveShadows = !PerfCuller.IsCaveName(batch.Name);
				mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
				mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
				bool caveBatch = PerfCuller.IsCaveName(batch.Name);
				PerfCuller.Register(mr, caveBatch, caveBatch && batch.Name.Length > 0 && char.IsDigit(batch.Name[batch.Name.Length - 1]));
			}
			if (batch.Collide)
			{
				MeshCollider mc = go.AddComponent<MeshCollider>();
				mc.sharedMesh = mesh;
				if (batch.Slippery)
				{
					mc.sharedMaterial = Frictionless;
				}
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
			if (batch.Handling == "dirt")
			{
				HandlingModifier handling = go.AddComponent<HandlingModifier>();
				handling.gripModifier = Mod.DirtGrip.Value;
				handling.resistanceModifier = Mod.DirtDrag.Value;
				handling.background = true;
			}
			else if (batch.Handling == "paint")
			{
				// the red and white paint of the hairpins is slick: brake on it and the tail steps out
				HandlingModifier handling = go.AddComponent<HandlingModifier>();
				handling.gripModifier = Mod.PaintGrip.Value;
				handling.resistanceModifier = 1f;
				handling.background = false;
			}
			else if (batch.Handling == "slick")
			{
				// the tarmac of the hairpins: a little less grip, so a brake flick swings the tail
				HandlingModifier handling = go.AddComponent<HandlingModifier>();
				handling.gripModifier = Mod.HairpinGrip.Value;
				handling.resistanceModifier = 1f;
				handling.background = false;
			}
			else if (batch.Handling == "touge")
			{
				// all the mountain tarmac is a little slippery; the surface grip scales the tyre's own grip
				HandlingModifier handling = go.AddComponent<HandlingModifier>();
				handling.gripModifier = Mod.TougeGrip.Value;
				handling.resistanceModifier = 1f;
				handling.background = false;
			}
			else if (batch.Handling == "gravel")
			{
				HandlingModifier handling = go.AddComponent<HandlingModifier>();
				handling.gripModifier = Mod.GravelGrip.Value;
				handling.resistanceModifier = Mod.GravelDrag.Value;
				handling.background = true;
			}
			else if (batch.Handling == "rough")
			{
				// the short way through the cave: broken rock, loose and slow
				HandlingModifier handling = go.AddComponent<HandlingModifier>();
				handling.gripModifier = Mod.ShortWayGrip.Value;
				handling.resistanceModifier = Mod.ShortWayDrag.Value;
				handling.background = true;
			}
			else if (batch.Handling == "grass")
			{
				HandlingModifier handling = go.AddComponent<HandlingModifier>();
				handling.gripModifier = 0.7f;
				handling.resistanceModifier = 3f;
				handling.background = true;
			}
			return mesh.triangles.Length / 3;
		}

		private static void BuildLabels(List<Label> labels, int layer)
		{
			if (labels.Count == 0)
			{
				return;
			}
			font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name.Contains("m6x11"))
				?? Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
			if (font == null)
			{
				Mod.Log.LogWarning("No TextMeshPro font found, the signs stay blank.");
				return;
			}
			foreach (Label l in labels)
			{
				GameObject go = new GameObject("Sign text") { layer = layer };
				go.transform.SetParent(root.transform, false);
				// TextMeshPro reads from its -Z side, so it looks away from the side it is read from.
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

		/// <summary>The player car's footprint: the centre of its body in its own space, and half its width and length (as Highway measures it).</summary>
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
					// the car is placed facing +X, so its length is along x
					centre = tm.playerCarController.transform.InverseTransformPoint(bounds.center);
					halfLength = bounds.size.x * 0.5f;
					halfWidth = bounds.size.z * 0.5f;
					Mod.Log.LogInfo("Player car footprint: " + bounds.size.x.ToString("0.0") + " long, " + bounds.size.z.ToString("0.0") + " wide");
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not measure the car: " + e.Message);
			}
		}

		// ---- the track line, the grid, the hazards ------------------------------------------------------------------------

		/// <summary>Replaces Stadium's track line with the lap and tells the lap counter, the AI and the drops about it.</summary>
		private static void Rewire(TrackManager tm)
		{
			LapManager lap = tm.lapManager;
			PathManager line = lap.trackLine;
			foreach (Transform child in line.transform.Cast<Transform>().ToList())
			{
				child.SetParent(null);
				UnityEngine.Object.Destroy(child.gameObject);
			}

			// A control point every 45 units on a straight and every 20 degrees in a bend.
			List<Pt> p = Data.Main;
			List<int> picks = new List<int> { 0 };
			float since = 0f;
			Vector3 lastFwd = Data.Forward(p, 0, true);
			for (int i = 1; i < p.Count; i++)
			{
				since += (p[i].P - p[i - 1].P).magnitude;
				Vector3 f = Data.Forward(p, i, true);
				float turned = Vector3.Angle(lastFwd, f);
				if (since >= 45f || turned >= 20f)
				{
					picks.Add(i);
					since = 0f;
					lastFwd = f;
				}
			}
			if ((p[picks[picks.Count - 1]].P - p[0].P).magnitude < 20f && picks.Count > 4)
			{
				picks.RemoveAt(picks.Count - 1);
			}
			for (int k = 0; k < picks.Count; k++)
			{
				Pt q = p[picks[k]];
				GameObject point = new GameObject("TD" + (k + 1));
				point.transform.SetParent(line.transform, true);
				point.transform.position = q.P;
				float half = q.W * 0.42f;
				point.transform.localScale = new Vector3(half, 1f, half);
				point.AddComponent<PathControlPointEditorController>();
			}
			AccessTools.Method(typeof(PathManager), "InitializePath").Invoke(line, null);
			Traverse.Create(line).Field("snapLanesToGround").SetValue(false);
			// Drops look for the ground by a ray from this high above the track line: in the cave that would hit the mountain.
			Traverse.Create(line).Field("snapRayHeight").SetValue(14f);
			line.BakeRacingLine();
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

			// A reset looks down from above the line for the road; 200 m up in the cave would find the mountain.
			tm.resetRayHeight = 6f;
			tm.resetRayLength = 40f;

			TrackScatter scatter = UnityEngine.Object.FindFirstObjectByType<TrackScatter>();
			if (scatter != null)
			{
				scatter.path = line;
			}
			Mod.Log.LogInfo("Temple of Drift track line: " + picks.Count + " control points, " + line.sampledPoints.Count + " samples, " + line.trackLength.ToString("0") + " units, " + line.lanes.Count + " lanes");
		}

		/// <summary>Six slots on the run-up before the start line, two lanes, facing +X; the player is last.</summary>
		private static void PlaceGrid(TrackManager tm, GameObject env)
		{
			Transform container = tm.startingPositionsContainer.transform;
			Transform playerSlot = container.Cast<Transform>().FirstOrDefault(t => t.name == "PlayerPosition");
			CarControllerNew car = tm.playerCarController;
			float lift = playerSlot != null ? car.transform.position.y - playerSlot.position.y : 0.5f;
			float y0 = Data.Main[0].P.y;
			float lane = 10f;

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
				slot.position = new Vector3(-30f * (n + 1), y0, n % 2 == 0 ? lane : -lane);
				n++;
			}
			Vector3 playerAt = new Vector3(-30f * 6f, y0, -lane);
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
				semaphore.position = new Vector3(-22f, semaphore.position.y, 0f);
			}
		}

		/// <summary>Chicken and trap spots all round the lap, on every road and in the cave, away from the grid.</summary>
		private static void SpreadHazards()
		{
			HazardsSpawner_FINAL spawner = UnityEngine.Object.FindFirstObjectByType<HazardsSpawner_FINAL>();
			if (spawner == null)
			{
				return;
			}
			System.Random rng = new System.Random(77);
			List<Pt> p = Data.Main;
			List<GameObject> spots = new List<GameObject>();
			int step = 14;   // one every ~110 units
			for (int i = 50; i < p.Count - 60; i += step)
			{
				Pt q = p[i];
				if (q.Hairpin)
				{
					continue;
				}
				Vector3 right = Data.Right(p, i, true);
				GameObject spot = new GameObject("HazardSpot" + spots.Count);
				spot.transform.SetParent(root.transform, false);
				spot.transform.position = q.P + right * (float)(rng.NextDouble() * q.W * 0.5 - q.W * 0.25) + Vector3.up * 0.3f;
				spots.Add(spot);
			}
			// the short cave branch, so both ways have something in them
			List<Pt> b = Data.Branch;
			for (int i = 10; i < b.Count - 10; i += 20)
			{
				GameObject spot = new GameObject("HazardSpot" + spots.Count);
				spot.transform.SetParent(root.transform, false);
				spot.transform.position = b[i].P + Vector3.up * 0.3f;
				spots.Add(spot);
			}
			Traverse.Create(spawner).Field("spawnPositions").SetValue(spots);
			Mod.Log.LogInfo("Temple of Drift hazard spots: " + spots.Count);
		}
	}
}
