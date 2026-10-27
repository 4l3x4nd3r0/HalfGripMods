using HarmonyLib;
using UnityEngine;

namespace HalfGripMinigun
{
	/// <summary>
	/// The Minigun's trigger, its heat and its lock. Each bullet adds heat; the meter holds (item magazines + Extra Magazines) x
	/// SecondsPerMagazine of fire. It cools by itself when nobody is firing (CoolSeconds from full to zero), an ammo box takes a
	/// magazine's worth off at once, and a meter that fills locks the remote for a long penalty (longer for each repeat in a race):
	/// it cannot be grabbed, raised or fired until the lock has run out, and a box shortens the lock. The lock shows as a padlock on
	/// the remote, the same way the game locks a gun during the opening ceasefire.
	/// </summary>
	internal class MiniHeat : MonoBehaviour
	{
		// A burst is "one trigger pull" while the game keeps calling Shoot at least this often.
		private const float PullGap = 0.15f;

		// How soon after the last bullet the gun starts to cool.
		private const float CoolDelay = 0.1f;

		private static readonly System.Reflection.MethodInfo FireShot = AccessTools.Method(typeof(Gun), "FireShot");

		private static readonly System.Reflection.MethodInfo ExitAimMode = AccessTools.Method(typeof(Gun), "ExitAimMode");


		private static AudioClip whir;


		private NAutomaticGun gun;

		private TrackManager race;

		private AudioSource voice;

		private RoofGun roof;

		private Transform originalFirePoint;

		private bool originalRecoilAtFirePoint;

		private SpriteRenderer padlock;

		private float heat;

		private float lockRemaining;

		private float lockTotal;

		private int overheats;

		private bool wasLocked;

		private float lastPull = -10f;

		private float spinStart;

		private float nextShot;

		private float lastBullet = -10f;

		private float spin;

		public static MiniHeat Current { get; private set; }

		/// <summary>The meter in seconds of fire.</summary>
		public float Capacity
		{
			get
			{
				GunUpgrade item = MinigunItem.Item;
				int mags = (item != null ? Mathf.Max(1, item.magazines) : Mod.Magazines.Value) + Mathf.Max(0, Mathf.RoundToInt(Stats.Get(Stat.ExtraMagazines, 0f)));
				return Mathf.Max(0.1f, mags * Mod.SecondsPerMagazine.Value);
			}
		}

		/// <summary>How full the meter is, 0 to 1.</summary>
		public float Fraction => Mathf.Clamp01(heat / Capacity);

		/// <summary>True while the remote is locked after an overheat.</summary>
		public bool Jammed => lockRemaining > 0f;

		/// <summary>How much of the lock is left, 1 to 0.</summary>
		public float LockFraction => lockTotal > 0f ? Mathf.Clamp01(lockRemaining / lockTotal) : 0f;

		public bool CanAim => !Jammed && !Ceasefire;

		/// <summary>How fast the barrels are turning, 0 to 1.</summary>
		public float Spin => spin;

		private bool Ceasefire => race != null && race.InOpeningCeasefire;

		public void Setup(NAutomaticGun owner)
		{
			gun = owner;
			Current = this;
			race = Object.FindFirstObjectByType<TrackManager>();

			voice = gameObject.AddComponent<AudioSource>();
			voice.clip = WhirClip();
			voice.loop = true;
			voice.spatialBlend = 0f;
			voice.volume = 0f;
			voice.playOnAwake = false;

			if (Mod.RoofGunVisible.Value)
			{
				roof = RoofGun.Build(owner, this);
				if (roof != null && roof.Muzzle != null)
				{
					// The shot's tracer, flash and recoil start at the gun's firing point: make that the roof gun's muzzle, so the bullets
					// leave from above the car and not from the remote, and the car is not tipped over by a recoil applied up there.
					originalFirePoint = owner.firePoint;
					originalRecoilAtFirePoint = owner.carRecoilAtFirePoint;
					owner.firePoint = roof.Muzzle;
					owner.carRecoilAtFirePoint = false;
				}
			}
			Mod.Log.LogInfo($"Minigun heat meter: {Capacity} s of fire, cools in {Mod.CoolSeconds.Value} s, roof gun {(roof != null ? "built" : "not built")}");
		}

