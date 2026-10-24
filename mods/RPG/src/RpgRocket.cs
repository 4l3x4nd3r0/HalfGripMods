using UnityEngine;

namespace HalfGripRpg
{
	/// <summary>
	/// A rocket on the rack that you can pick up and load. It is an ordinary grabbable that falls back to the rack when dropped
	/// (the rack handles that); this adds the loading itself: bring it to the front of the launcher and it turns to lie along the
	/// tube and slides in tail first. Pull it back out and it is a rocket again.
	/// </summary>
	internal class RpgRocket : NAmmo
	{
		public SpriteRenderer loadedSprite;

		public SpriteRenderer emptySprite;

		public AudioClip[] insertSounds;

		// Seconds to swing round and line up in front of the tube, then to slide home.
		private const float AlignDuration = 0.14f;

		private const float InsertDuration = 0.3f;

		// Once home, the warhead is drawn over the launcher's tube and the shaft behind it, as two sprites cut from the one rocket.
		private const float WarheadShare = 0.6f;

		// How far in front of and behind the launcher's body those two are drawn, in game units.
		private const float PartGap = 0.1f;

		private SpriteRenderer front;

		private SpriteRenderer back;

		private bool splitMade;

		private bool partsShown;

		private RpgLauncher launcher;

		private float nextSearch;

		private bool inserting;

		private bool seated;

		private bool wasOverZone;

		private float elapsed;

		private Vector3 fromPosition;

		private Quaternion fromRotation;

		// Where it sits relative to the launcher's body once home. The body is part of a moving handle and the rocket hangs on the rack,
		// so left alone the rocket would be left behind whenever the body shifts; it is carried along instead.
		private Vector3 homeLocalPosition;

		private Quaternion homeLocalRotation;

		private RpgLauncher.Pose align;

		private RpgLauncher.Pose insert;

		public override bool busy => seated || inserting;

		public override bool spent => false;

		private RpgLauncher Launcher
		{
			get
			{
				if (launcher == null && Time.time >= nextSearch)
				{
					launcher = GetComponentInParent<RpgLauncher>(true);
					if (launcher == null)
					{
						// The rack is not necessarily under the gun; look for it in the scene, but not every frame.
						launcher = FindFirstObjectByType<RpgLauncher>(FindObjectsInactive.Include);
						nextSearch = Time.time + 1f;
					}
				}
				return launcher;
			}
		}

		/// <summary>How long the rocket is, in world units, standing up.</summary>
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

		/// <summary>The way the art's warhead points in the world.</summary>
		public Vector3 ArtUp => Sprite != null ? Sprite.transform.rotation * Vector3.up : transform.up;

		/// <summary>The rotation it has when it sits upright on the rack, warhead up, whatever it is being tilted to now.</summary>
		public Quaternion RestRotation => transform.parent ? transform.parent.rotation * baseLocalRotation : baseLocalRotation;

		/// <summary>Where it is in the world right now.</summary>
		public Vector3 Position => transform.position;

		private SpriteRenderer Sprite => loadedSprite != null ? loadedSprite : emptySprite;

		protected override void Update()
		{
			RpgLauncher gun = Launcher;
			RefreshSprites(gun);
			if (inserting)
			{
				StepInsert(gun);
				return;
			}
			base.Update();
			if (seated && !stuck)
			{
				// Pulled back out of the tube.
				seated = false;
				grabRedirect = null;
				if (gun != null)
				{
					gun.OnRocketRemoved(this);
				}
			}
			if (seated || !FellOutOfTheWorld())
			{
				TryInsert(gun);
			}
		}

		// After the rack and the grab physics have had their say, put a loaded rocket back exactly where it sits in the tube.
		private void LateUpdate()
		{
			RpgLauncher gun = Launcher;
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
			if (partsShown && gun != null && front != null)
			{
				PlaceParts(gun);
			}
		}

		private void RefreshSprites(RpgLauncher gun)
		{
			SpriteRenderer whole = loadedSprite;
			if (emptySprite != null)
			{
				emptySprite.enabled = false;
			}
			if (whole == null)
			{
				return;
			}
			// While you aim, the launcher's own model stands in for the rocket, so the flat one is hidden.
			bool aiming = seated && gun != null && gun.grabbed;
			bool split = (seated || inserting) && !aiming && gun != null && gun.BodyRenderer != null && EnsureSplit(whole);
			whole.enabled = !aiming && !split;
			if (front != null)
			{
				front.enabled = split;
				back.enabled = split;
			}
			partsShown = false;
			if (!split)
			{
				return;
			}
			SpriteRenderer body = gun.BodyRenderer;
			front.sortingLayerID = body.sortingLayerID;
			back.sortingLayerID = body.sortingLayerID;
			front.sortingOrder = body.sortingOrder + 1;
			back.sortingOrder = body.sortingOrder - 1;
			partsShown = true;
		}

