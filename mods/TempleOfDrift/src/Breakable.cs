using System.Collections.Generic;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>
	/// A plank barricade across one end of the short way. Nothing in it is solid, so a car never collides with it and the game's own crash
	/// damage (which only runs in the car's OnCollisionEnter) cannot happen. One sensor covers the whole opening: a car that comes in at
	/// <see cref="BreakSpeed"/> or more (dashboard units) breaks it and goes through, keeping <see cref="SpeedKept"/> of its speed; a slower car
	/// is held by a soft stop (the part of its velocity toward the boards is removed while it is inside the sensor), so it stops just short of
	/// them without damage. The broken pieces tumble, fall and shrink away.
	/// </summary>
	internal sealed class Breakable : MonoBehaviour
	{
		internal float BreakSpeed = 60f;
		internal float SpeedKept = 0.9f;
		internal float FloorY;
		/// <summary>The road's direction of travel here (flat): the boards stand across it.</summary>
		internal Vector3 Direction = Vector3.forward;
		/// <summary>Debug hit test only: any rigid body counts as a car.</summary>
		internal static bool AcceptAnyBody;

		private sealed class Piece
		{
			public Transform T;
			public Vector3 Vel, Spin, Scale0;
			public bool Landed;
		}

		private readonly List<Piece> pieces = new List<Piece>();
		private bool broken;
		private float brokenAt;
		private int stopped;
		internal bool Broken { get { return broken; } }
		/// <summary>How many times a slow car was held at the boards (debug).</summary>
		internal int Stops { get { return stopped; } }

		/// <summary>One piece of the barricade: a box mesh with its own object, no collider.</summary>
		internal void AddPiece(Mesh mesh, Material material, Vector3 pos, Quaternion rot, int layer)
		{
			GameObject go = new GameObject("mine piece") { layer = layer };
			go.transform.SetParent(transform, false);
			go.transform.SetPositionAndRotation(pos, rot);
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer mr = go.AddComponent<MeshRenderer>();
			mr.sharedMaterial = material;
			mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			mr.receiveShadows = false;
			mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
			mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
			PerfCuller.Register(mr, true, false);
			pieces.Add(new Piece { T = go.transform, Scale0 = go.transform.localScale });
		}

		/// <summary>The sensor over the whole opening (a trigger box on the Ignore Raycast layer, which the wheels' ground rays skip).</summary>
		internal void AddSensor(Vector3 centre, Quaternion rot, Vector3 size)
		{
			GameObject go = new GameObject("mine barricade sensor") { layer = 2 };
			go.transform.SetParent(transform, false);
			go.transform.SetPositionAndRotation(centre, rot);
			BoxCollider bc = go.AddComponent<BoxCollider>();
			bc.size = size;
			bc.isTrigger = true;
			go.AddComponent<Relay>().Owner = this;
		}

		private sealed class Relay : MonoBehaviour
		{
			public Breakable Owner;

			private void OnTriggerEnter(Collider other) { Owner.Touched(other); }
			private void OnTriggerStay(Collider other) { Owner.Touched(other); }
		}

		private void Touched(Collider other)
		{
			if (broken)
			{
				return;
			}
			Rigidbody rb = other.attachedRigidbody;
			if (rb == null)
			{
				return;
			}
			if (rb.GetComponent<CarControllerNew>() == null && other.GetComponentInParent<CarControllerNew>() == null && !AcceptAnyBody)
			{
				return;
			}
			Vector3 v = rb.linearVelocity;
			Vector3 flat = new Vector3(v.x, 0f, v.z);
			if (flat.magnitude * 1.8f >= BreakSpeed)
			{
				Break(rb, flat);
				return;
			}
			// too slow: held at the boards, without a collision (so without damage)
			Vector3 dir = new Vector3(Direction.x, 0f, Direction.z).normalized;
			float side = Vector3.Dot(rb.position - transform.position, dir);
			float toward = side < 0f ? Vector3.Dot(v, dir) : -Vector3.Dot(v, dir);
			if (toward > 0f)
			{
				rb.linearVelocity = v - dir * (side < 0f ? toward : -toward);
				stopped++;
			}
		}

		private void Break(Rigidbody rb, Vector3 flat)
		{
			broken = true;
			brokenAt = Time.time;
			Vector3 dir = flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward;
			foreach (Piece p in pieces)
			{
				Vector3 sideways = Vector3.Cross(Vector3.up, dir) * Random.Range(-4f, 4f);
				p.Vel = dir * Random.Range(3f, 9f) + sideways + Vector3.up * Random.Range(1f, 5f);
				p.Spin = new Vector3(Random.Range(-220f, 220f), Random.Range(-220f, 220f), Random.Range(-220f, 220f));
			}
			rb.linearVelocity = rb.linearVelocity * SpeedKept;
			TreeFellConfig cfg = TreeFellConfig.Instance;
			if (cfg != null && cfg.fellSounds != null && cfg.fellSounds.Length > 0)
			{
				SoundController.PlayRandomSoundAtPosition(cfg.fellSounds, transform.position, cfg.fellVolume, 5f, 120f, 0.1f);
			}
			Mod.Log.LogInfo("Barricade broken at " + (flat.magnitude * 1.8f).ToString("0") + " speedo, the car keeps " + SpeedKept.ToString("0.00"));
		}

		private void Update()
		{
			if (!broken)
			{
				return;
			}
			float dt = Time.deltaTime;
			float age = Time.time - brokenAt;
			foreach (Piece p in pieces)
			{
				if (p.T == null)
				{
					continue;
				}
				if (!p.Landed)
				{
					p.Vel += Physics.gravity * dt;
					p.T.position += p.Vel * dt;
					p.T.Rotate(p.Spin * dt, Space.Self);
					if (p.T.position.y <= FloorY + 0.4f && p.Vel.y < 0f)
					{
						p.Landed = true;
						Vector3 pos = p.T.position;
						pos.y = FloorY + 0.4f;
						p.T.position = pos;
					}
				}
				if (age > 3f)
				{
					p.T.localScale = p.Scale0 * Mathf.Max(0f, 1f - (age - 3f));
				}
			}
			if (age > 4f)
			{
				Destroy(gameObject);
			}
		}
	}
}
