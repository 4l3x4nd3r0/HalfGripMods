using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HalfGripRailgun
{
	/// <summary>
	/// What makes the pump shotgun a railgun. While the gun is raised with a ball chambered it charges six capacitors in
	/// <c>ChargeSeconds</c> (two) at full speed and <c>StillChargeSeconds</c> (five) standing still, one after another; lowered, they drain at a share of the fastest rate. Only a full capacitor counts: the
	/// shot's damage is the base plus a sixth of the way to the maximum for each. The coils light up with them (the one filling from
	/// the bottom up, slowly at first and in a rush at the end), the gun whines higher as it builds, and a bar of six cells shows
	/// under the crosshair.
	/// </summary>
	internal class RailCharge : MonoBehaviour
	{
		private static readonly Color BarColour = new Color(0.45f, 0.78f, 1f, 1f);

		private static readonly System.Reflection.MethodInfo AimPoint = AccessTools.PropertyGetter(typeof(Gun), "AimScreenPoint");

		// The slug shotgun's sprites, read from the game's race scene, and the railgun art that stands in for each. Every file is the
		// size of the sprite it replaces. The "cover" layer is drawn over the pump and stays put with the body: the lit coils and the arcs
		// are sprites parented to it (see CockpitFx), so it is replaced by a clear picture.
		private static readonly Dictionary<string, string> Reskin = new Dictionary<string, string>
		{
			{ "IMG_3632_0", "railgun_body.png" },
			{ "IMG_3632_under_0", "railgun_clear.png" },
			{ "IMG_3632_cover", "railgun_clear.png" },
			{ "IMG_3633_0", "railgun_pump.png" },
			{ "Shell", "railgun_ball.png" }
		};

		// Shells the car is given later are copies of the game's own, so they are swapped as they turn up.
		private static readonly Dictionary<string, string> Balls = new Dictionary<string, string> { { "Shell", "railgun_ball.png" } };

		private static AudioClip whine;

		private NPumpActionGun gun;

		private Rigidbody carBody;

		private AudioSource voice;

		private SpriteRenderer glow;

		private SpriteRenderer pumpSprite;

		private SpriteRenderer bodySprite;

		private AudioSource sfx;

		private CockpitFx cockpit;

		private RailSparks sparks;

		private RailgunArt.HeldBody held;

		private int arcVariant;

		private float nextArc;

		private float prevPump;

		private float needleKick;

		private Material originalTracer;

		private Material beam;

		private float originalWidth;

		private float originalLife;

		private float carSearch;

		private float ballTimer;

		/// <summary>Capacitors charged, 0..6: the whole number is the level, the rest is the one filling.</summary>
		public float Capacitors { get; private set; }

		public int Level => Mathf.Min(RailLayout.Coils, (int)(Capacitors + 0.0001f));

		/// <summary>The charge as 0..1, smooth (the gauge needle, the whine).</summary>
		public float Charge => Capacitors / RailLayout.Coils;

		/// <summary>The charge as 0..1 in whole capacitors (the beam, the shot's sound and sparks).</summary>
		public float Power => Level / (float)RailLayout.Coils;

		/// <summary>Shots loaded: the one in the chamber and the balls in the tube.</summary>
		public int Shots => Mathf.Max(0, gun.bullets) + (gun.loaded ? 1 : 0);

		public float ShotDamage => Mod.MinDamage.Value + Mod.PerCapacitor * Level;

		public void Setup(NPumpActionGun owner)
		{
			gun = owner;
			carBody = FindCarBody();
			// Before the sprites are swapped, find the layer that will carry the glow.
			foreach (SpriteRenderer sprite in owner.GetComponentsInChildren<SpriteRenderer>(true))
			{
				if (sprite.sprite != null && sprite.sprite.name == "IMG_3632_cover")
				{
					glow = sprite;
					break;
				}
			}
			Mod.Log.LogInfo("Railgun: swapped " + ReskinSprites() + " sprites at " + Mod.GunScale.Value + " of the shotgun's size, cover layer " + (glow != null ? "found" : "MISSING"));
			QuietCasing(owner);
			held = RailgunArt.ReskinHeldModel(owner);
			cockpit = new CockpitFx(bodySprite, pumpSprite);
			sfx = gameObject.AddComponent<AudioSource>();
			sfx.spatialBlend = 0f;
			sfx.playOnAwake = false;
			sparks = RailSparks.On(gameObject);
			prevPump = owner.pump != null ? owner.pump.value : 0f;

			originalTracer = owner.tracerMaterial;
			originalWidth = owner.tracerWidthMultiplier;
			originalLife = owner.tracerLifetime;

			voice = gameObject.AddComponent<AudioSource>();
			voice.clip = WhineClip();
			voice.loop = true;
			voice.spatialBlend = 0f;
			voice.volume = 0f;
			voice.playOnAwake = false;

			if (Mod.DumpInfo.Value)
			{
				Mod.Log.LogInfo($"Railgun set up on '{owner.name}': capacity={owner.capacity}, bullets={owner.bullets}, tracer={(originalTracer ? originalTracer.name : "none")}, car body={(carBody ? "found" : "not yet")}");
			}
		}

		// The cockpit sprites are swapped one by one so the gun's parts can be drawn smaller: a higher pixels-per-unit makes a sprite
		// smaller about its middle, and the body, its layers and the pump all shrink together, so they still line up.
		private int ReskinSprites()
		{
			float scale = Mathf.Clamp(Mod.GunScale.Value, 0.4f, 1.5f);
			int swapped = 0;
			foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				Sprite current = renderer.sprite;
				if (current == null || !Reskin.TryGetValue(current.name, out string file))
				{
					continue;
				}
				float ppu = file == "railgun_ball.png" || Mathf.Abs(scale - 1f) < 0.01f ? 0f : current.pixelsPerUnit / scale;
				Sprite replacement = Mod.Ctx.Sprites.LoadSprite(file, current, ppu);
				if (replacement != null)
				{
					renderer.sprite = replacement;
					if (current.name == "IMG_3632_0" && (bodySprite == null || renderer.GetComponentInParent<NPumpActionGun>() == gun))
					{
						bodySprite = renderer;
					}
					if (current.name == "IMG_3633_0" && (pumpSprite == null || renderer.GetComponentInParent<NPumpActionGun>() == gun))
					{
						pumpSprite = renderer;
					}
					swapped++;
				}
			}
			return swapped;
		}

		// A railgun throws no shotgun shells: no casing flies out when it is pumped, and no case clatters on the floor afterwards.
		private static void QuietCasing(NPumpActionGun owner)
		{
			if (owner.spentCasing != null)
			{
				owner.spentCasing.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
				owner.spentCasing.gameObject.SetActive(false);
				owner.spentCasing = null;
			}
			owner.ejectShellSounds = new AudioClip[0];
		}

		// The car the gun rides in: the link the gun was given, or else the player's car in the scene.
		private Rigidbody FindCarBody()
		{
			if (gun != null && gun.carLink != null && gun.carLink.cc != null && gun.carLink.cc.rb != null)
			{
				return gun.carLink.cc.rb;
			}
			foreach (CarControllerNew car in Object.FindObjectsByType<CarControllerNew>(FindObjectsSortMode.None))
			{
				if (car.isPlayer && car.rb != null)
				{
					return car.rb;
				}
			}
			return null;
		}

		private void Update()
		{
			if (gun == null)
			{
				return;
			}
			float dt = Time.deltaTime;
			// The fastest the capacitors fill, a second: all six in ChargeSeconds (the drain is measured against it).
			float rate = RailLayout.Coils / Mathf.Max(0.2f, Mod.ChargeSeconds.Value);
			if (carBody == null)
			{
				carSearch -= dt;
				if (carSearch <= 0f)
				{
					carSearch = 1f;
					carBody = FindCarBody();
				}
			}
			if (gun.grabbed)
			{
				// Raised with nothing chambered (just fired, not pumped yet) the gun cannot shoot, so it does not charge either.
				if (gun.loaded)
				{
					// Standing still it takes StillChargeSeconds for all six; at FullSpeed, ChargeSeconds; in between, by speed.
					float speed = carBody != null ? carBody.linearVelocity.magnitude : 0f;
					float share = Mathf.Clamp01(speed / Mathf.Max(1f, Mod.FullSpeed.Value));
					float slow = RailLayout.Coils / Mathf.Max(Mod.ChargeSeconds.Value, Mod.StillChargeSeconds.Value);
					Capacitors = Mathf.Min(RailLayout.Coils, Capacitors + Mathf.Lerp(slow, rate, share) * dt);
				}
			}
			else if (Capacitors > 0f)
			{
				Capacitors = Mathf.Max(0f, Capacitors - Mod.DrainShare.Value * rate * dt);
			}

			Pump(dt);
			Glow();
			Whine();

			ballTimer -= dt;
			if (ballTimer <= 0f)
			{
				ballTimer = 1f;
				Mod.Ctx.Sprites.Reskin(Balls);
			}
		}

		// The pump: its gauge needle jumps on each stroke and the air hisses on the way back.
		private void Pump(float dt)
		{
			needleKick = Mathf.MoveTowards(needleKick, 0f, 90f * dt);
			if (gun.pump == null)
			{
				return;
			}
			float value = gun.pump.value;
			if (prevPump < 0.5f && value >= 0.5f)
			{
				needleKick = -30f;
			}
			else if (prevPump >= 0.5f && value < 0.5f)
			{
				needleKick = 25f;
				sfx.PlayOneShot(RailAudio.Hiss, 0.28f);
			}
			prevPump = value;
		}

		// The coils light one by one with the charge, arcs flicker between the lit spark plugs, and the needle follows.
		private void Glow()
		{
			if (Level >= 2)
			{
				if (Time.time >= nextArc)
				{
					nextArc = Time.time + Random.Range(0.06f, 0.14f);
					arcVariant = Random.value < 0.3f ? 0 : Random.Range(1, 4);
					if (arcVariant > 0 && Random.value < 0.5f)
					{
						sfx.PlayOneShot(RailAudio.Tick, Mathf.Lerp(0.05f, 0.18f, Power));
					}
				}
			}
			else
			{
				arcVariant = 0;
			}
			if (held != null)
			{
				held.Show(Capacitors, arcVariant, gun.loaded, Shots);
			}
			if (cockpit != null)
			{
				cockpit.Show(Capacitors, arcVariant, needleKick, gun.loaded, Shots);
			}
		}

		private void Whine()
		{
			if (voice == null)
			{
				return;
			}
			bool audible = Charge > 0.02f;
			voice.volume = audible ? Mathf.Lerp(0.04f, 0.22f, Charge) : 0f;
			voice.pitch = Mathf.Lerp(0.6f, 2.4f, Charge);
			if (audible && !voice.isPlaying)
			{
				voice.Play();
			}
			else if (!audible && voice.isPlaying)
			{
				voice.Stop();
			}
		}

		/// <summary>Called as the shot leaves: a blue-white beam, thicker and brighter with the charge.</summary>
		public void ShapeBeam()
		{
			if (originalTracer == null)
			{
				return;
			}
			if (beam == null)
			{
				beam = new Material(originalTracer) { name = "railgun beam", hideFlags = HideFlags.HideAndDontSave };
			}
			beam.color = Color.Lerp(new Color(0.35f, 0.62f, 1f, 1f), new Color(0.85f, 0.95f, 1f, 1f), Power);
			gun.tracerMaterial = beam;
			gun.tracerWidthMultiplier = Mathf.Lerp(2.5f, 7f, Power);
			gun.tracerLifetime = 0.4f;
			Fire();
		}

		// The shot: a crack and a zap, louder with the charge, and a burst of sparks at the muzzle of the held gun.
		private void Fire()
		{
			try
			{
				sfx.PlayOneShot(RailAudio.Zap, Mathf.Lerp(0.3f, 0.95f, Power));
				if (held != null && held.Renderer != null && gun.firePoint != null)
				{
					Bounds box = held.Renderer.bounds;
					float length = Mathf.Max(box.size.x, box.size.y, box.size.z);
					sparks.Burst(gun.firePoint.position, gun.AimForward, length, held.Renderer.gameObject.layer, Power);
				}
			}
			catch (System.Exception e)
			{
				Mod.Log.LogWarning("Railgun shot effects: " + e.Message);
			}
		}

		/// <summary>The shot is gone: the charge goes with it.</summary>
		public void Spend()
		{
			Capacitors = 0f;
		}

		/// <summary>Puts back what the component changed on the game's gun.</summary>
		public void Restore()
		{
			if (cockpit != null)
			{
				cockpit.Destroy();
				cockpit = null;
			}
			if (sparks != null)
			{
				Object.Destroy(sparks);
			}
			if (gun != null)
			{
				gun.tracerMaterial = originalTracer;
				gun.tracerWidthMultiplier = originalWidth;
				gun.tracerLifetime = originalLife;
			}
			if (voice != null)
			{
				voice.Stop();
			}
		}

		private void OnDisable()
		{
			if (voice != null)
			{
				voice.Stop();
			}
		}

		private void OnGUI()
		{
			if (!Mod.ScreenBar.Value || gun == null || !gun.grabbed || AimPoint == null)
			{
				return;
			}
			Vector2 point = (Vector2)AimPoint.Invoke(gun, null);
			float cx = point.x;
			float cy = Screen.height - point.y;
			float width = Screen.height * 0.12f;
			float height = Mathf.Max(3f, Screen.height * 0.006f);
			float y = cy + Screen.height * 0.05f;
			// Six cells, one per capacitor: a full one is lit, the one filling grows slowly and then jumps, the rest are dark.
			float gap = Mathf.Max(2f, width * 0.035f);
			float cell = (width - gap * 5f) / 6f;
			GUI.color = new Color(0f, 0f, 0f, 0.7f);
			GUI.DrawTexture(new Rect(cx - width * 0.5f - 2f, y - 2f, width + 4f, height + 4f), Texture2D.whiteTexture);
			for (int i = 0; i < RailLayout.Coils; i++)
			{
				float x = cx - width * 0.5f + i * (cell + gap);
				GUI.color = new Color(0.12f, 0.13f, 0.16f, 1f);
				GUI.DrawTexture(new Rect(x, y, cell, height), Texture2D.whiteTexture);
				float fill = i < Level ? 1f : i == Level ? RailPixels.Ease(Capacitors - Level) : 0f;
				if (fill > 0f)
				{
					GUI.color = i < Level ? Color.Lerp(BarColour, Color.white, 0.5f) : BarColour;
					GUI.DrawTexture(new Rect(x, y, cell * fill, height), Texture2D.whiteTexture);
				}
			}
			GUI.color = Color.white;
		}

		// One second of a rising tone built from whole cycles, so it loops without a click; the pitch is moved by the AudioSource.
		private static AudioClip WhineClip()
		{
			if (whine == null)
			{
				const int rate = 44100;
				var data = new float[rate];
				for (int i = 0; i < rate; i++)
				{
					float t = i / (float)rate;
					float s = Mathf.Sin(6.2831853f * 220f * t) + 0.45f * Mathf.Sin(6.2831853f * 440f * t) + 0.25f * Mathf.Sin(6.2831853f * 660f * t);
					data[i] = s * 0.35f * (0.85f + 0.15f * Mathf.Sin(6.2831853f * 8f * t));
				}
				whine = AudioClip.Create("railgun whine", rate, 1, rate, false);
				whine.SetData(data, 0);
				whine.hideFlags = HideFlags.HideAndDontSave;
			}
			return whine;
		}
	}
}
