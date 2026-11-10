using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace JudgmentDay
{
	/// <summary>
	/// The cockpit art of the gun and its swinging lever. The game's pump (a straight slide you drag back and forth) keeps doing the
	/// logic: grabbing, chambering. The pump's grab area sits on the lever loop; the lever is a separate sprite, a child of the body sprite,
	/// turned about its hinge in step with the pump (0 at rest, LeverAngle when pulled all the way back).
	/// </summary>
	internal class Lever : MonoBehaviour
	{
		// Where the pictures put things (art\make_jd_sprites.py prints these: the constants must match).
		private const float BodyHeight = 48f;

		internal const float HingeX = 145f;

		internal const float HingeY = 23f;

		// The part of the lever that can be grabbed: this share of the picture's width and height, at the bottom of the loop.
		private const float HandleWidthShare = 0.8f;

		private const float HandleHeightShare = 0.5f;

		private const float LeverWidth = 40f;

		private const float LeverHeight = 24f;

		private const float LeverHingeX = 5f;

		private const float LeverHingeY = 5f;

		// The slug shotgun's sprites, read from the game's race scene, and what stands in for each. Every file is the size of the sprite
		// it replaces. The shells are the game's own.
		private static readonly Dictionary<string, string> Reskin = new Dictionary<string, string>
		{
			{ "IMG_3632_0", "jd_body.png" },
			{ "IMG_3632_under_0", "jd_clear.png" },
			{ "IMG_3632_cover", "jd_clear.png" },
			{ "IMG_3633_0", "jd_pump_clear.png" }
		};

		/// <summary>The lever's pump while it is fitted (the drag patch only touches this one).</summary>
		internal static LinearObject Pump;

		private NPumpActionGun gun;

		private SpriteRenderer bodySprite;

		private Transform lever;

		private SpriteRenderer lever2D;

		private SpriteRenderer pumpSprite;

		private int placeFrames;

		private bool placed;

		public void Setup(NPumpActionGun owner)
		{
			gun = owner;
			Pump = owner.pump;
			EaseAction();
			int swapped = 0;
			foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				Sprite current = renderer.sprite;
				if (current == null || !Reskin.TryGetValue(current.name, out string file))
				{
					continue;
				}
				Sprite replacement = Mod.Ctx.Sprites.LoadSprite(file, current);
				if (replacement == null)
				{
					continue;
				}
				renderer.sprite = replacement;
				if (current.name == "IMG_3633_0" && (pumpSprite == null || renderer.GetComponentInParent<NPumpActionGun>() == gun))
				{
					pumpSprite = renderer;
				}
				if (current.name == "IMG_3632_0" && (bodySprite == null || renderer.GetComponentInParent<NPumpActionGun>() == gun))
				{
					bodySprite = renderer;
				}
				swapped++;
			}
			Mod.Log.LogInfo("Judgment Day: swapped " + swapped + " cockpit sprites, body sprite " + (bodySprite != null ? "found" : "MISSING"));
			if (bodySprite != null)
			{
				BuildLever();
			}
		}

		// The pump shotgun's action is stiff: the pump is pulled shut by springs, ten times stronger while a shell is chambered, so that
		// it cannot be worked open on a live round. A lever should move easily. UpdatePump rewrites the pump's spring every frame from the
		// two private base values it kept in Awake, so those are scaled; the live-shell hold is a public field; and the control-display
		// ratio makes a short drag move the lever further.
		private void EaseAction()
		{
			if (gun.pump == null)
			{
				return;
			}
			Traverse t = Traverse.Create(gun);
			float before = gun.pump.objectSpringForce;
			foreach (string name in new[] { "basePumpSpring", "basePumpPhysicsSpring" })
			{
				Traverse f = t.Field(name);
				if (f.FieldExists())
				{
					f.SetValue(f.GetValue<float>() * Mod.SpringShare.Value);
				}
				else
				{
					Mod.Log.LogWarning("NPumpActionGun." + name + " was not found: the lever keeps the pump's full spring");
				}
			}
			gun.loadedHoldSpringMultiplier = Mod.LoadedHold.Value;
			gun.pump.CDRatio = Mod.LeverReach.Value;
			if (Mod.DumpInfo.Value)
			{
				Mod.Log.LogInfo($"Judgment Day lever action eased: spring share {Mod.SpringShare.Value}, live-shell hold x{Mod.LoadedHold.Value} (game 10), reach {Mod.LeverReach.Value}; pump springs were {before} / {gun.pump.physicsObjectSpringForce}, mass {gun.pump.mass}, friction {gun.pump.linearFriction}, limits {gun.pump.minLimit}..{gun.pump.maxLimit}");
			}
		}

		// The lever sprite: the lever's own picture, pivot at its hinge, placed so the hinge lands on the body's hinge.
		private void BuildLever()
		{
			Texture2D texture = Mod.Ctx.Sprites.LoadTexture("jd_lever.png");
			Sprite body = bodySprite.sprite;
			if (texture == null || texture.width != (int)LeverWidth || texture.height != (int)LeverHeight)
			{
				Mod.Log.LogWarning("jd_lever.png is missing or not 40x24: no swinging lever");
				return;
			}
			float ppu = body.pixelsPerUnit;
			var pivot = new Vector2(LeverHingeX / LeverWidth, 1f - LeverHingeY / LeverHeight);
			Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, ppu);
			sprite.name = "jd lever";
			var go = new GameObject("jd lever");
			go.layer = bodySprite.gameObject.layer;
			go.transform.SetParent(bodySprite.transform, false);
			// The body's pivot is in pixels from its bottom left; the hinge is in pixels from the picture's top left.
			Vector2 p = body.pivot;
			go.transform.localPosition = new Vector3((HingeX - p.x) / ppu, (BodyHeight - HingeY - p.y) / ppu, 0f);
			var renderer = go.AddComponent<SpriteRenderer>();
			renderer.sprite = sprite;
			renderer.sortingLayerID = bodySprite.sortingLayerID;
			renderer.sortingOrder = bodySprite.sortingOrder + 2;
			renderer.sharedMaterial = bodySprite.sharedMaterial;
			lever = go.transform;
			lever2D = renderer;
			if (Mod.DumpInfo.Value)
			{
				Mod.Log.LogInfo($"Judgment Day lever built: body sprite ppu {ppu}, pivot {p}, hinge local {go.transform.localPosition}, layer {go.layer}, order {renderer.sortingOrder}, body scale {bodySprite.transform.lossyScale}");
			}
		}

		// The game's pump is the part you grab. This is a lever gun, so the pump's grab area is moved onto the lever loop (the pump's own
		// sprite is clear now) with a new rest pose, the way the Sniper moves its bolt. Done a few frames after Awake, once the
		// grabbable has set itself up and the sprites have a place on screen.
		private void Place()
		{
			if (placed || lever2D == null || pumpSprite == null || gun.pump == null)
			{
				return;
			}
			if (++placeFrames < 3)
			{
				return;
			}
			placed = true;
			Transform pump = gun.pump.transform;
			// Only the bottom of the loop is the handle: the grab area is moved to the lowest part of the lever and cut down to its size, so
			// the rest of the lever (and the gun round it) is not caught by the cursor.
			Bounds lb = lever2D.bounds;
			Vector3 downWorld = -lever2D.transform.up;
			Vector3 rightWorld = lever2D.transform.right;
			float handleWidth = lb.size.x * HandleWidthShare;
			float handleHeight = lb.size.y * HandleHeightShare;
			// The picture is padded on its top left, so the loop's bottom arc sits a little right of the picture's middle.
			Vector3 handle = lb.center + downWorld * (lb.size.y * 0.5f - handleHeight * 0.5f) + rightWorld * (lb.size.x * 0.08f);
			Vector3 offset = handle - pumpSprite.bounds.center;
			Vector3 world = pump.position + offset;
			Vector3 local = pump.parent != null ? pump.parent.InverseTransformPoint(world) : world;
			gun.pump.ResetRestPose(local, pump.localRotation);
			float oldAngle = gun.pump.anchorAngle;
			if (Mod.DragDown.Value)
			{
				// The lever swings down, so it is pulled by dragging down: of the two directions at right angles to the pump's usual
				// slide, take the one that points down on the screen (the body sprite faces the camera, so its up is the screen's up).
				Vector3 origin = gun.pump.PlaneToWorld(Vector2.zero);
				Vector3 down = -lever2D.transform.up;
				float up = Vector3.Dot(gun.pump.PlaneToWorld(Vector2.up) - origin, down);
				float dn = Vector3.Dot(gun.pump.PlaneToWorld(Vector2.down) - origin, down);
				gun.pump.anchorAngle = up >= dn ? Mathf.PI / 2f : -Mathf.PI / 2f;
				gun.pump.ResetRestPose(local, pump.localRotation);
			}
			float oldMargin = 0f;
			if (gun.pump.attachedCollider != null)
			{
				oldMargin = gun.pump.attachedCollider.hitBoxExpand;
				gun.pump.attachedCollider.hitBoxExpand = Mod.GrabMargin.Value;
			}
			string shrunk = ShrinkHandle(gun.pump.attachedCollider, handle, rightWorld, -downWorld, handleWidth, handleHeight);
			if (Mod.DumpInfo.Value)
			{
				Mod.Log.LogInfo($"Judgment Day: pump grab area moved onto the lever by {offset} (pump sprite centre {pumpSprite.bounds.center}, lever centre {lever2D.bounds.center}); drag axis angle {oldAngle:0.00} -> {gun.pump.anchorAngle:0.00} (limits {gun.pump.minLimit}..{gun.pump.maxLimit}); grab margin {oldMargin} -> {(gun.pump.attachedCollider != null ? gun.pump.attachedCollider.hitBoxExpand : 0f)}; handle {handleWidth:0.##} x {handleHeight:0.##} world units: {shrunk}");
			}
		}

		// Cuts the pump's grab shape down to a box of the given world size (a square collider's width and height, a circle's radius, a
		// capsule's radius and length), whatever way round the collider's own plane runs.
		private static string ShrinkHandle(InteriorCollider collider, Vector3 centre, Vector3 right, Vector3 up, float width, float height)
		{
			if (collider == null)
			{
				return "no collider";
			}
			Vector2 origin = collider.WorldToPlane(centre);
			Vector2 perRight = collider.WorldToPlane(centre + right) - origin;
			Vector2 perUp = collider.WorldToPlane(centre + up) - origin;
			float planeW = Mathf.Abs(perRight.x) * width + Mathf.Abs(perUp.x) * height;
			float planeH = Mathf.Abs(perRight.y) * width + Mathf.Abs(perUp.y) * height;
			if (collider is InteriorSquareCollider square)
			{
				string was = square.width + "x" + square.height;
				square.width = planeW;
				square.height = planeH;
				return "square " + was + " -> " + planeW.ToString("0.##") + "x" + planeH.ToString("0.##");
			}
			if (collider is InteriorCapsuleCollider capsule)
			{
				string was = "r " + capsule.radius + " to " + capsule.otherPoint;
				capsule.radius = Mathf.Min(planeW, planeH) * 0.5f;
				capsule.otherPoint = planeW >= planeH ? new Vector2(planeW - planeH, 0f) : new Vector2(0f, planeH - planeW);
				return "capsule " + was + " -> r " + capsule.radius.ToString("0.##");
			}
			if (collider is InteriorCircleCollider circle)
			{
				string was = "r " + circle.radius;
				circle.radius = Mathf.Min(planeW, planeH) * 0.5f;
				return "circle " + was + " -> r " + circle.radius.ToString("0.##");
			}
			return "unknown collider type " + collider.GetType().Name + ", size kept";
		}

		private void Update()
		{
			if (lever == null || gun == null || gun.pump == null)
			{
				return;
			}
			Place();
			lever.localRotation = Quaternion.Euler(0f, 0f, -Mod.LeverAngle.Value * Mathf.Clamp01(gun.pump.value));
		}

		public void Remove()
		{
			if (lever != null)
			{
				Destroy(lever.gameObject);
			}
		}
	}
}
