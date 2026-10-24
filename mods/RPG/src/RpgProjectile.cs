using System.Collections.Generic;
using UnityEngine;

namespace HalfGripRpg
{
	/// <summary>
	/// A rocket in flight. A PG-7 style flight (the booster throws it out of the tube, then the sustainer lights and it speeds
	/// up), a hit check along its path, and a blast that damages by distance, shoves cars about and credits kills to the player.
	/// </summary>
	internal class RpgProjectile : MonoBehaviour
	{
		// The booster throws it out of the tube; the sustainer motor lights a moment later and pushes it up to full speed.
		private const float BoostTime = 0.15f;

		private const float SustainTime = 0.6f;

		private const float Gravity = 2f;

		// Seconds in the air before it goes off on its own.
		private const float Lifetime = 6f;

		// How fat the rocket is for the purpose of hitting something, so it cannot slip past a car it only clips.
		private const float Fatness = 0.5f;

		// A rocket does not arm until it has flown this far, so it cannot go off against the muzzle or your own bonnet.
		private const float ArmingDistance = 3f;

		// How fast the blast throws a free car, in metres per second, at the centre. It fades out to the edge.
		private const float KnockSpeed = 12f;

		private static readonly RaycastHit[] castHits = new RaycastHit[16];

		private static readonly List<EnemyController> hurt = new List<EnemyController>();

		private static readonly List<Rigidbody> shoved = new List<Rigidbody>();

		private Vector3 velocity;

		private int hitMask;

		private float flown;

		private float travelled;

		private bool spent;

		public static void Launch(Vector3 from, Vector3 direction, LayerMask hitLayers)
		{
			GameObject go = new GameObject("RpgRocket");
			go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(direction));
			RpgProjectile rocket = go.AddComponent<RpgProjectile>();
			rocket.velocity = direction * Mod.LaunchSpeed.Value;
			// The player's own car is never something its own rocket hits in flight.
			rocket.hitMask = hitLayers.value & ~(1 << LayerMask.NameToLayer("Player"));
			RpgModels.AttachFlyingRocket(go.transform);
			Vfx.Trail(go.transform, Vector3.back * RpgModels.FlyingLength * 0.95f);
		}

		private void Update()
		{
			if (spent)
			{
				return;
			}
			float dt = Time.deltaTime;
			flown += dt;

			if (flown > BoostTime)
			{
				float rate = (Mod.MaxSpeed.Value - Mod.LaunchSpeed.Value) / SustainTime;
				float speed = Mathf.MoveTowards(velocity.magnitude, Mod.MaxSpeed.Value, rate * dt);
				velocity = velocity.normalized * speed;
			}
			velocity += Vector3.down * (Gravity * dt);

			Vector3 step = velocity * dt;
			float distance = step.magnitude;
			if (distance > 0.0001f)
			{
				Vector3 heading = step / distance;
				int count = Physics.SphereCastNonAlloc(transform.position, Fatness, heading, castHits, distance, hitMask, QueryTriggerInteraction.Ignore);
				int best = -1;
				for (int i = 0; i < count; i++)
				{
					if (best < 0 || castHits[i].distance < castHits[best].distance)
					{
						best = i;
					}
				}
				if (best >= 0)
				{
					RaycastHit hit = castHits[best];
					Vector3 point = hit.distance <= 0f ? transform.position : hit.point;
					EnemyController struck = hit.collider.GetComponentInParent<EnemyController>();
					if (travelled < ArmingDistance && struck == null)
					{
						// Still unarmed and it hit scenery: it just drops, with no blast.
						spent = true;
						Destroy(gameObject);
						return;
					}
					Explode(point + hit.normal * 0.01f, hit.normal, struck);
					return;
				}
				transform.position += step;
				transform.rotation = Quaternion.LookRotation(heading);
				travelled += distance;
			}
			if (flown >= Lifetime)
			{
				Explode(transform.position, Vector3.zero, null);
			}
		}

		// How much of the blast's full damage reaches something this far from the centre: all of it out to two car lengths, half at
		// three, 15% at four, and nothing past five.
		public static float Falloff(float distance)
		{
			float cars = distance / Mathf.Max(0.1f, Mod.CarLength.Value);
			if (cars <= 2f)
			{
				return 1f;
			}
			if (cars <= 3f)
			{
				return Mathf.Lerp(1f, 0.5f, cars - 2f);
			}
			if (cars <= 4f)
			{
				return Mathf.Lerp(0.5f, 0.15f, cars - 3f);
			}
			return cars <= 5f ? Mathf.Lerp(0.15f, 0f, cars - 4f) : 0f;
		}

