using UnityEngine;

namespace HalfGripSniper
{
	/// <summary>
	/// A cartridge on the rack that you can pick up and load. It is an ordinary grabbable that falls back to the rack when dropped
	/// (the rack handles that); this adds the loading itself: bring it to the open breech and it turns to lie along the barrel and
	/// drops in. Pull it back out and it is a cartridge again. Shutting the bolt chambers it (see <see cref="SniperGun"/>).
	/// </summary>
	internal class Cartridge : NAmmo
	{
		public SpriteRenderer loadedSprite;

		public SpriteRenderer emptySprite;

		public AudioClip[] insertSounds;

		// Seconds to swing round and line up above the breech, then to drop in.
		private const float AlignDuration = 0.14f;

		private const float InsertDuration = 0.25f;

		// How far in front of the rifle's body it is drawn while in the breech, in game units, along the line of sight. Draw order alone
		// is not always enough in this scene (the RPG found the same).
		private const float PartGap = 0.1f;

		private SniperGun rifle;

		private float nextSearch;

		private bool inserting;

		private bool seated;

		private bool wasOverZone;

		private float elapsed;

		private Vector3 fromPosition;

		private Quaternion fromRotation;

		// Where it sits relative to the rifle's body once home. The body is part of a moving handle and the cartridge hangs on the rack,
		// so left alone the cartridge would be left behind whenever the body shifts; it is carried along instead.
		private Vector3 homeLocalPosition;

		private Quaternion homeLocalRotation;

		private SniperGun.Pose align;

		private SniperGun.Pose insert;

		// A cartridge on the rack is far too long to lie on a receiver, so while it is in or going into the breech its sprite shrinks.
		private const float BreechSize = 0.62f;

		private Vector3 spriteRestPosition;

		private Vector3 spriteRestScale;

		private int spriteRestOrder;

		private bool restKnown;

		public override bool busy => seated || inserting;

		public override bool spent => false;

		private SniperGun Rifle
		{
			get
			{
				if (rifle == null && Time.time >= nextSearch)
				{
					rifle = GetComponentInParent<SniperGun>(true);
					if (rifle == null)
					{
						// The rack is not necessarily under the gun; look for it in the scene, but not every frame.
						rifle = FindFirstObjectByType<SniperGun>(FindObjectsInactive.Include);
						nextSearch = Time.time + 1f;
					}
				}
				return rifle;
			}
		}

		/// <summary>How long the cartridge is, in world units, standing up.</summary>
		public float Length
		{
			get
			{
				SpriteRenderer sprite = Sprite;
				return sprite != null && sprite.sprite != null ? sprite.sprite.bounds.size.y * sprite.transform.lossyScale.y : 0f;
			}
		}

		/// <summary>Where the sprite's middle is, as an offset from the object's origin, in world units at the object's current rotation.</summary>
		public Vector3 CentreOffset
		{
			get
			{
				SpriteRenderer sprite = Sprite;
				if (sprite == null || sprite.sprite == null)
				{
					return Vector3.zero;
				}
				Vector3 world = sprite.transform.TransformPoint(sprite.sprite.bounds.center);
				return world - transform.position;
			}
		}

		/// <summary>How the art is turned relative to the object it hangs on.</summary>
		public Quaternion ArtOffset => Sprite != null ? Quaternion.Inverse(transform.rotation) * Sprite.transform.rotation : Quaternion.identity;

		/// <summary>Where the middle of the art is in the world.</summary>
		public Vector3 SpriteCentre => Sprite != null && Sprite.sprite != null ? Sprite.transform.TransformPoint(Sprite.sprite.bounds.center) : transform.position;

		/// <summary>The way the art's tip points in the world.</summary>
		public Vector3 ArtUp => Sprite != null ? Sprite.transform.rotation * Vector3.up : transform.up;

		/// <summary>The rotation it has when it sits upright on the rack, tip up, whatever it is being tilted to now.</summary>
		public Quaternion RestRotation => transform.parent ? transform.parent.rotation * baseLocalRotation : baseLocalRotation;

		private SpriteRenderer Sprite => loadedSprite != null ? loadedSprite : emptySprite;

		protected override void Update()
		{
			SniperGun gun = Rifle;
			RefreshSprites(gun);
			if (inserting)
			{
				StepInsert(gun);
				return;
			}
			base.Update();
			if (seated && !stuck)
			{
				// Pulled back out of the breech.
				seated = false;
				grabRedirect = null;
				if (gun != null)
				{
					gun.OnCartridgeRemoved(this);
				}
			}
			if (seated || !FellOutOfTheWorld())
			{
				TryInsert(gun);
			}
		}