		/// <summary>The game calls this every frame the trigger is held (and once when it is first pressed).</summary>
		public void Pull()
		{
			float now = Time.time;
			if (now - lastPull > PullGap)
			{
				spinStart = now;
			}
			lastPull = now;
			if (Jammed || Ceasefire || FireShot == null)
			{
				return;
			}
			if (now - spinStart < Mathf.Max(0.01f, Mod.SpinUp.Value) || now < nextShot)
			{
				return;
			}
			float interval = 1f / Mathf.Max(1f, Mod.RoundsPerSecond.Value);
			// Keep the average rate even when a frame is longer than a bullet's interval.
			nextShot = Mathf.Max(nextShot + interval, now - interval);
			FireShot.Invoke(gun, null);
			lastBullet = now;
			heat += interval;
			if (heat >= Capacity)
			{
				Overheat();
			}
		}

		private void Overheat()
		{
			heat = Capacity;
			overheats++;
			lockTotal = Mathf.Max(1f, Mod.OverheatPenalty.Value + Mod.RepeatPenalty.Value * (overheats - 1));
			lockRemaining = lockTotal;
			TextController.EnqueueRandomText(2f, "OVERHEATED: LOCKED " + Mathf.RoundToInt(lockTotal) + " s");
			Mod.Log.LogInfo($"Minigun overheated (number {overheats} this race): locked for {lockTotal} s");
		}

		/// <summary>
		/// An ammo box: takes <paramref name="magazines"/> magazines' worth of heat off and shortens a lock that is running. False if
		/// there was nothing to take off (the gun is cold and unlocked).
		/// </summary>
		public bool CoolByMagazines(int magazines)
		{
			bool helped = false;
			if (heat > 0.001f)
			{
				heat = Mathf.Max(0f, heat - magazines * Mod.SecondsPerMagazine.Value);
				helped = true;
			}
			if (Jammed)
			{
				lockRemaining = Mathf.Max(0f, lockRemaining - Mod.BoxLockRelief.Value * magazines);
				helped = true;
			}
			return helped;
		}

		private void Update()
		{
			float now = Time.time;
			float dt = Time.deltaTime;
			if (heat > 0f && now - lastBullet > CoolDelay)
			{
				float baseSeconds = Mathf.Max(1, MinigunItem.Item != null ? MinigunItem.Item.magazines : Mod.Magazines.Value) * Mod.SecondsPerMagazine.Value;
				heat = Mathf.Max(0f, heat - baseSeconds / Mathf.Max(0.1f, Mod.CoolSeconds.Value) * dt);
			}
			if (lockRemaining > 0f)
			{
				lockRemaining = Mathf.Max(0f, lockRemaining - dt);
			}

			// The remote cannot be picked up while it is locked, by the overheat or by the opening ceasefire (the game's own padlock
			// rides on it for that one), and it drops out of your hand if you were holding it.
			bool locked = Jammed || Ceasefire;
			if (locked != wasLocked)
			{
				wasLocked = locked;
				SetGrabbable(!locked);
				if (locked && gun.grabbed && ExitAimMode != null)
				{
					ExitAimMode.Invoke(gun, null);
				}
			}
			ShowPadlock(Jammed);

			// The barrels spin up while the trigger is held (and the gun is not locked), and run down again after.
			bool held = gun != null && gun.grabbed && now - lastPull < PullGap && !Jammed;
			float target = held ? Mathf.Clamp01((now - spinStart) / Mathf.Max(0.01f, Mod.SpinUp.Value)) : 0f;
			spin = Mathf.MoveTowards(spin, target, dt * (held ? 1f / Mathf.Max(0.01f, Mod.SpinUp.Value) : 1.2f));
			Whir();
		}