		private void Explode(Vector3 at, Vector3 surfaceNormal, EnemyController struck)
		{
			spent = true;
			float carLength = Mathf.Max(0.1f, Mod.CarLength.Value);
			float reach = carLength * 5f;
			Vfx.Explosion(at, carLength);
			SoundController.PlayRandomSoundAtPosition(new[] { Vfx.BoomClip() }, at, 1f, 40f, 800f, 0.08f);

			// Burnt black where it went off; much fainter if it went off on a car, so the ground below only got scorched.
			int enemyLayer = LayerMask.NameToLayer("Enemy");
			int groundMask = enemyLayer >= 0 ? hitMask & ~(1 << enemyLayer) : hitMask;
			bool onCar = struck != null;
			Scorch.Place(at, surfaceNormal, onCar, groundMask, carLength * (onCar ? 1.3f : 2.2f), onCar ? 0.3f : 0.92f);

			float centre = Mod.ExplosionDamage.Value;
			bool playerHurt = false;
			hurt.Clear();
			shoved.Clear();
			foreach (Collider collider in Physics.OverlapSphere(at, reach, ~0, QueryTriggerInteraction.Ignore))
			{
				Vector3 closest = collider.ClosestPoint(at);
				float distance = Vector3.Distance(closest, at);
				float share = Falloff(distance);
				if (share <= 0f)
				{
					continue;
				}
				float amount = centre * share;

				EnemyController enemy = collider.GetComponentInParent<EnemyController>();
				if (enemy != null)
				{
					Shove(collider.attachedRigidbody, at, reach, share);
					if (hurt.Contains(enemy))
					{
						continue;
					}
					hurt.Add(enemy);
					Vector3 away = closest - at;
					Vector3 hitNormal = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.up;
					if (enemy == struck)
					{
						// The warhead's shaped charge, on top of the blast.
						amount += Mod.DirectHitDamage.Value;
					}
					enemy.TakeDamage(LapShield.Scale(enemy, amount), closest, hitNormal, DamageMarkKind.Impact, -1f, null, true);
					continue;
				}

				CarControllerNew car = collider.GetComponentInParent<CarControllerNew>();
				if (car != null && car.isPlayer)
				{
					// Once per blast, however many of the car's colliders are in range.
					if (Mod.SelfDamage.Value && !playerHurt && car.carLink != null)
					{
						playerHurt = true;
						car.carLink.TakeCrashDamage(amount, true, DamageKind.Crash);
					}
					continue;
				}

				// Barrels and the like go up if the blast is strong enough where they stand.
				DestructibleObject breakable = collider.GetComponent<DestructibleObject>();
				if (breakable != null && share >= 0.5f)
				{
					breakable.TakeDamageFromBullet();
				}
				Shove(collider.attachedRigidbody, at, reach, share);
			}
			hurt.Clear();
			shoved.Clear();
			ShakeCamera(at, carLength * 3f);
			Destroy(gameObject);
		}

		// A free body is thrown away from the blast, with some lift. Heavier things are thrown harder, so every car goes the same speed.
		private static void Shove(Rigidbody body, Vector3 at, float radius, float share)
		{
			if (body == null || body.isKinematic || shoved.Contains(body))
			{
				return;
			}
			CarControllerNew player = body.GetComponentInParent<CarControllerNew>();
			if (player != null && player.isPlayer)
			{
				return;
			}
			shoved.Add(body);
			// Thrown at KnockSpeed at full strength, less further out.
			body.AddExplosionForce(body.mass * KnockSpeed * share * 2f, at, radius, 0.6f, ForceMode.Impulse);
		}

		private static void ShakeCamera(Vector3 at, float radius)
		{
			if (Shaker.instance == null)
			{
				return;
			}
			Camera cam = Camera.main;
			float near = cam != null ? Mathf.Clamp01(1f - Vector3.Distance(cam.transform.position, at) / (radius * 6f)) : 0.5f;
			if (near > 0f)
			{
				Shaker.instance.Shake(1.5f + 4f * near, 5f, 0.01f, 1.4f, 2);
			}
		}
	}
}