		// The two halves are held a little apart in depth as well as in draw order (order alone is not always enough in this scene),
		// but along the line of sight, so that neither moves on screen: pushing them along the plane's normal would shift them under perspective.
		private void PlaceParts(RpgLauncher gun)
		{
			SpriteRenderer whole = loadedSprite;
			Vector3 ray = (whole.transform.position - gun.ViewPosition).normalized * PartGap;
			front.transform.SetPositionAndRotation(whole.transform.position - ray, whole.transform.rotation);
			back.transform.SetPositionAndRotation(whole.transform.position + ray, whole.transform.rotation);
		}

		// Cuts the rocket's sprite in two across the neck: the warhead, and the shaft that goes into the tube.
		private bool EnsureSplit(SpriteRenderer whole)
		{
			if (splitMade)
			{
				return front != null && back != null;
			}
			if (whole.sprite == null)
			{
				return false;
			}
			splitMade = true;
			UnityEngine.Sprite full = whole.sprite;
			Rect rect = full.rect;
			int width = (int)rect.width;
			int height = (int)rect.height;
			int warheadRows = Mathf.Clamp(Mathf.RoundToInt(height * WarheadShare), 1, height - 1);
			int shaftRows = height - warheadRows;
			Vector2 pivot = full.pivot;

			// Both keep the whole sprite's pivot, so put together at the same spot they are exactly the one rocket again.
			UnityEngine.Sprite warhead = UnityEngine.Sprite.Create(full.texture, new Rect(rect.x, rect.y + shaftRows, width, warheadRows),
				new Vector2(pivot.x / width, (pivot.y - shaftRows) / warheadRows), full.pixelsPerUnit);
			UnityEngine.Sprite shaft = UnityEngine.Sprite.Create(full.texture, new Rect(rect.x, rect.y, width, shaftRows),
				new Vector2(pivot.x / width, pivot.y / shaftRows), full.pixelsPerUnit);
			warhead.hideFlags = HideFlags.HideAndDontSave;
			shaft.hideFlags = HideFlags.HideAndDontSave;
			front = MakePart("warhead", whole, warhead);
			back = MakePart("shaft", whole, shaft);
			return true;
		}

		private static SpriteRenderer MakePart(string name, SpriteRenderer whole, UnityEngine.Sprite sprite)
		{
			var go = new GameObject(name);
			go.layer = whole.gameObject.layer;
			go.transform.SetParent(whole.transform.parent, false);
			go.transform.localPosition = whole.transform.localPosition;
			go.transform.localRotation = whole.transform.localRotation;
			go.transform.localScale = whole.transform.localScale;
			SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
			renderer.sprite = sprite;
			renderer.sharedMaterial = whole.sharedMaterial;
			renderer.color = whole.color;
			renderer.sortingLayerID = whole.sortingLayerID;
			renderer.sortingOrder = whole.sortingOrder;
			renderer.enabled = false;
			return renderer;
		}

		private void TryInsert(RpgLauncher gun)
		{
			// It is the warhead's tip that is brought to the muzzle, so that is what is measured, and it is drawn in from a fair way off.
			bool over = gun != null && gun.IsNearMuzzle(SpriteCentre + ArtUp * (Length * 0.5f), Mathf.Max(1.2f, Length * 0.7f));
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

		private void StepInsert(RpgLauncher gun)
		{
			elapsed += Time.deltaTime;
			float swing = Progress(elapsed, AlignDuration);
			Place(fromPosition, fromRotation, align, swing);
			float slide = 1f;
			if (swing >= 1f)
			{
				slide = Progress(elapsed - AlignDuration, InsertDuration);
				Place(align.position, align.rotation, insert, slide);
			}
			if (swing < 1f || slide < 1f)
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
			Stick();
			if (gun != null)
			{
				grabRedirect = gun.RocketRedirect;
				gun.OnRocketSeated(this);
			}
		}

		private void Place(Vector3 fromPos, Quaternion fromRot, RpgLauncher.Pose to, float t)
		{
			float eased = Mathf.SmoothStep(0f, 1f, t);
			transform.position = Vector3.Lerp(fromPos, to.position, eased);
			transform.rotation = Quaternion.Slerp(fromRot, to.rotation, eased);
		}

		private static float Progress(float time, float duration)
		{
			return duration > 0f ? Mathf.Clamp01(time / duration) : 1f;
		}

		/// <summary>Called by the launcher when the rocket has left the tube: the rocket is gone.</summary>
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