		// After the rack and the grab physics have had their say, put a loaded cartridge back exactly where it sits in the breech.
		private void LateUpdate()
		{
			SniperGun gun = Rifle;
			if (gun != null && seated && !IsGrabbed)
			{
				Transform carrier = gun.BodyTransform;
				if (carrier != null)
				{
					transform.SetPositionAndRotation(carrier.TransformPoint(homeLocalPosition), carrier.rotation * homeLocalRotation);
					// Keep the grab physics in step with where it was put, so that pulling it out starts from here and not from where it was.
					AdoptPoseFromTransform();
					Stick();
				}
			}
			PlaceSprite(gun);
		}

		private void RefreshSprites(SniperGun gun)
		{
			if (emptySprite != null)
			{
				emptySprite.enabled = false;
			}
			if (loadedSprite == null)
			{
				return;
			}
			// While you aim, the rifle's own model stands in for everything, so the flat cartridge is hidden.
			// Once it is in the breech it is inside the rifle, so it is not drawn on top of it any more.
			loadedSprite.enabled = !seated;
		}

		// While it is in or going into the breech, the cartridge is drawn over the rifle: above it in draw order, and a little nearer the
		// view along the line of sight, where it does not move on screen.
		private void PlaceSprite(SniperGun gun)
		{
			SpriteRenderer whole = loadedSprite;
			if (whole == null)
			{
				return;
			}
			if (!restKnown)
			{
				restKnown = true;
				spriteRestPosition = whole.transform.localPosition;
				spriteRestScale = whole.transform.localScale;
				spriteRestOrder = whole.sortingOrder;
			}
			bool inBreech = (seated || inserting) && gun != null && gun.BodyRenderer != null;
			if (!inBreech)
			{
				whole.transform.localPosition = spriteRestPosition;
				whole.transform.localScale = spriteRestScale;
				whole.sortingOrder = spriteRestOrder;
				return;
			}
			SpriteRenderer body = gun.BodyRenderer;
			whole.sortingLayerID = body.sortingLayerID;
			whole.sortingOrder = body.sortingOrder + 1;
			whole.transform.localPosition = spriteRestPosition;
			float going = seated ? 1f : Progress(elapsed, AlignDuration + InsertDuration);
			whole.transform.localScale = spriteRestScale * Mathf.Lerp(1f, BreechSize, going);
			Vector3 ray = (whole.transform.position - gun.ViewPosition).normalized * PartGap;
			whole.transform.position -= ray;
		}

		private void TryInsert(SniperGun gun)
		{
			bool over = gun != null && gun.IsNearBreech(SpriteCentre, Mathf.Max(1.5f, Length * 0.8f));
			bool entered = over && !wasOverZone;
			wasOverZone = over;
			if (!entered || seated || gun == null || !gun.CanAccept)
			{
				return;
			}
			if (!gun.TryPoses(this, out align, out insert))
			{
				return;
			}
			gun.Reserve(this);
			inserting = true;
			elapsed = 0f;
			fromPosition = transform.position;
			fromRotation = transform.rotation;
			Release();
			SoundController.PlayRandomSound2D(insertSounds, gameObject, 0.5f);
		}

		private void StepInsert(SniperGun gun)
		{
			elapsed += Time.deltaTime;
			float swing = Progress(elapsed, AlignDuration);
			Place(fromPosition, fromRotation, align, swing);
			float drop = 1f;
			if (swing >= 1f)
			{
				drop = Progress(elapsed - AlignDuration, InsertDuration);
				Place(align.position, align.rotation, insert, drop);
			}
			if (swing < 1f || drop < 1f)
			{
				return;
			}
			if (gun != null)
			{
				gun.Refine(this, insert);
			}
			Transform carrier = gun != null ? gun.BodyTransform : null;
			if (carrier != null)
			{
				homeLocalPosition = carrier.InverseTransformPoint(transform.position);
				homeLocalRotation = Quaternion.Inverse(carrier.rotation) * transform.rotation;
			}
			AdoptPoseFromTransform();
			inserting = false;
			seated = true;
			canBeGrabbed = false;
			Stick();
			if (gun != null)
			{
				grabRedirect = gun.CartridgeRedirect;
				gun.OnCartridgeSeated(this);
			}
		}

		private void Place(Vector3 fromPos, Quaternion fromRot, SniperGun.Pose to, float t)
		{
			float eased = Mathf.SmoothStep(0f, 1f, t);
			transform.position = Vector3.Lerp(fromPos, to.position, eased);
			transform.rotation = Quaternion.Slerp(fromRot, to.rotation, eased);
		}

		private static float Progress(float time, float duration)
		{
			return duration > 0f ? Mathf.Clamp01(time / duration) : 1f;
		}

		/// <summary>Called by the rifle when the cartridge has been chambered: the cartridge is gone.</summary>
		public void Spend()
		{
			seated = false;
			inserting = false;
			grabRedirect = null;
			MagazineManager rack = GetComponentInParent<MagazineManager>(true);
			if (rack != null)
			{
				rack.Take(this);
			}
			else
			{
				Destroy(gameObject);
			}
		}
	}
}
