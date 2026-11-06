using System.Collections.Generic;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>
	/// Trees and bamboo you can hit, with the game's own rule for its Forest trees (<c>FellableTerrain.Struck</c>, <c>TreeFellConfig</c>):
	/// into the trunk at 120 speedo units or more (the speed into the surface x 1.8) it goes over and the car keeps 60 percent of its speed;
	/// slower, the car glances off it and the tree leans and sways back. Bamboo is softer: it goes over at half that speed. Only trees close
	/// to the road have a collider; the rest of the forest is scenery. The game's own trees are drawn by <see cref="TreeInstancer"/>; a tree
	/// that falls is taken out of there and replaced by a copy that topples.
	/// </summary>
	internal sealed class TreeHit : MonoBehaviour
	{
		internal float FellSpeed = 120f;
		/// <summary>The tree's drawing when it is a game object of its own (bamboo); null for the instanced trees.</summary>
		internal Transform Visual;
		internal TreeInstancer.Handle Handle;
		internal float Height = 40f;
		/// <summary>Share of its speed a car keeps after felling this one; 0 or less means the game's own value.</summary>
		internal float SpeedKept;

		private bool gone;
		internal bool Gone { get { return gone; } }
		private Transform hinge;
		private Quaternion rest;
		private Vector3 lean;
		private float angle, speed;
		private bool swaying;

		private static TreeFellConfig Config
		{
			get { return TreeFellConfig.Instance; }
		}

		internal static GameObject Solid(Transform parent, int layer, Vector3 basePos, float radius, float height, float fellSpeed, TreeInstancer.Handle handle, Transform visual, float speedKept = 0f)
		{
			GameObject go = new GameObject("tree trunk") { layer = layer };
			go.transform.SetParent(parent, false);
			go.transform.position = basePos;
			CapsuleCollider cap = go.AddComponent<CapsuleCollider>();
			cap.direction = 1;
			cap.radius = radius;
			cap.height = Mathf.Max(height, radius * 2.2f);
			cap.center = new Vector3(0f, cap.height * 0.5f, 0f);
			TreeHit hit = go.AddComponent<TreeHit>();
			hit.FellSpeed = fellSpeed;
			hit.Handle = handle;
			hit.Visual = visual;
			hit.Height = height;
			hit.SpeedKept = speedKept;
			return go;
		}

		/// <summary>Debug hit test only: any rigid body counts as a car.</summary>
		internal static bool AcceptAnyBody;

		/// <summary>For bamboo: the solid capsule, switched off for a car that comes in fast enough to break the clump (so nothing collides and the game's crash damage never runs).</summary>
		internal CapsuleCollider SolidCol;
		private bool armed;
		private Rigidbody armedBody;

		/// <summary>
		/// A bamboo clump near the road: solid for a slow car (it glances off and the clump sways, the game's rule), but a car coming in at
		/// <paramref name="fellSpeed"/> or more breaks it and goes through. A sensor trigger (on the Ignore Raycast layer, which the wheels'
		/// ground rays skip) round the clump switches the solid part off before contact and breaks the clump when the car reaches it.
		/// </summary>
		internal static GameObject BambooClump(Transform parent, int layer, Vector3 basePos, float solidRadius, float senseRadius, float height, float fellSpeed, float speedKept, Transform visual)
		{
			GameObject go = Solid(parent, layer, basePos, solidRadius, height, fellSpeed, null, visual, speedKept);
			TreeHit hit = go.GetComponent<TreeHit>();
			hit.SolidCol = go.GetComponent<CapsuleCollider>();
			GameObject sensor = new GameObject("bamboo sensor") { layer = 2 };
			sensor.transform.SetParent(go.transform, false);
			CapsuleCollider cap = sensor.AddComponent<CapsuleCollider>();
			cap.direction = 1;
			cap.radius = senseRadius;
			cap.height = Mathf.Max(height, senseRadius * 2.2f);
			cap.center = new Vector3(0f, cap.height * 0.5f, 0f);
			cap.isTrigger = true;
			sensor.AddComponent<Sensor>().Owner = hit;
			return go;
		}

		private sealed class Sensor : MonoBehaviour
		{
			public TreeHit Owner;

			private void OnTriggerEnter(Collider other) { Owner.SenseEnter(other); }
			private void OnTriggerStay(Collider other) { Owner.SenseStay(other); }
			private void OnTriggerExit(Collider other) { Owner.SenseExit(other); }
		}

		private bool IsCar(Collider other, out Rigidbody rb)
		{
			rb = other.attachedRigidbody;
			if (rb == null)
			{
				return false;
			}
			return rb.GetComponent<CarControllerNew>() != null || other.GetComponentInParent<CarControllerNew>() != null || AcceptAnyBody;
		}

		/// <summary>The body's horizontal speed toward the clump, in game units per second (x 1.8 for the speedometer).</summary>
		private float SpeedToward(Rigidbody rb)
		{
			Vector3 to = transform.position - rb.position;
			to.y = 0f;
			Vector3 v = rb.linearVelocity;
			v.y = 0f;
			return to.sqrMagnitude < 0.01f ? v.magnitude : Mathf.Max(0f, Vector3.Dot(v, to.normalized));
		}

		private float FlatDistance(Rigidbody rb)
		{
			Vector3 to = transform.position - rb.position;
			to.y = 0f;
			return to.magnitude;
		}

		internal void SenseEnter(Collider other)
		{
			Rigidbody rb;
			if (gone || SolidCol == null || !IsCar(other, out rb))
			{
				return;
			}
			if (SpeedToward(rb) * 1.8f >= FellSpeed)
			{
				armed = true;
				armedBody = rb;
				SolidCol.enabled = false;
			}
		}

		internal void SenseStay(Collider other)
		{
			if (gone || !armed || other.attachedRigidbody != armedBody || armedBody == null)
			{
				return;
			}
			Rigidbody rb = armedBody;
			if (FlatDistance(rb) <= 3.8f)
			{
				BreakBy(rb);
			}
			else if (SpeedToward(rb) * 1.8f < FellSpeed * 0.8f)
			{
				armed = false;
				armedBody = null;
				SolidCol.enabled = true;   // it slowed down: the clump is solid again
			}
		}

		internal void SenseExit(Collider other)
		{
			if (gone || !armed || armedBody == null || other.attachedRigidbody != armedBody)
			{
				return;
			}
			if (FlatDistance(armedBody) > 8.5f)
			{
				armed = false;
				armedBody = null;
				SolidCol.enabled = true;
			}
		}

		private void BreakBy(Rigidbody rb)
		{
			Vector3 v = rb.linearVelocity;
			Vector3 to = transform.position - rb.position;
			to.y = 0f;
			Vector3 flat = new Vector3(v.x, 0f, v.z);
			Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : (flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward);
			float kept = SpeedKept > 0f ? SpeedKept : 0.94f;
			armed = false;
			armedBody = null;
			Fell(v, dir);
			rb.linearVelocity = v * kept;
			Mod.Log.LogInfo("Bamboo broken at " + (SpeedToward(rb) * 1.8f).ToString("0") + " speedo, the car keeps " + kept.ToString("0.00"));
		}

		private void OnCollisionEnter(Collision collision)
		{
			if (gone || collision.contactCount == 0)
			{
				return;
			}
			CarControllerNew car = collision.collider.GetComponentInParent<CarControllerNew>();
			if (car == null && collision.rigidbody != null)
			{
				car = collision.rigidbody.GetComponent<CarControllerNew>();
			}
			Rigidbody body;
			Vector3 prev;
			if (car != null && car.rb != null)
			{
				body = car.rb;
				prev = car.prevVel;
			}
			else if (AcceptAnyBody && collision.rigidbody != null)
			{
				body = collision.rigidbody;
				prev = transform.position - body.position;   // the debug stand-in: pointing at the tree is "forward"
			}
			else
			{
				return;
			}
			ContactPoint contact = collision.GetContact(0);
			Vector3 vel = collision.relativeVelocity;
			if (Vector3.Dot(vel, prev) < 0f)
			{
				vel = -vel;
			}
			// in the callback on the tree the contact normal points into the tree (the game's own code reads it in the car's callback, where it points
			// back at the car): turn it so it opposes the motion, whichever way round Unity gives it
			Vector3 n = contact.normal;
			if (Vector3.Dot(n, vel) > 0f)
			{
				n = -n;
			}
			float into = -Vector3.Dot(vel, n);
			if (into <= 0f)
			{
				return;
			}
			TreeFellConfig cfg = Config;
			bool fell = into * 1.8f >= FellSpeed;
			Vector3 away = -n;
			if (fell)
			{
				Fell(vel, away);
			}
			else
			{
				Bend(away, into);
			}
			Vector3 normal = n;
			normal.y = 0f;
			body.linearVelocity = fell ? vel * (SpeedKept > 0f ? SpeedKept : cfg.speedKept) : (vel + normal * into) * cfg.glanceSpeedKept;
			if (AcceptAnyBody)
			{
				Mod.Log.LogInfo("Tree hit by the test body at " + (into * 1.8f).ToString("0") + " speedo into the trunk: " + (fell ? "FELL" : "glanced"));
			}
		}
		private void Bend(Vector3 push, float into)
		{
			if (Visual == null)
			{
				return;
			}
			TreeFellConfig cfg = Config;
			float deg = Mathf.Min(into * cfg.bendPerSpeed, cfg.bendMax);
			Vector3 l = Vector3.ProjectOnPlane(push, Vector3.up);
			if (deg < 0.5f || l.sqrMagnitude < 0.01f)
			{
				return;
			}
			if (!swaying)
			{
				swaying = true;
				rest = Visual.rotation;
				lean = l.normalized;
				angle = 0f;
				speed = 0f;
			}
			speed += deg * Mathf.Sqrt(cfg.bendStiffness);
			enabled = true;
		}

		private void Update()
		{
			if (!swaying || Visual == null)
			{
				enabled = false;
				return;
			}
			TreeFellConfig cfg = Config;
			float dt = Time.deltaTime;
			speed -= (cfg.bendStiffness * angle + 2f * cfg.bendDamping * Mathf.Sqrt(cfg.bendStiffness) * speed) * dt;
			angle += speed * dt;
			Visual.rotation = Quaternion.AngleAxis(angle, Vector3.Cross(Vector3.up, lean)) * rest;
			if (Mathf.Abs(angle) < 0.1f && Mathf.Abs(speed) < 1f)
			{
				angle = 0f;
				speed = 0f;
				Visual.rotation = rest;
				swaying = false;
				enabled = false;
			}
		}

		private void Fell(Vector3 push, Vector3 away)
		{
			gone = true;
			GetComponent<Collider>().enabled = false;
			Vector3 dir = Vector3.ProjectOnPlane(away, Vector3.up);
			if (dir.sqrMagnitude < 0.01f)
			{
				dir = Vector3.ProjectOnPlane(push, Vector3.up);
			}
			if (dir.sqrMagnitude < 0.01f)
			{
				dir = Vector3.forward;
			}
			TreeFellConfig cfg = Config;
			dir = Quaternion.Euler(0f, Random.Range(-cfg.fallAngleSpread, cfg.fallAngleSpread), 0f) * dir.normalized;
			Transform pivot = Visual;
			if (pivot == null && Handle != null)
			{
				pivot = TreeInstancer.Topple(Handle, gameObject.layer);
			}
			if (pivot != null)
			{
				swaying = false;
				Fall f = pivot.gameObject.AddComponent<Fall>();
				f.Begin(dir, push.magnitude, Height);
			}
			if (cfg.fellSounds != null && cfg.fellSounds.Length > 0)
			{
				SoundController.PlayRandomSoundAtPosition(cfg.fellSounds, transform.position, cfg.fellVolume, 5f, 120f, 0.1f);
			}
			enabled = false;
		}

		/// <summary>The fall of a felled tree: it swings over about its foot, lies for a while, then shrinks away.</summary>
		private sealed class Fall : MonoBehaviour
		{
			private Quaternion start;
			private Vector3 axis;
			private float angle, speed, gravity, landed = -1f;
			private Vector3 scale0;

			internal void Begin(Vector3 dir, float pushSpeed, float height)
			{
				TreeFellConfig cfg = Config;
				start = transform.rotation;
				scale0 = transform.localScale;
				axis = Vector3.Cross(Vector3.up, dir);
				float k = Random.Range(1f - cfg.fallSpeedSpread, 1f + cfg.fallSpeedSpread);
				speed = cfg.fallKick * k * pushSpeed / Mathf.Max(height, 1f) * 57.29578f;
				gravity = cfg.fallGravity * k * 1.5f * Physics.gravity.magnitude / Mathf.Max(height, 1f) * 57.29578f;
			}

			private void Update()
			{
				TreeFellConfig cfg = Config;
				if (landed < 0f)
				{
					speed += gravity * Mathf.Max(Mathf.Sin(angle * Mathf.Deg2Rad), 0.05f) * Time.deltaTime;
					angle += speed * Time.deltaTime;
					if (angle >= 88f)
					{
						angle = 88f;
						landed = Time.time;
					}
					transform.rotation = Quaternion.AngleAxis(angle, axis) * start;
					return;
				}
				float t = (Time.time - landed - cfg.fallenLifetime) / Mathf.Max(cfg.shrinkTime, 0.01f);
				if (t >= 1f)
				{
					Destroy(gameObject);
					return;
				}
				if (t > 0f)
				{
					transform.localScale = scale0 * (1f - t);
				}
			}
		}
	}
}
