using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// What you see and hear of other players' guns: their gun on their car while they aim, a red line when it points at you, tracers and
	/// the shot's sound, a red mark at the screen edge toward whoever hurt you, and (with the RPG mod) their rockets in flight.
	/// Only the aim, the shots' landing points and the rockets' positions are sent; the damage is still the existing <see cref="Msg.Hit"/>.
	/// </summary>
	internal static class Cues
	{
		private const int MaxPoints = 12;
		private const int MaxRockets = 4;

		/// <summary>Counted for the test rig: shots from other players drawn, and hit marks raised.</summary>
		internal static int ShotsSeen;
		internal static int Flashes;

		// ------------------------------------------------------------------ what the local player is doing

		private static readonly MethodInfo aimScreenPoint = AccessTools.PropertyGetter(typeof(Gun), "AimScreenPoint");
		private static Gun[] guns = new Gun[0];
		private static float gunsAt;
		private static readonly List<Vector3> pendingShots = new List<Vector3>();
		private static int pendingType;
		private static readonly HashSet<ulong> shotLogged = new HashSet<ulong>();

		private static int GunCode(Gun gun)
		{
			try
			{
				return Mathf.Clamp((int)gun.RequiredGunType + 1, 1, 255);
			}
			catch (Exception)
			{
				return (int)GunType.SMG_RIFLE + 1;
			}
		}

		/// <summary>The gun the local player is holding up, or null.</summary>
		private static Gun RaisedGun()
		{
			if (Time.unscaledTime >= gunsAt)
			{
				gunsAt = Time.unscaledTime + 1f;
				guns = UnityEngine.Object.FindObjectsByType<Gun>(FindObjectsSortMode.None);
			}
			foreach (Gun gun in guns)
			{
				if (gun != null && gun.grabbed && gun.isActiveAndEnabled && gun.fpvcamera != null)
				{
					return gun;
				}
			}
			return null;
		}

		/// <summary>Adds the aim to the car's state packet: 0, or the gun type + 1, where it points, and which player's car it is on.</summary>
		internal static void WriteAim(W w, CarControllerNew car)
		{
			Gun gun = aimScreenPoint == null ? null : RaisedGun();
			if (gun == null)
			{
				w.U8(0);
				return;
			}
			Ray ray;
			try
			{
				ray = gun.fpvcamera.ScreenPointToRay((Vector2)aimScreenPoint.Invoke(gun, null));
			}
			catch (Exception)
			{
				w.U8(0);
				return;
			}
			float distance = float.MaxValue;
			foreach (RaycastHit hit in Physics.RaycastAll(ray, 300f, ~0, QueryTriggerInteraction.Ignore))
			{
				if (hit.distance < distance && !hit.collider.transform.IsChildOf(car.transform))
				{
					distance = hit.distance;
				}
			}
			if (distance == float.MaxValue)
			{
				distance = 150f;
			}
			ulong aimedAt = 0;
			float nearest = float.MaxValue;
			foreach (RemoteCar rc in RaceSync.Humans)
			{
				Vector3 toCar = rc.Position - ray.origin;
				float along = Vector3.Dot(toCar, ray.direction);
				if (along <= 0f || along > distance + 6f || along >= nearest)
				{
					continue;
				}
				if ((toCar - ray.direction * along).magnitude < 5f)
				{
					nearest = along;
					aimedAt = rc.PeerId;
				}
			}
			w.U8(GunCode(gun)).Vec(ray.origin + ray.direction * distance).U64(aimedAt);
		}

		internal static void ReadAim(RemoteCar rc, R r)
		{
			int code = r.U8();
			if (code == 0)
			{
				rc.AimGun = 0;
				return;
			}
			Vector3 point = r.Vec();
			ulong aimedAt = r.U64();
			if (float.IsNaN(point.x + point.y + point.z) || point.sqrMagnitude > 1e10f)
			{
				return;
			}
			rc.AimGun = code;
			rc.AimPoint = point;
			rc.AimedAt = aimedAt;
			rc.AimStamp = Time.unscaledTime;
		}

		internal static void NoteShot(Gun gun, Vector3 landed)
		{
			if (gun == null || landed == Vector3.zero || gun.GetType().Name == "RpgLauncher" || pendingShots.Count >= MaxPoints)
			{
				return;
			}
			pendingType = GunCode(gun);
			pendingShots.Add(landed);
		}

		// ------------------------------------------------------------------ per frame

		internal static void Tick()
		{
			if (pendingShots.Count > 0)
			{
				W w = new W(Msg.Shot).U8(pendingType).U8(pendingShots.Count);
				foreach (Vector3 point in pendingShots)
				{
					w.Vec(point);
				}
				pendingShots.Clear();
				Room.SendAll(w);
			}
			TickGhosts();
		}

		// ------------------------------------------------------------------ somebody else's gun

		private static Transform ModelFor(EnemyGun gun, int type)
		{
			switch ((GunType)type)
			{
				case GunType.REVOLVER:
					return gun.revolverModel != null ? gun.revolverModel : gun.smgModel;
				case GunType.SHOTGUN:
				case GunType.SAWED_OFF:
					return gun.shotgunModel != null ? gun.shotgunModel : gun.smgModel;
				default:
					return gun.smgModel != null ? gun.smgModel : gun.revolverModel;
			}
		}

		private static Vector3 Muzzle(Transform model, RemoteCar rc)
		{
			if (model != null && model.gameObject.activeSelf)
			{
				Transform fire = model.Find("FirePoint");
				return fire != null ? fire.position : model.position;
			}
			return rc.Position + Vector3.up * 1.3f;
		}

		private static Transform Shown(EnemyGun gun)
		{
			foreach (Transform model in new[] { gun.revolverModel, gun.smgModel, gun.shotgunModel })
			{
				if (model != null && model.gameObject.activeSelf)
				{
					return model;
				}
			}
			return null;
		}

		private static void Show(Transform model, Transform wanted)
		{
			if (model != null && model.gameObject.activeSelf != (model == wanted))
			{
				model.gameObject.SetActive(model == wanted);
			}
		}

		/// <summary>Every frame, for another player's car: their gun while they aim, and the red line when it is on the local car.</summary>
		internal static void UpdateStandIn(RemoteCar rc)
		{
			EnemyGun gun = rc.Gun;
			if (gun == null)
			{
				return;
			}
			bool racing = Room.Active && Room.Phase == Phase.Racing;
			bool alive = rc.Controller != null && rc.Controller.alive;
			bool aiming = racing && alive && rc.AimGun > 0 && Time.unscaledTime - rc.AimStamp < 0.5f;
			Transform model = aiming ? ModelFor(gun, rc.AimGun - 1) : null;
			Show(gun.revolverModel, model);
			Show(gun.smgModel, model);
			Show(gun.shotgunModel, model);
			if (model != null)
			{
				Vector3 direction = rc.AimPoint - model.position;
				if (direction.sqrMagnitude > 0.01f)
				{
					model.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(gun.aimRotationOffset);
				}
			}

			TrackManager tm = RaceSync.TM;
			bool onMe = aiming && rc.AimedAt == Room.LocalId && !RaceSync.LocalOut && tm != null && tm.playerCarController != null;
			if (onMe && rc.LaserAlpha <= 0f)
			{
				Mod.Log.LogInfo("Aimed at by " + rc.DisplayName());
			}
			rc.LaserAlpha = Mathf.MoveTowards(rc.LaserAlpha, onMe ? 1f : 0f, Time.unscaledDeltaTime * (onMe ? 5f : 3f));
			if (rc.LaserAlpha <= 0.01f)
			{
				if (rc.Laser != null)
				{
					rc.Laser.enabled = false;
				}
				return;
			}
			if (rc.Laser == null)
			{
				GameObject line = new GameObject("MpAimLaser");
				line.transform.SetParent(rc.transform, false);
				rc.Laser = line.AddComponent<LineRenderer>();
				rc.Laser.positionCount = 2;
				rc.Laser.material = gun.tracerMaterial;
				rc.Laser.startWidth = gun.telegraphWidth;
				rc.Laser.endWidth = gun.telegraphWidth;
			}
			rc.Laser.enabled = true;
			Color color = gun.telegraphColor;
			color.a *= rc.LaserAlpha;
			rc.Laser.startColor = color;
			rc.Laser.endColor = color;
			rc.Laser.SetPosition(0, Muzzle(model, rc));
			if (tm != null && tm.playerCarController != null)
			{
				rc.Laser.SetPosition(1, tm.playerCarController.transform.position + Vector3.up);
			}
		}

		// ------------------------------------------------------------------ shots

		internal static void OnShot(ulong from, R r)
		{
			int code = r.U8();
			int count = Math.Min(r.U8(), MaxPoints);
			List<Vector3> points = new List<Vector3>();
			for (int i = 0; i < count; i++)
			{
				Vector3 point = r.Vec();
				if (!float.IsNaN(point.x + point.y + point.z) && point.sqrMagnitude < 1e10f)
				{
					points.Add(point);
				}
			}
			RemoteCar rc = RaceSync.RemoteOf(from);
			if (rc == null || rc.Gun == null || Room.Phase != Phase.Racing || points.Count == 0)
			{
				return;
			}
			if (shotLogged.Add(from))
			{
				Mod.Log.LogInfo("Shot from " + rc.DisplayName() + " (" + points.Count + " point(s))");
			}
			ShotsSeen++;
			Vector3 origin = Muzzle(Shown(rc.Gun), rc);
			foreach (Vector3 point in points)
			{
				TracerLine.Spawn(rc.Gun, origin, point, points.Count);
			}
			AudioClip[] sounds = rc.Gun.shootSounds;
			if (sounds != null && sounds.Length > 0)
			{
				SoundController.PlayRandomSoundAtPosition(sounds, origin, rc.Gun.shootSoundVolume, 25f, 500f);
			}
		}

		// ------------------------------------------------------------------ the mark at the screen edge

		private static CueHud hud;

		internal static void FlashFrom(RemoteCar rc)
		{
			if (rc != null)
			{
				Flash(rc.Position);
			}
		}

		/// <summary>An AI gun's bullet hit the local car.</summary>
		internal static void FlashIfPlayerHit(RaycastHit hit, Vector3 origin)
		{
			TrackManager tm = RaceSync.TM;
			if (!Room.Active || Room.Race == null || tm == null || tm.playerCarController == null || hit.collider == null)
			{
				return;
			}
			if (hit.collider.transform.IsChildOf(tm.playerCarController.transform))
			{
				Flash(origin);
			}
		}

		private static void Flash(Vector3 source)
		{
			if (RaceSync.LocalOut)
			{
				return;
			}
			Flashes++;
			if (hud == null)
			{
				GameObject go = new GameObject("MpCueHud");
				hud = go.AddComponent<CueHud>();
			}
			hud.Add(source);
		}

		// ------------------------------------------------------------------ rockets (the RPG mod's, if it is installed)

		private sealed class Ghost
		{
			internal GameObject Body;
			internal Vector3 Position;
			internal Vector3 Velocity;
			internal float Stamp;
		}

		private static readonly Dictionary<ulong, Dictionary<int, Ghost>> ghosts = new Dictionary<ulong, Dictionary<int, Ghost>>();
		private static bool rocketLooked;
		private static Type rocketType;
		private static FieldInfo rocketVelocity;
		private static Type rpgModels;
		private static Type rpgVfx;

		private static void FindRocket()
		{
			if (rocketLooked)
			{
				return;
			}
			rocketLooked = true;
			rocketType = AccessTools.TypeByName("HalfGripRpg.RpgProjectile");
			if (rocketType == null)
			{
				return;
			}
			rocketVelocity = AccessTools.Field(rocketType, "velocity");
			rpgModels = rocketType.Assembly.GetType("HalfGripRpg.RpgModels");
			rpgVfx = rocketType.Assembly.GetType("HalfGripRpg.Vfx");
			MethodInfo explode = AccessTools.Method(rocketType, "Explode");
			if (rocketVelocity == null || explode == null)
			{
				Mod.Log.LogWarning("The RPG's rocket looks different from what Multiplayer expects, so other players will not see it fly.");
				rocketType = null;
				return;
			}
			Mod.Ctx.Harmony.Patch(explode, prefix: new HarmonyMethod(typeof(Cues), nameof(ExplodePrefix)));
			Mod.Log.LogInfo("RPG found: its rockets are shown to the other players.");
		}

		// The rocket is about to go off (the argument is named 'at' in the RPG mod): tell the others where.
		private static void ExplodePrefix(object __instance, Vector3 at)
		{
			UnityEngine.Object rocket = __instance as UnityEngine.Object;
			if (rocket != null && Room.Active && Room.Phase == Phase.Racing)
			{
				Room.SendAll(new W(Msg.RocketEnd).I32(rocket.GetInstanceID()).Vec(at));
			}
		}

		/// <summary>Twenty times a second: where the local player's rockets are.</summary>
		internal static void SendRockets()
		{
			FindRocket();
			if (rocketType == null)
			{
				return;
			}
			UnityEngine.Object[] found = UnityEngine.Object.FindObjectsByType(rocketType, FindObjectsSortMode.None);
			if (found.Length == 0)
			{
				return;
			}
			int count = Math.Min(found.Length, MaxRockets);
			W w = new W(Msg.Rocket).U8(count);
			for (int i = 0; i < count; i++)
			{
				Component rocket = (Component)found[i];
				w.I32(rocket.GetInstanceID()).Vec(rocket.transform.position).Vec((Vector3)rocketVelocity.GetValue(rocket));
			}
			Room.SendAll(w, false);
		}

		internal static void OnRocket(ulong from, R r)
		{
			int count = Math.Min(r.U8(), MaxRockets);
			for (int i = 0; i < count; i++)
			{
				int id = r.I32();
				Vector3 position = r.Vec();
				Vector3 velocity = r.Vec();
				if (float.IsNaN(position.x + position.y + position.z + velocity.x + velocity.y + velocity.z) || position.sqrMagnitude > 1e10f || RaceSync.RemoteOf(from) == null || Room.Phase != Phase.Racing)
				{
					continue;
				}
				Dictionary<int, Ghost> mine;
				if (!ghosts.TryGetValue(from, out mine))
				{
					mine = new Dictionary<int, Ghost>();
					ghosts[from] = mine;
				}
				Ghost ghost;
				if (!mine.TryGetValue(id, out ghost))
				{
					ghost = new Ghost { Body = MakeGhost(position, velocity) };
					mine[id] = ghost;
				}
				ghost.Position = position;
				ghost.Velocity = velocity;
				ghost.Stamp = Time.unscaledTime;
			}
		}

		private static GameObject MakeGhost(Vector3 position, Vector3 velocity)
		{
			GameObject go = new GameObject("MpGhostRocket");
			go.transform.SetPositionAndRotation(position, velocity.sqrMagnitude > 1f ? Quaternion.LookRotation(velocity) : Quaternion.identity);
			try
			{
				// the same body and smoke trail the RPG gives its own rocket, with no collider and no damage
				rpgModels.GetMethod("AttachFlyingRocket").Invoke(null, new object[] { go.transform });
				float length = (float)rpgModels.GetField("FlyingLength").GetRawConstantValue();
				rpgVfx.GetMethod("Trail").Invoke(null, new object[] { go.transform, Vector3.back * length * 0.95f });
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not dress another player's rocket like the RPG's: " + e.Message);
			}
			return go;
		}

		private static void TickGhosts()
		{
			if (ghosts.Count == 0)
			{
				return;
			}
			float now = Time.unscaledTime;
			foreach (Dictionary<int, Ghost> mine in ghosts.Values)
			{
				List<int> gone = null;
				foreach (KeyValuePair<int, Ghost> pair in mine)
				{
					Ghost ghost = pair.Value;
					float age = now - ghost.Stamp;
					if (ghost.Body == null || age > 0.5f)
					{
						// no word for half a second and no explosion either: the rocket dropped without going off
						if (ghost.Body != null)
						{
							UnityEngine.Object.Destroy(ghost.Body);
						}
						(gone ?? (gone = new List<int>())).Add(pair.Key);
						continue;
					}
					Vector3 target = ghost.Position + ghost.Velocity * Mathf.Min(age, 0.25f);
					Transform t = ghost.Body.transform;
					t.position = Vector3.Lerp(t.position, target, 0.5f);
					if (ghost.Velocity.sqrMagnitude > 1f)
					{
						t.rotation = Quaternion.LookRotation(ghost.Velocity);
					}
				}
				if (gone != null)
				{
					foreach (int id in gone)
					{
						mine.Remove(id);
					}
				}
			}
		}

		internal static void OnRocketEnd(ulong from, R r)
		{
			int id = r.I32();
			Vector3 at = r.Vec();
			if (float.IsNaN(at.x + at.y + at.z) || at.sqrMagnitude > 1e10f)
			{
				return;
			}
			Dictionary<int, Ghost> mine;
			Ghost ghost;
			if (!ghosts.TryGetValue(from, out mine) || !mine.TryGetValue(id, out ghost))
			{
				return; // never saw it fly: no blast to show
			}
			if (ghost.Body != null)
			{
				UnityEngine.Object.Destroy(ghost.Body);
			}
			mine.Remove(id);
			try
			{
				rpgVfx.GetMethod("Explosion").Invoke(null, new object[] { at, 13f });
				AudioClip boom = (AudioClip)rpgVfx.GetMethod("BoomClip").Invoke(null, null);
				SoundController.PlayRandomSoundAtPosition(new[] { boom }, at, 1f, 40f, 800f, 0.08f);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not show another player's rocket going off: " + e.Message);
			}
		}

		// ------------------------------------------------------------------ clean-up

		/// <summary>The race is over or the mod is switched off: take every object it added to the scene away.</summary>
		internal static void Remove()
		{
			pendingShots.Clear();
			shotLogged.Clear();
			foreach (Dictionary<int, Ghost> mine in ghosts.Values)
			{
				foreach (Ghost ghost in mine.Values)
				{
					if (ghost.Body != null)
					{
						UnityEngine.Object.Destroy(ghost.Body);
					}
				}
			}
			ghosts.Clear();
			if (hud != null)
			{
				UnityEngine.Object.Destroy(hud.gameObject);
				hud = null;
			}
		}
	}

	/// <summary>Another player's gun fired along a line: the game's own tracer, from their car to where the shot landed.</summary>
	internal sealed class TracerLine : MonoBehaviour
	{
		private const float Life = 0.5f;

		private LineRenderer line;
		private Color color;
		private float age;

		internal static void Spawn(EnemyGun gun, Vector3 from, Vector3 to, int pellets)
		{
			GameObject go = new GameObject("MpTracer");
			TracerLine tracer = go.AddComponent<TracerLine>();
			LineRenderer lr = go.AddComponent<LineRenderer>();
			lr.positionCount = 2;
			lr.SetPosition(0, from);
			lr.SetPosition(1, to);
			lr.material = gun.tracerMaterial;
			float width = 0.1f * gun.tracerWidthMultiplier / Mathf.Max(1, pellets);
			lr.startWidth = width;
			lr.endWidth = width;
			tracer.line = lr;
			tracer.color = lr.material.color;
			tracer.color.a = 1f;
		}

		private void Update()
		{
			age += Time.deltaTime;
			if (age >= Life || line == null)
			{
				Destroy(gameObject);
				return;
			}
			color.a = Mathf.Lerp(1f, 0f, age / Life);
			line.startColor = color;
			line.endColor = color;
		}
	}

	/// <summary>A red bar at the screen edge pointing toward whatever just hurt the local car, fading over a second.</summary>
	internal sealed class CueHud : MonoBehaviour
	{
		private const float Life = 1f;

		private struct Mark
		{
			internal Vector3 Source;
			internal float At;
		}

		private readonly List<Mark> marks = new List<Mark>();

		internal void Add(Vector3 source)
		{
			if (marks.Count >= 8)
			{
				marks.RemoveAt(0);
			}
			marks.Add(new Mark { Source = source, At = Time.unscaledTime });
		}

		private void OnGUI()
		{
			TrackManager tm = RaceSync.TM;
			Camera cam = tm != null && tm.playerCarController != null ? tm.playerCarController.fpvCamera : null;
			if (Event.current.type != EventType.Repaint || cam == null)
			{
				return;
			}
			Matrix4x4 saved = GUI.matrix;
			Color savedColor = GUI.color;
			Vector2 centre = new Vector2(Screen.width, Screen.height) * 0.5f;
			float length = Screen.height * 0.16f;
			float thick = Screen.height * 0.022f;
			for (int i = marks.Count - 1; i >= 0; i--)
			{
				float age = Time.unscaledTime - marks[i].At;
				if (age > Life)
				{
					marks.RemoveAt(i);
					continue;
				}
				Vector3 local = cam.transform.InverseTransformPoint(marks[i].Source);
				float angle = Mathf.Atan2(local.x, local.z);
				Vector2 at = new Vector2(centre.x + Mathf.Sin(angle) * centre.x * 0.9f, centre.y - Mathf.Cos(angle) * centre.y * 0.88f);
				GUI.matrix = saved;
				GUIUtility.RotateAroundPivot(angle * Mathf.Rad2Deg, at);
				GUI.color = new Color(1f, 0.1f, 0.1f, 0.85f * (1f - age / Life));
				GUI.DrawTexture(new Rect(at.x - length * 0.5f, at.y - thick * 0.5f, length, thick), Texture2D.whiteTexture);
			}
			GUI.matrix = saved;
			GUI.color = savedColor;
		}
	}

	// A shot along a ray (not the RPG's rocket, which is shown by its own flight): the landing point is sent so the others can draw it.
	[HarmonyPatch(typeof(Gun), "FireRay")]
	internal static class ShotCue
	{
		private static void Postfix(Gun __instance, Vector3 __result)
		{
			if (Room.Active && Room.Phase == Phase.Racing)
			{
				Cues.NoteShot(__instance, __result);
			}
		}
	}

	// Another player's stand-in is made from an enemy car; its gun parts must survive (the chance of "no gun" is rolled in Awake).
	[HarmonyPatch(typeof(EnemyGun), "Awake")]
	internal static class KeepStandInGun
	{
		private static void Prefix(EnemyGun __instance)
		{
			if (RaceSync.MakingHuman)
			{
				__instance.noGunChance = 0f;
			}
		}
	}
}
