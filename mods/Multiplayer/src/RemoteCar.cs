using System;
using HarmonyLib;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// Stands in for somebody else's car (or, on a client, for one of the host's AI cars). It is an ordinary enemy car from the game's
	/// prefab, so the game's own gun rays, tire shots, blasts, rams and damage marks all find it, but it is driven by the network:
	/// <see cref="EnemyController.canMove"/> stays off and the body is a kinematic rigidbody moved towards the last state received.
	/// </summary>
	internal sealed class RemoteCar : MonoBehaviour
	{
		internal const float Health = 1000000f;

		internal ulong PeerId;
		internal bool IsAi;
		internal int AiIndex;
		internal EnemyController Controller;
		internal CarControllerNew Car;
		internal Rigidbody Body;
		internal float HpFraction = 1f;

		// What the player behind this car is doing with a gun (see Cues): the gun type + 1, 0 when no gun is raised, where it points, whom it points at.
		internal EnemyGun Gun;
		internal int AimGun;
		internal Vector3 AimPoint;
		internal ulong AimedAt;
		internal float AimStamp;
		internal LineRenderer Laser;
		internal float LaserAlpha;

		private Vector3 position;
		private Vector3 velocity;
		private Quaternion rotation = Quaternion.identity;
		private float steer;
		private float stamp;
		private bool hasState;

		internal bool Human { get { return !IsAi; } }

		/// <summary>The player's name, or the AI car's.</summary>
		internal string DisplayName()
		{
			if (IsAi)
			{
				CarSkinController skin = GetComponent<CarSkinController>();
				return skin != null && !string.IsNullOrEmpty(skin.carName) ? skin.carName : "AI";
			}
			Peer peer = Room.Find(PeerId);
			return peer != null ? peer.Name : "Player";
		}

		/// <summary>What floats over the car: the name and, for a player, the connection's ping on a second line.</summary>
		internal string TagText()
		{
			string name = DisplayName();
			if (IsAi)
			{
				return name;
			}
			Peer peer = Room.Find(PeerId);
			return peer != null && peer.PingMs > 0 && !peer.Local ? name + "\n" + peer.PingMs + " ms" : name;
		}

		internal Vector3 Position { get { return Body != null ? Body.position : transform.position; } }

		internal Vector3 Velocity { get { return velocity; } }

		internal static RemoteCar Make(GameObject car, ulong peerId, bool ai, int aiIndex)
		{
			RemoteCar rc = car.AddComponent<RemoteCar>();
			rc.PeerId = peerId;
			rc.IsAi = ai;
			rc.AiIndex = aiIndex;
			rc.Controller = car.GetComponent<EnemyController>();
			rc.Gun = car.GetComponentInChildren<EnemyGun>(true);
			if (!ai && rc.Gun != null)
			{
				rc.Gun.enabled = false; // the game's AI must not shoot from another player's car; Cues shows the player's own aim instead
			}
			rc.Car = car.GetComponent<CarControllerNew>();
			rc.Body = car.GetComponent<Rigidbody>();
			if (rc.Body != null)
			{
				rc.Body.isKinematic = true;
			}
			rc.position = car.transform.position;
			rc.rotation = car.transform.rotation;
			NameTag.Attach(car, rc.TagText, !ai);
			return rc;
		}

		internal void OnState(Vector3 p, Quaternion r, Vector3 v, float steerAngle, float hp)
		{
			// refuse nonsense: an absurd position or a NaN would fling the body across the map
			if (float.IsNaN(p.x + p.y + p.z + r.x + r.y + r.z + r.w + v.x + v.y + v.z) || p.sqrMagnitude > 1e10f)
			{
				return;
			}
			position = p;
			velocity = v;
			rotation = r;
			steer = steerAngle;
			HpFraction = hp;
			stamp = Time.unscaledTime;
			if (!hasState)
			{
				hasState = true;
				if (Body != null)
				{
					Body.position = p;
					Body.rotation = r;
				}
				transform.SetPositionAndRotation(p, r);
			}
		}

		private void FixedUpdate()
		{
			if (!hasState || Body == null)
			{
				return;
			}
			float age = Mathf.Min(Time.unscaledTime - stamp, 0.25f);
			Vector3 target = position + velocity * age;
			Vector3 now = Body.position;
			Vector3 next = (target - now).sqrMagnitude > 100f ? target : Vector3.Lerp(now, target, 0.5f);
			Body.MovePosition(next);
			Body.MoveRotation(Quaternion.Slerp(Body.rotation, rotation, 0.5f));
		}

		private void LateUpdate()
		{
			if (Controller == null)
			{
				return;
			}
			// the countdown switches every enemy on; this one is driven from the network
			Controller.canMove = false;
			if (Human)
			{
				Cues.UpdateStandIn(this);
			}
			if (Car != null)
			{
				Car.steeringAngleDeg = steer;
			}
			if (Controller.alive)
			{
				Traverse.Create(Controller).Field("health").SetValue(Health);
			}
		}
	}
}
