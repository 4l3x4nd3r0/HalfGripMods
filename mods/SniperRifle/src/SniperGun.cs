using HalfGripTools;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HalfGripSniper
{
	/// <summary>
	/// The Sniper Rifle itself. It is a gun in its own right, not a re-dressed SMG: one round at a time, worked by a bolt.
	///
	/// The round's life: a cartridge from the rack is pushed into the open breech (see <see cref="Cartridge"/>); closing the bolt
	/// chambers it; raising the rifle zooms the view and builds a charge; a pull of the trigger spends the round, with damage by the
	/// charge; the case stays in the breech until the bolt is pulled back, which throws it out and leaves the breech open for the next.
	/// </summary>
	internal class SniperGun : Gun
	{
		/// <summary>Where a cartridge is, and which way it faces.</summary>
		public struct Pose
		{
			public Vector3 position;

			public Quaternion rotation;

			// Where the middle of the cartridge's art should end up, and the way its tip should point.
			public Vector3 centre;

			public Vector3 artUp;
		}

		// The SMG's sprites by name, and the art that stands in for each. The back, round and feed sprites are made empty.
		private static readonly Dictionary<string, string> Reskin = new Dictionary<string, string>
		{
			{ "baza smg_0", "sniper_body.png" },
			{ "smg2_0", "sniper_bolt.png" },
			{ "smg3 backside_0", "sniper_clear_back.png" },
			{ "smg4_0", "sniper_clear_round.png" },
			{ "fullbullet_0", "sniper_clear_feed.png" },
			{ "amunitie smg_0", "sniper_round.png" },
			{ "amunitie smg full_0", "sniper_round.png" }
		};

		// The rifle is much longer than the SMG, so its art needs its own pixels-per-unit (higher than the SMG's, so the pixels are a bit finer).
		private static readonly Dictionary<string, float> Ppu = new Dictionary<string, float>
		{
			{ "sniper_body.png", BodyPixelsPerUnit }
		};

		public const string BodySprite = "sniper_body";

		// sniper_body.png is 168x44 and drawn at 8.11 pixels per unit around its centre, which makes it as long as the 290-pixel version
		// was but with chunky pixels like the rest of the cockpit (make_sniper_sprites.py prints these). The muzzle is its left edge on
		// row 21 from the top; the breech is on the receiver, at column 102, row 20; the middle of the bolt handle's art goes at column 96, row 20.
		private const float BodyPixelsPerUnit = 8.11f;

		private const float BodyWidth = 168f;

		private const float BodyHeight = 44f;

		private const float MuzzleRow = 21f;

		private const float BreechColumn = 102f;

		private const float BreechRow = 20f;

		private const float BoltColumn = 96f;

		private const float BoltRow = 20f;

		// How much longer the held rifle is than the SMG it replaces.
		private const float HeldLengthOverSmg = 1.6f;

		// Seconds between shots, and between the shot and lowering the rifle.
		private const float Cooldown = 0.4f;

		private const float LowerDelay = 0.35f;

		private AudioClip[] pullSounds;

		private AudioClip[] releaseSounds;

		private AudioClip[] seatSounds;

		private AudioClip[] clickSounds;

		private ParticleSystem ejectEffect;

		private LinearObject bolt;

		private float boltBack;

		private float boltForward;

		private TrackManager race;

		private SpriteRenderer bodySprite;

		private Vector3 breech;

		private Vector3 outward;

		private Vector3 upward;

		private Vector3 normal;

		private Camera viewCamera;

		private LaserDot laser;

		private ScopeOverlay overlay;

		private SpriteRenderer crosshairSprite;

		private Camera interiorCamera;

		private bool cockpitHidden;

		// Z toggles the scope while the rifle is up. Raised, the rifle starts without it: a quick shot, no zoom, no charge.
		private bool scopeIn;

		private Cartridge seatedCartridge;

		private Cartridge reserved;

		// What is in the rifle. A round in the chamber with the bolt shut is ready to fire; a case stays until the bolt throws it out.
		private bool chambered;

		private bool spentCase;

		private bool boltOpen;

		private bool holdOpen;

		private bool dryClicked;

		private float nextShot;

		private float aimTime;

		private float zoom;

		private bool ready;

		public override GunType RequiredGunType => GunType.SMG_RIFLE;

		/// <summary>The rifle's body sprite: what a cartridge in the breech is drawn in front of.</summary>
		public SpriteRenderer BodyRenderer => bodySprite;

		/// <summary>The body sprite's transform, which a cartridge in the breech rides on.</summary>
		public Transform BodyTransform => bodySprite != null ? bodySprite.transform : null;

		/// <summary>True while the rifle is raised and the scope is in use (switched on with Z).</summary>
		private bool Scoped => grabbed && scopeIn;

		/// <summary>How far the view is zoomed in, 0 to 1.</summary>
		public float ZoomAmount => zoom;

		/// <summary>Where the rifle is pointed on the screen, in the game's screen coordinates (y up).</summary>
		public Vector2 ScreenAimPoint => AimScreenPoint;

		/// <summary>How far the shot is charged, 0 to 1.</summary>
		public float Charge
		{
			get
			{
				float time = Mathf.Max(0.05f, Mod.ChargeTime.Value);
				return Mathf.Clamp01((aimTime - Mod.ChargeDelay.Value) / time);
			}
		}

		/// <summary>The damage a shot fired now would do.</summary>
		public float ShotDamage
		{
			get
			{
				float charge = Charge;
				// Three points: none, half and full charge, so the first half of the bar is worth more than the second.
				return charge < 0.5f
					? Mathf.Lerp(Mod.BaseDamage.Value, Mod.HalfChargeDamage.Value, charge * 2f)
					: Mathf.Lerp(Mod.HalfChargeDamage.Value, Mod.FullChargeDamage.Value, charge * 2f - 1f);
			}
		}

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

		/// <summary>True while a cartridge may be pushed in: the bolt is open, nothing in the breech, nothing already going in, and the race is under way.</summary>
		public bool CanAccept => ready && isActiveAndEnabled && boltOpen && !chambered && !spentCase && seatedCartridge == null && reserved == null && !Ceasefire;

		private bool Ceasefire => race != null && race.InOpeningCeasefire;

		// Wake-up is deferred to Setup, which runs once the SMG's authored settings have been copied across.
		protected override void Awake()
		{
		}

		/// <summary>Takes over the SMG gun's scenery and settings, then wakes up like any gun.</summary>
		public void Setup(NAutomaticGun smg)
		{
			GunConversion.CopyFields(typeof(Gun), smg, this);
			pullSounds = smg.slidePullSounds;
			releaseSounds = smg.slideReleaseSounds;
			seatSounds = smg.magLockSounds;
			clickSounds = smg.emptyClickSounds;
			ejectEffect = smg.bulletEjectEffect;
			// The SMG's slide is the rifle's bolt. Its limits are the SMG's, so how far back counts as "open" comes from it too.
			bolt = smg.slide;
			boltBack = smg.slideBackThreshold;
			boltForward = smg.slideForwardThreshold;
			base.Awake();
			if (bolt != null)
			{
				bolt.onGrabbed.AddListener(OnBoltGrabbed);
			}
		}

		protected override void Start()
		{
			base.Start();
			race = Object.FindFirstObjectByType<TrackManager>();
			Equip();
		}

		// A bolt-action has no tutorial of its own to teach.
		protected override void RegisterTutorial()
		{
		}

		// Grabbing the bolt while the rifle is ready raises the rifle instead, like grabbing the SMG's slide would.
		protected override IEnumerable<Grabbable> AimParts()
		{
			yield return bolt;
		}

		private void Equip()
		{
			CopyRevolverShot();
			Mod.Ctx.Sprites.Reskin(Reskin, Ppu);
			BuildBreech();
			PlaceBolt();
			BuildHeldModel();
			laser = LaserDot.Make();
			overlay = ScopeOverlay.Make(this);
			ready = true;
			if (Mod.DumpInfo.Value)
			{
				int lines = 0;
				Dump(transform, 0, ref lines);
			}
		}

		// Sounds, tracer and hit effects come from the revolver's gun object: a crack, and a streak to follow.
		private void CopyRevolverShot()
		{
			Gun revolver = Object.FindObjectsByType<Gun>(FindObjectsInactive.Include, FindObjectsSortMode.None)
				.FirstOrDefault(g => g != this && g.RequiredGunType == GunType.REVOLVER);
			if (revolver == null)
			{
				Mod.Log.LogWarning("No revolver gun in this scene, so the rifle keeps the SMG's sounds and tracer");
				return;
			}
			shootSounds = revolver.shootSounds;
			shoootSoundVolume = revolver.shoootSoundVolume;
			tracerMaterial = revolver.tracerMaterial;
			tracerWidthMultiplier = revolver.tracerWidthMultiplier;
			tracerLifetime = revolver.tracerLifetime * 2f;
			bulletHitEffect = revolver.bulletHitEffect;
			bulletCollisionSounds = revolver.bulletCollisionSounds;
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

		private void BuildBreech()
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
				Mod.Log.LogWarning("Could not find the rifle body sprite, so there is nowhere to load a cartridge");
				return;
			}
			RefreshBreech();
			Mod.Log.LogInfo($"Breech: {breech} outward={outward} bolt={(bolt != null ? bolt.name : "none")}");
		}

		// The SMG's slide sits where the SMG's receiver is, which is nowhere near the rifle's. The bolt is moved (the whole slide, so its
		// grab area goes with it) to sit on the rifle's receiver, and its rest position is taken afresh from there.
		private void PlaceBolt()
		{
			if (bolt == null || bodySprite == null)
			{
				return;
			}
			SpriteRenderer art = null;
			foreach (SpriteRenderer renderer in bolt.GetComponentsInChildren<SpriteRenderer>(true))
			{
				if (renderer.sprite != null && renderer.sprite.name == "sniper_bolt")
				{
					art = renderer;
					break;
				}
			}
			if (art == null)
			{
				Mod.Log.LogWarning("Could not find the bolt's art, so the bolt stays where the SMG's slide was");
				return;
			}
			Transform t = bodySprite.transform;
			float flipX = bodySprite.flipX ? -1f : 1f;
			float flipY = bodySprite.flipY ? -1f : 1f;
			Vector3 target = t.TransformPoint(new Vector3((BoltColumn - BodyWidth / 2f) / BodyPixelsPerUnit * flipX, (BodyHeight / 2f - BoltRow) / BodyPixelsPerUnit * flipY, 0f));
			Vector3 delta = Vector3.ProjectOnPlane(target - art.bounds.center, t.forward);
			Transform handle = bolt.transform;
			Vector3 world = handle.position + delta;
			Vector3 local = handle.parent != null ? handle.parent.InverseTransformPoint(world) : world;
			bolt.ResetRestPose(local, handle.localRotation);
			Mod.Log.LogInfo($"Moved the bolt by {delta} onto the receiver");
		}

		// The 3D rifle you hold while aiming. The SMG's meshes are hidden and the rifle built from the art goes in their place, as long
		// as the SMG's were, with its muzzle at the gun's fire point.
		private void BuildHeldModel()
		{
			if (aimModel == null || firePoint == null)
			{
				Mod.Log.LogWarning("No aim model or fire point, so the held rifle has no model");
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
				Mod.Log.LogWarning("The SMG's held model has no meshes to measure, so the held rifle has no model");
				return;
			}

			Texture2D body = Mod.Ctx.Sprites.LoadTexture("sniper_body.png");
			if (body == null)
			{
				return;
			}
			Vector3 muzzleLocal = aimModel.InverseTransformPoint(firePoint.position);
			float length = (front - rear) * HeldLengthOverSmg;
			float pixel = length / BodyWidth;
			Mesh mesh = VoxelMesh.Build(body, pixel, BodyWidth * 0.031f * pixel, MuzzleRow, "Sniper rifle");
			GameObject model = VoxelMesh.MakeObject("SniperRifleModel", mesh, body, material, aimModel, layer);
			model.transform.localPosition = muzzleLocal;
			Mod.Log.LogInfo($"Held rifle model: muzzle {muzzleLocal}, length {length} (SMG model {rear} to {front}), layer {LayerMask.LayerToName(layer)}");
		}

		// Where the breech is in the world right now. The body is part of a handle that can move, so this is worked out afresh when it is needed.
		private void RefreshBreech()
		{
			Transform t = bodySprite.transform;
			float flipX = bodySprite.flipX ? -1f : 1f;
			float flipY = bodySprite.flipY ? -1f : 1f;
			float x = (BreechColumn - BodyWidth / 2f) / BodyPixelsPerUnit;
			float y = (BodyHeight / 2f - BreechRow) / BodyPixelsPerUnit;
			breech = t.TransformPoint(new Vector3(x * flipX, y * flipY, 0f));
			// Toward the muzzle, which is the way a cartridge's tip points once it is in.
			outward = (t.TransformPoint(new Vector3(-flipX, 0f, 0f)) - t.position).normalized;
			upward = (t.TransformPoint(new Vector3(0f, flipY, 0f)) - t.position).normalized;
			normal = t.forward;
		}

		/// <summary>True if <paramref name="point"/> is within <paramref name="radius"/> of the breech, measured across the plane the rifle is drawn in.</summary>
		public bool IsNearBreech(Vector3 point, float radius)
		{
			if (bodySprite == null)
			{
				return false;
			}
			RefreshBreech();
			return Vector3.ProjectOnPlane(point - breech, normal).magnitude < radius;
		}

		/// <summary>The poses a cartridge takes on its way in: lined up above the breech, then dropped in.</summary>
		public bool TryPoses(Cartridge cartridge, out Pose align, out Pose insert)
		{
			align = default;
			insert = default;
			float length = cartridge.Length;
			if (bodySprite == null || length <= 0f)
			{
				return false;
			}
			RefreshBreech();

			// The cartridge's own up is its tip; turn that to point along the barrel, about the plane the rack and body sit in.
			Quaternion rest = cartridge.RestRotation;
			Vector3 tip = Vector3.ProjectOnPlane(rest * cartridge.ArtOffset * Vector3.up, normal);
			float angle = Vector3.SignedAngle(tip, Vector3.ProjectOnPlane(outward, normal), normal);
			Quaternion turned = Quaternion.AngleAxis(angle, normal) * rest;

			// The object's origin is not the sprite's middle, so aim the middle and work back to the origin.
			Vector3 middle = Quaternion.Inverse(cartridge.transform.rotation) * cartridge.CentreOffset;

			align.rotation = turned;
			insert.rotation = turned;
			align.centre = AtDepth(breech + upward * (length * 0.5f + 0.8f));
			insert.centre = AtDepth(breech);
			align.artUp = outward;
			insert.artUp = outward;
			align.position = align.centre - turned * middle;
			insert.position = insert.centre - turned * middle;
			return true;
		}

		/// <summary>
		/// Once a cartridge is home, measures where its art really is and nudges it onto the breech. The arithmetic that got it close
		/// cannot know how the scene's sprites are set up; this does not need to.
		/// </summary>
		public void Refine(Cartridge cartridge, Pose target)
		{
			float turn = Vector3.SignedAngle(Vector3.ProjectOnPlane(cartridge.ArtUp, normal), Vector3.ProjectOnPlane(target.artUp, normal), normal);
			cartridge.transform.RotateAround(cartridge.SpriteCentre, normal, turn);
			Vector3 off = target.centre - cartridge.SpriteCentre;
			cartridge.transform.position += Vector3.ProjectOnPlane(off, normal);
		}

		private Vector3 AtDepth(Vector3 point)
		{
			return point - normal * Vector3.Dot(point - bodySprite.transform.position, normal);
		}

		public void Reserve(Cartridge cartridge)
		{
			reserved = cartridge;
		}

		/// <summary>The cartridge is in the open breech. If the bolt was shut over it somehow, it is chambered straight away.</summary>
		public void OnCartridgeSeated(Cartridge cartridge)
		{
			reserved = null;
			SoundController.PlayRandomSound2D(seatSounds, gameObject, 0.8f);
			if (!boltOpen)
			{
				Chamber(cartridge);
				return;
			}
			seatedCartridge = cartridge;
		}

		/// <summary>A cartridge in the breech was pulled back out: the breech is empty again.</summary>
		public void OnCartridgeRemoved(Cartridge cartridge)
		{
			if (reserved == cartridge)
			{
				reserved = null;
			}
			if (seatedCartridge == cartridge)
			{
				seatedCartridge = null;
			}
			SoundController.PlayRandomSound2D(seatSounds, gameObject, 0.4f);
		}

		/// <summary>Grabbing a cartridge in the breech lifts the rifle to aim when it is ready, the same as grabbing the body.</summary>
		public Grabbable CartridgeRedirect()
		{
			return AimRedirect();
		}

		private void Chamber(Cartridge cartridge)
		{
			chambered = true;
			dryClicked = false;
			if (seatedCartridge == cartridge)
			{
				seatedCartridge = null;
			}
			cartridge.Spend();
		}

		private void OnBoltGrabbed()
		{
			holdOpen = false;
		}

		// The bolt is a LinearObject, dragged by hand. Pulled all the way back it opens: the case flies out, and it stays open when let
		// go. Pushed all the way forward again it shuts, and a cartridge waiting in the breech goes into the chamber.
		private void UpdateBolt()
		{
			if (bolt == null)
			{
				return;
			}
			if (!boltOpen && bolt.value >= boltBack)
			{
				boltOpen = true;
				SoundController.PlayRandomSound2D(pullSounds, gameObject, 0.5f);
				if (spentCase)
				{
					spentCase = false;
					if (ejectEffect != null)
					{
						ejectEffect.Play();
					}
				}
				if (Mod.DumpInfo.Value)
				{
					Mod.Log.LogInfo($"Bolt open (value {bolt.value:0.00})");
				}
			}
			else if (boltOpen && bolt.value <= boltForward)
			{
				boltOpen = false;
				SoundController.PlayRandomSound2D(releaseSounds, gameObject, 0.5f);
				if (seatedCartridge != null)
				{
					Chamber(seatedCartridge);
				}
				if (Mod.DumpInfo.Value)
				{
					Mod.Log.LogInfo($"Bolt shut (value {bolt.value:0.00}), chambered={chambered}");
				}
			}

			// An open bolt stays open whenever it is let go, wherever it was let go: the spring runs it back to the stop. (An earlier version
			// held it open only if it was still past the open mark on the very frame it was released, and the game's own spring could have
			// pulled it just under by then, so it would slide shut.) While it is held the spring is off, so it can be pushed home.
			holdOpen = boltOpen && !bolt.IsGrabbed;
			bolt.targetValue = holdOpen ? 1f : 0f;
		}

		protected override bool CanEnterAimMode()
		{
			return chambered && !boltOpen && !Ceasefire;
		}

		protected override bool IsFull()
		{
			return chambered;
		}

		protected override bool PartsAim()
		{
			return chambered && !boltOpen;
		}

		protected override int ShotsLeft()
		{
			return chambered ? 1 : 0;
		}

		// Infinite ammo from the cheat console: there is always a round in the chamber.
		protected override void LoadFully()
		{
			chambered = true;
		}

		protected override void EnterAimMode()
		{
			base.EnterAimMode();
			aimTime = 0f;
			scopeIn = false;
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			zoom = 0f;
			SetCockpitHidden(false);
			Scope.Release(fpvcamera);
			if (laser != null)
			{
				laser.Hide();
			}
		}

		private void SetCockpitHidden(bool hide)
		{
			if (interiorCamera == null)
			{
				GameObject named = GameObject.Find("InteriorCamera");
				interiorCamera = named != null ? named.GetComponent<Camera>() : null;
				if (interiorCamera == null)
				{
					return;
				}
			}
			if (hide && !cockpitHidden && interiorCamera.enabled)
			{
				interiorCamera.enabled = false;
				cockpitHidden = true;
			}
			else if (!hide && cockpitHidden)
			{
				interiorCamera.enabled = true;
				cockpitHidden = false;
			}
		}

		private void OnDestroy()
		{
			SetCockpitHidden(false);
			if (laser != null)
			{
				Destroy(laser.gameObject);
			}
			if (overlay != null)
			{
				Destroy(overlay.gameObject);
			}
		}

		// The game's own crosshair turns itself on every frame while the rifle is raised; once zoomed in, the reticle is the aim point,
		// so it is turned back off after the game has had its say.
		private void LateUpdate()
		{
			if (!ready || !Mod.ShowScope.Value || crosshair == null)
			{
				return;
			}
			if (crosshairSprite == null)
			{
				crosshairSprite = crosshair.GetComponent<SpriteRenderer>();
			}
			if (crosshairSprite != null && Scoped && zoom > 0.5f)
			{
				crosshairSprite.enabled = false;
			}
		}

		protected override void Update()
		{
			if (!ready)
			{
				return;
			}
			UpdateBolt();
			if (aimModel != null)
			{
				aimModel.gameObject.SetActive(grabbed);
			}
			if (!GameInput.Hand(1))
			{
				dryClicked = false;
			}
			if (grabbed && GameInput.KeyDown(KeyCode.Z))
			{
				// Scoping in starts the charge from nothing, and so does putting the scope away and bringing it back.
				scopeIn = !scopeIn;
				aimTime = 0f;
			}
			aimTime = Scoped ? aimTime + InteriorMath.DeltaTime : 0f;
			UpdateScope();
			base.Update();
		}

		// The zoom follows the rifle being raised, and the red dot sits where it points.
		private void UpdateScope()
		{
			float target = Scoped ? 1f : 0f;
			zoom = Mathf.Lerp(zoom, target, InteriorMath.SmoothFactor(Mod.ZoomSpeed.Value, Time.deltaTime));
			if (fpvcamera != null)
			{
				Scope.Set(fpvcamera, zoom);
			}
			// The cockpit (wheel, shifter, dash) is drawn by the interior camera, over the world, so a black surround cannot cover it
			// inside the lens. While scoped in it is switched off, the way the game does itself when the camera is let loose.
			SetCockpitHidden(Mod.ShowScope.Value && Scoped && zoom > 0.6f);
			if (laser == null)
			{
				return;
			}
			if (!Mod.LaserDot.Value || !grabbed || fpvcamera == null)
			{
				laser.Hide();
				return;
			}
			Ray ray = fpvcamera.ScreenPointToRay(AimScreenPoint);
			if (Physics.Raycast(ray, out RaycastHit hit, 1000f, hitLayers))
			{
				laser.Show(hit.point, hit.collider.gameObject.layer, fpvcamera, Charge);
			}
			else
			{
				laser.Hide();
			}
		}

		public override void Shoot()
		{
			if (Time.time < nextShot)
			{
				return;
			}
			if (!chambered && !Endless)
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
				chambered = false;
				spentCase = true;
			}
			// FireRay reads ShotDamage as the shot leaves, so the charge is spent only after.
			FireShot();
			aimTime = 0f;
			if (Mod.LowerAfterShot.Value)
			{
				StartCoroutine(Lower());
			}
		}

		// The rifle is lowered so the bolt can be worked.
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
	}
}
