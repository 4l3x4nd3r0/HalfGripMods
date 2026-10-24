using HalfGripTools;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripRpg
{
	/// <summary>
	/// The RPG itself. It is a gun in its own right, not a re-dressed SMG: it has no slide, no magazine and no safety. It holds
	/// one rocket at a time, which you push into the muzzle (see <see cref="RpgRocket"/>); you aim it like any other gun and a
	/// pull of the trigger sends that rocket out of the tube as a projectile (see <see cref="RpgProjectile"/>).
	/// </summary>
	internal class RpgLauncher : Gun
	{
		/// <summary>Where a rocket is, and which way it faces.</summary>
		public struct Pose
		{
			public Vector3 position;

			public Quaternion rotation;

			// Where the middle of the rocket's art should end up, and the way its warhead should point.
			public Vector3 centre;

			public Vector3 artUp;
		}

		// The SMG's sprites by name, and the art that stands in for each. The slide, its back, and the round and feed sprites are made empty.
		private static readonly Dictionary<string, string> Reskin = new Dictionary<string, string>
		{
			{ "baza smg_0", "rpg_body.png" },
			{ "smg2_0", "rpg_clear_slide.png" },
			{ "smg3 backside_0", "rpg_clear_back.png" },
			{ "smg4_0", "rpg_clear_round.png" },
			{ "fullbullet_0", "rpg_clear_feed.png" },
			{ "amunitie smg_0", "rpg_rocket.png" },
			{ "amunitie smg full_0", "rpg_rocket.png" }
		};

		// The launcher art is drawn at about twice the SMG's pixel density, so it needs its own pixels-per-unit to come out long.
		private static readonly Dictionary<string, float> Ppu = new Dictionary<string, float>
		{
			{ "rpg_body.png", 14f }
		};

		public const string BodySprite = "rpg_body";

		// rpg_body.png is 258x62 and drawn at 14 pixels per unit around its centre. The tube's mouth is its left edge, on row 12.5 from the top.
		private const float BodyPixelsPerUnit = 14f;

		private const float MuzzleX = -(258f / 2f) / BodyPixelsPerUnit;

		private const float MuzzleY = (62f / 2f - 12.5f) / BodyPixelsPerUnit;

		// How much of a rocket is in the tube when it is home. The rest, the warhead, stays out in front.
		private const float InsideShare = 0.72f;

		// How much longer the held launcher is than the SMG it replaces.
		private const float HeldLengthOverSmg = 1.35f;

		// Seconds between shots, and between the shot and lowering the launcher.
		private const float Cooldown = 0.5f;

		private const float LowerDelay = 0.35f;

		private AudioClip[] seatSounds;

		private AudioClip[] clickSounds;

		private TrackManager race;

		private SpriteRenderer bodySprite;

		private Vector3 muzzle;

		private Vector3 outward;

		private Vector3 normal;

		private GameObject heldRocket;

		private RpgRocket loadedRocket;

		private RpgRocket reserved;

		private bool loaded;

		private bool dryClicked;

		private float nextShot;

		private bool ready;

		public override GunType RequiredGunType => GunType.SMG_RIFLE;

		/// <summary>The launcher's body sprite: what a loaded rocket is drawn in front of and behind.</summary>
		public SpriteRenderer BodyRenderer => bodySprite;

		private Camera viewCamera;

		/// <summary>Where the camera that draws the car's interior is.</summary>
		public Vector3 ViewPosition
		{
			get
			{
				if (viewCamera == null)
				{
					GameObject named = GameObject.Find("InteriorCamera");
					viewCamera = named != null ? named.GetComponent<Camera>() : null;
					if (viewCamera == null)
					{
						viewCamera = fpvcamera != null ? fpvcamera : Camera.main;
					}
				}
				return viewCamera != null ? viewCamera.transform.position : transform.position + Vector3.back * 10f;
			}
		}

		/// <summary>The body sprite's transform, which a loaded rocket rides on.</summary>
		public Transform BodyTransform => bodySprite != null ? bodySprite.transform : null;

		/// <summary>
		/// True if <paramref name="tip"/> is within <paramref name="radius"/> of the muzzle. Measured across the plane the launcher is
		/// drawn in, not through it: the rack is a good way behind the launcher, and a straight-line distance would never be small.
		/// </summary>
		public bool IsNearMuzzle(Vector3 tip, float radius)
		{
			if (bodySprite == null)
			{
				return false;
			}
			RefreshMuzzle();
			return Vector3.ProjectOnPlane(tip - muzzle, normal).magnitude < radius;
		}

		/// <summary>Where the tube's mouth is in the world right now.</summary>
		public Vector3 MuzzlePoint
		{
			get
			{
				if (bodySprite != null)
				{
					RefreshMuzzle();
				}
				return muzzle;
			}
		}

		/// <summary>True while a rocket may be pushed in: nothing loaded, nothing already going in, and the race is under way.</summary>
		public bool CanAccept => ready && isActiveAndEnabled && !loaded && reserved == null && !Ceasefire;

		private bool Ceasefire => race != null && race.InOpeningCeasefire;

		// Wake-up is deferred to Setup, which runs once the SMG's authored settings have been copied across.
		protected override void Awake()
		{
		}

		/// <summary>Takes over the SMG gun's scenery and settings, then wakes up like any gun.</summary>
		public void Setup(NAutomaticGun smg)
		{
			GunConversion.CopyFields(typeof(Gun), smg, this);
			seatSounds = smg.magLockSounds;
			clickSounds = smg.emptyClickSounds;

			// The SMG's slide, the thing you rack to chamber a round, has no place on a launcher.
			if (smg.slide != null)
			{
				smg.slide.gameObject.SetActive(false);
			}
			base.Awake();
		}

		protected override void Start()
		{
			base.Start();
			race = Object.FindFirstObjectByType<TrackManager>();
			Equip();
		}

		// A rocket launcher has no tutorial of its own to teach.
		protected override void RegisterTutorial()
		{
		}

		// The look of it all: sprites, the 3D model you hold, the muzzle load zone and the sounds.
		private void Equip()
		{
			// No bullet tracer: the rocket is its own streak of smoke.
			tracerWidthMultiplier = 0f;
			tracerLifetime = 0.01f;
			shootSounds = new[] { Vfx.LaunchClip() };
			shoootSoundVolume = 0.9f;

			Mod.Ctx.Sprites.Reskin(Reskin, Ppu);
			BuildLoadZone();
			BuildHeldModel();
			ready = true;
			if (Mod.DumpInfo.Value)
			{
				int lines = 0;
				Dump(transform, 0, ref lines);
			}
		}

		// What the gun object is made of, for the log: handy when something is not where it should be.
		private static void Dump(Transform t, int depth, ref int lines)
		{
			if (lines++ > 250 || depth > 6)
			{
				return;
			}
			var parts = new List<string>();
			foreach (Component c in t.GetComponents<Component>())
			{
				if (c != null && !(c is Transform))
				{
					parts.Add(c.GetType().Name);
				}
			}
			Mod.Log.LogInfo($"[tree] {new string(' ', depth * 2)}{t.name} {(t.gameObject.activeSelf ? "" : "(inactive) ")}pos={t.localPosition} scale={t.localScale} [{string.Join(",", parts)}]");
			foreach (Transform child in t)
			{
				Dump(child, depth + 1, ref lines);
			}
		}

		// The muzzle is the left end of the body sprite, on the row the barrel is centred on.
		private void BuildLoadZone()
		{
			bodySprite = null;
			foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>(true))
			{
				if (renderer.sprite != null && renderer.sprite.name == BodySprite)
				{
					bodySprite = renderer;
					break;
				}
			}
			if (bodySprite == null)
			{
				Mod.Log.LogWarning("Could not find the RPG body sprite, so there is nowhere to load a rocket");
				return;
			}

			RefreshMuzzle();
			Mod.Log.LogInfo($"Muzzle: {muzzle} outward={outward}");
		}

		// The 3D gun you hold while aiming. The SMG's meshes are hidden and the launcher built from the art goes in their place, as long
		// as the SMG's were, with its muzzle at the gun's fire point.
		private void BuildHeldModel()
		{
			if (aimModel == null || firePoint == null)
			{
				Mod.Log.LogWarning("No aim model or fire point, so the held RPG has no model");
				return;
			}

			// Measure the SMG's model in the aim model's own space, before it is hidden.
			bool any = false;
			float front = float.MinValue;
			float rear = float.MaxValue;
			Material material = null;
			int layer = aimModel.gameObject.layer;
			foreach (MeshFilter filter in aimModel.GetComponentsInChildren<MeshFilter>(true))
			{
				MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
				if (renderer == null || filter.sharedMesh == null)
				{
					continue;
				}
				layer = renderer.gameObject.layer;
				if (material == null && renderer.sharedMaterial != null && renderer.sharedMaterial.mainTexture != null)
				{
					material = renderer.sharedMaterial;
				}
				Bounds bounds = filter.sharedMesh.bounds;
				for (int i = 0; i < 8; i++)
				{
					Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
					float z = aimModel.InverseTransformPoint(filter.transform.TransformPoint(corner)).z;
					front = Mathf.Max(front, z);
					rear = Mathf.Min(rear, z);
					any = true;
				}
				renderer.enabled = false;
			}
			if (!any)
			{
				Mod.Log.LogWarning("The SMG's held model has no meshes to measure, so the held RPG has no model");
				return;
			}

			RpgModels.GunMaterial = material;
			Vector3 muzzleLocal = aimModel.InverseTransformPoint(firePoint.position);
			float length = (front - rear) * HeldLengthOverSmg;
			RpgModels.BuildHeld(aimModel, muzzleLocal, length, layer, out heldRocket);
			Mod.Log.LogInfo($"Held RPG model: muzzle {muzzleLocal}, length {length} (SMG model {rear} to {front}), layer {LayerMask.LayerToName(layer)}");
		}

		// Where the tube's mouth is right now. The body is part of a handle that can move, so this is worked out afresh when it is needed.
		private void RefreshMuzzle()
		{
			Transform t = bodySprite.transform;
			float flipX = bodySprite.flipX ? -1f : 1f;
			muzzle = t.TransformPoint(new Vector3(MuzzleX * flipX, MuzzleY * (bodySprite.flipY ? -1f : 1f), 0f));
			outward = (t.TransformPoint(new Vector3(-flipX, 0f, 0f)) - t.position).normalized;
			normal = t.forward;
		}

		/// <summary>The poses a rocket takes on its way in: lined up in front of the tube, then pushed home.</summary>
		public bool TryPoses(RpgRocket rocket, out Pose align, out Pose insert)
		{
			align = default;
			insert = default;
			float length = rocket.Length;
			if (bodySprite == null || length <= 0f)
			{
				return false;
			}
			RefreshMuzzle();

			// The rocket's own up is its warhead; turn that to point out of the tube, about the plane the rack and body sit in.
			Quaternion rest = rocket.RestRotation;
			// Its art may be turned relative to the object it hangs on, so go by the art's own up.
			Vector3 warhead = Vector3.ProjectOnPlane(rest * rocket.ArtOffset * Vector3.up, normal);
			float angle = Vector3.SignedAngle(warhead, Vector3.ProjectOnPlane(outward, normal), normal);
			Quaternion turned = Quaternion.AngleAxis(angle, normal) * rest;

			// The object's origin is not the sprite's middle, so aim the middle and work back to the origin.
			Vector3 middle = Quaternion.Inverse(rocket.transform.rotation) * rocket.CentreOffset;
			// It goes in at the launcher body's own depth rather than the rack's: with a perspective camera a different depth shows up as
			// the rocket sitting off the tube. (Its warhead and shaft are then nudged in front of and behind the body, see RpgRocket.)
			float depth = 0f;

			align.rotation = turned;
			insert.rotation = turned;
			align.centre = AtDepth(muzzle + outward * (length * 0.5f + 0.3f), depth);
			insert.centre = AtDepth(muzzle - outward * (length * (InsideShare - 0.5f)), depth);
			align.artUp = outward;
			insert.artUp = outward;
			align.position = align.centre - turned * middle;
			insert.position = insert.centre - turned * middle;
			return true;
		}

		/// <summary>
		/// Once a rocket is home, measures where its art really is and nudges it onto the tube's axis and along it. The arithmetic
		/// that got it close cannot know how the scene's sprites are set up; this does not need to.
		/// </summary>
		public void Refine(RpgRocket rocket, Pose target)
		{
			float turn = Vector3.SignedAngle(Vector3.ProjectOnPlane(rocket.ArtUp, normal), Vector3.ProjectOnPlane(target.artUp, normal), normal);
			rocket.transform.RotateAround(rocket.SpriteCentre, normal, turn);
			Vector3 off = target.centre - rocket.SpriteCentre;
			rocket.transform.position += Vector3.ProjectOnPlane(off, normal);
		}

		private Vector3 AtDepth(Vector3 point, float depth)
		{
			Vector3 inPlane = point - normal * Vector3.Dot(point - bodySprite.transform.position, normal);
			return inPlane + normal * depth;
		}

		public void Reserve(RpgRocket rocket)
		{
			reserved = rocket;
		}

		/// <summary>The rocket is home: the tube is loaded, and nothing more to do.</summary>
		public void OnRocketSeated(RpgRocket rocket)
		{
			reserved = null;
			loadedRocket = rocket;
			loaded = true;
			dryClicked = false;
			SoundController.PlayRandomSound2D(seatSounds, gameObject, 0.8f);
			// A solid clunk as it locks in.
			if (Shaker.instance != null)
			{
				Shaker.instance.Shake(0.5f, 12f, 0.01f, 0.18f, 2);
			}
		}

		/// <summary>A loaded rocket was pulled back out: the tube is empty and the rocket is whole again.</summary>
		public void OnRocketRemoved(RpgRocket rocket)
		{
			if (reserved == rocket)
			{
				reserved = null;
			}
			if (loadedRocket == rocket)
			{
				loadedRocket = null;
				loaded = false;
				if (grabbed)
				{
					ExitAimMode();
				}
			}
			SoundController.PlayRandomSound2D(seatSounds, gameObject, 0.4f);
		}

		/// <summary>Grabbing the loaded rocket lifts the launcher to aim, the same as grabbing the tube.</summary>
		public Grabbable RocketRedirect()
		{
			return AimRedirect();
		}

		protected override bool CanEnterAimMode()
		{
			return loaded && !Ceasefire;
		}

		protected override bool IsFull()
		{
			return loaded;
		}

		protected override bool PartsAim()
		{
			return loaded;
		}

		protected override int ShotsLeft()
		{
			return loaded ? 1 : 0;
		}

		// Infinite ammo from the cheat console: there is always a rocket in the tube.
		protected override void LoadFully()
		{
			loaded = true;
		}

		protected override void Update()
		{
			if (!ready)
			{
				return;
			}
			if (aimModel != null)
			{
				aimModel.gameObject.SetActive(grabbed);
			}
			if (heldRocket != null)
			{
				heldRocket.SetActive(loaded && grabbed);
			}
			if (!GameInput.Hand(1))
			{
				dryClicked = false;
			}
			base.Update();
		}

		public override void Shoot()
		{
			if (Time.time < nextShot)
			{
				return;
			}
			if (!loaded && !Endless)
			{
				if (!dryClicked)
				{
					SoundController.PlayRandomSound2D(clickSounds, gameObject, 0.5f);
					dryClicked = true;
				}
				return;
			}
			nextShot = Time.time + Cooldown;
			if (!Endless)
			{
				loaded = false;
			}
			FireShot();
			OnFired();
		}

		// The rocket is out of the tube: its casing goes with it and the launcher is lowered so the next one can go in.
		private void OnFired()
		{
			if (loadedRocket != null)
			{
				RpgRocket spent = loadedRocket;
				loadedRocket = null;
				spent.Spend();
			}
			if (Mod.LowerAfterShot.Value)
			{
				StartCoroutine(Lower());
			}
		}

		private IEnumerator Lower()
		{
			yield return new WaitForSeconds(LowerDelay);
			if (grabbed)
			{
				ExitAimMode();
				if (body != null)
				{
					body.Release();
				}
			}
		}

		/// <summary>Called in place of the gun's bullet ray: launches a rocket. Returns where the shot was aimed, which is what the gun's effects use.</summary>
		public Vector3 LaunchRocket(Vector3 origin, Vector3 direction)
		{
			Vector3 target = origin + direction * 300f;
			if (Physics.Raycast(origin, direction, out RaycastHit hit, 1000f, hitLayers))
			{
				target = hit.point;
			}
			Vector3 from = firePoint != null ? firePoint.position : origin;

			// The rocket falls a little on the way, so aim a little high by as much as it will fall.
			float averageSpeed = Mathf.Max(1f, (Mod.LaunchSpeed.Value + Mod.MaxSpeed.Value) * 0.5f);
			float flightTime = Vector3.Distance(from, target) / averageSpeed;
			Vector3 aimed = target + Vector3.up * (flightTime * flightTime);

			Vector3 heading = aimed - from;
			if (heading.sqrMagnitude < 4f)
			{
				heading = direction;
			}
			RpgProjectile.Launch(from, heading.normalized, hitLayers);
			Vfx.Backblast(from, -heading.normalized);
			return target;
		}
	}
}