		private void SetGrabbable(bool grabbable)
		{
			if (gun == null)
			{
				return;
			}
			if (gun.body != null)
			{
				gun.body.canBeGrabbed = grabbable;
			}
			if (gun.slide != null)
			{
				gun.slide.canBeGrabbed = grabbable;
			}
		}

		// A padlock on the middle of the remote, in front of it, drawn at the remote's own pixel size.
		private void ShowPadlock(bool show)
		{
			if (padlock == null)
			{
				if (!show || gun == null || gun.body == null)
				{
					return;
				}
				SpriteRenderer remote = null;
				foreach (SpriteRenderer sprite in gun.body.GetComponentsInChildren<SpriteRenderer>(true))
				{
					if (sprite.sprite != null && sprite.sprite.name == "minigun_body")
					{
						remote = sprite;
						break;
					}
				}
				Texture2D texture = Mod.Ctx.Sprites.LoadTexture("minigun_lock.png");
				if (remote == null || texture == null)
				{
					return;
				}
				var go = new GameObject("MinigunLock");
				go.layer = remote.gameObject.layer;
				go.transform.SetParent(remote.transform, false);
				Vector3 toCamera = Vector3.back;
				GameObject eye = GameObject.Find("InteriorCamera");
				if (eye != null)
				{
					toCamera = (eye.transform.position - remote.bounds.center).normalized;
				}
				go.transform.position = remote.bounds.center + toCamera * 0.1f;
				go.transform.rotation = remote.transform.rotation;
				padlock = go.AddComponent<SpriteRenderer>();
				padlock.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), remote.sprite.pixelsPerUnit);
				padlock.sortingLayerID = remote.sortingLayerID;
				padlock.sortingOrder = remote.sortingOrder + 5;
			}
			if (padlock.enabled != show)
			{
				padlock.enabled = show;
			}
		}

		private void Whir()
		{
			if (voice == null)
			{
				return;
			}
			bool audible = spin > 0.02f;
			voice.volume = audible ? Mathf.Lerp(0.03f, 0.2f, spin) : 0f;
			voice.pitch = Mathf.Lerp(0.5f, 1.8f, spin);
			if (audible && !voice.isPlaying)
			{
				voice.Play();
			}
			else if (!audible && voice.isPlaying)
			{
				voice.Stop();
			}
		}

		public void Cleanup()
		{
			if (voice != null)
			{
				voice.Stop();
			}
			if (gun != null)
			{
				if (originalFirePoint != null)
				{
					gun.firePoint = originalFirePoint;
				}
				gun.carRecoilAtFirePoint = originalRecoilAtFirePoint;
				SetGrabbable(true);
			}
			if (padlock != null)
			{
				Destroy(padlock.gameObject);
			}
			if (roof != null)
			{
				Destroy(roof.gameObject);
			}
			if (Current == this)
			{
				Current = null;
			}
		}

		private void OnDestroy()
		{
			if (Current == this)
			{
				Current = null;
			}
		}

		// A low hum of whole cycles with a little modulation, so it loops without a click; the pitch is moved by the AudioSource.
		private static AudioClip WhirClip()
		{
			if (whir == null)
			{
				const int rate = 44100;
				var data = new float[rate];
				for (int i = 0; i < rate; i++)
				{
					float t = i / (float)rate;
					float saw = 2f * (t * 90f - Mathf.Floor(t * 90f + 0.5f));
					float s = 0.6f * saw + 0.5f * Mathf.Sin(6.2831853f * 180f * t) + 0.2f * Mathf.Sin(6.2831853f * 540f * t);
					data[i] = s * 0.3f * (0.8f + 0.2f * Mathf.Sin(6.2831853f * 24f * t));
				}
				whir = AudioClip.Create("minigun whir", rate, 1, rate, false);
				whir.SetData(data, 0);
				whir.hideFlags = HideFlags.HideAndDontSave;
			}
			return whir;
		}
	}
}
