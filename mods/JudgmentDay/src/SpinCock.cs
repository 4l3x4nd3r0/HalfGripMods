using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace JudgmentDay
{
	/// <summary>
	/// The Terminator's one-handed reload. While the gun is raised the game's own "gun spin" tracker (Gun.gunSpin: a pendulum that
	/// hangs from the aim point, so circling the mouse winds it round) counts the turns. With a shell in the chamber the game's own
	/// twirl is left alone. With the chamber empty and a shell in the tube, one turn flips the gun over its lever and chambers the
	/// next shell, as the pump would; you cannot fire during the flip. With the tube empty a turn does nothing.
	/// </summary>
	internal class SpinCock : MonoBehaviour
	{
		private static readonly FieldInfo AngleField = AccessTools.Field(typeof(Gun), "pendulumCumulativeAngle");

		private static readonly MethodInfo ResetPendulum = AccessTools.Method(typeof(Gun), "ResetPendulum");

		private NPumpActionGun gun;

		private MeshRenderer heldBody;

		private Art.HeldLever heldLever;

		private float mark;

		private bool wasGrabbed;

		private float flipTime = -1f;

		private bool chamberedThisFlip;

		private bool sawEmptyTube;

		public bool Flipping => flipTime >= 0f;

		public void Setup(NPumpActionGun owner)
		{
			gun = owner;
			gun.gunSpin = true;
			Art.HeldParts parts = Art.ReskinHeldModel(owner);
			heldBody = parts != null ? parts.Body : null;
			heldLever = parts != null ? parts.Lever : null;
			if (AngleField == null)
			{
				Mod.Log.LogWarning("Gun.pendulumCumulativeAngle was not found: the game changed, the spin-cock cannot read the spin");
			}
			if (Mod.DumpInfo.Value)
			{
				Mod.Log.LogInfo($"Judgment Day set up on '{owner.name}': capacity={owner.capacity}, bullets={owner.bullets}, spin trigger {owner.spinRotationsToTrigger} turns in {owner.spinDetectionWindow} s (the game's own twirl)");
			}
		}

		private void OnDestroy()
		{
			if (heldLever != null)
			{
				heldLever.Remove();
			}
			if (gun != null)
			{
				gun.gunSpin = false;
			}
		}

		private float Angle()
		{
			return AngleField == null ? 0f : (float)AngleField.GetValue(gun);
		}

		private void LateUpdate()
		{
			if (gun == null)
			{
				return;
			}
			if (!gun.grabbed)
			{
				wasGrabbed = false;
				if (Flipping)
				{
					// Put down mid-flip: the reload still happens, the animation was started.
					if (!chamberedThisFlip)
					{
						Chamber();
					}
					flipTime = -1f;
					heldLever?.Set(0f);
					ForgetSpin();
				}
				return;
			}
			float angle = Angle();
			if (!wasGrabbed)
			{
				wasGrabbed = true;
				mark = angle;
			}
			if (Flipping)
			{
				mark = angle;
				Animate();
				return;
			}
			if (gun.loaded)
			{
				// A live shell: nothing to cock. Keep following the spin so the next turn starts from here.
				mark = angle;
				sawEmptyTube = false;
				return;
			}
			if (Mathf.Abs(angle - mark) < Mod.TurnsPerCock.Value * 2f * Mathf.PI)
			{
				return;
			}
			mark = angle;
			if (gun.bullets > 0)
			{
				StartFlip();
			}
			else if (!sawEmptyTube)
			{
				sawEmptyTube = true;
				if (Mod.DumpInfo.Value)
				{
					Mod.Log.LogInfo("Spin-cock: a turn with an empty chamber and an empty tube, nothing to chamber");
				}
			}
		}

		private void StartFlip()
		{
			flipTime = 0f;
			chamberedThisFlip = false;
			SoundController.PlayRandomSound2D(gun.reloadPullSounds, gameObject);
			ForgetSpin();
			if (Mod.DumpInfo.Value)
			{
				Mod.Log.LogInfo($"Spin-cock: flipping for {Mod.FlipSeconds.Value} s, bullets {gun.bullets}, chambered(spent) {gun.chambered}");
			}
		}

		// The flip takes FlipSeconds; the shell goes into the chamber 60% of the way through, with the pump's release sound; firing is
		// blocked until it ends.
		private void Animate()
		{
			float seconds = Mathf.Max(0.2f, Mod.FlipSeconds.Value);
			flipTime += InteriorMath.DeltaTime;
			float t = flipTime / seconds;
			// Keep the game's twirl from picking up the spin that cocked the gun: a new one has to be made.
			ForgetSpin();
			if (!chamberedThisFlip && t >= 0.6f)
			{
				Chamber();
				SoundController.PlayRandomSound2D(gun.reloadReleaseSounds, gameObject);
			}
			if (t >= 1f)
			{
				flipTime = -1f;
				heldLever?.Set(0f);
				ForgetSpin();
				return;
			}
			// The lever of the held gun snaps open at the start, stays open through the turn and closes as the shell goes in (the film's flip
			// cycles the lever with the spin).
			float open = t < 0.2f ? Smooth(t / 0.2f) : t < 0.5f ? 1f : t < 0.78f ? 1f - Smooth((t - 0.5f) / 0.28f) : 0f;
			heldLever?.Set(open * Mod.LeverAngle.Value);
			float eased = t * t * (3f - 2f * t);
			Transform aim = gun.aimModel;
			if (aim != null)
			{
				// The gun is tossed up a little as it turns, so the turn can be seen from behind.
				Vector3 up = aim.up;
				float lift = heldBody != null ? heldBody.bounds.size.magnitude * 0.12f : 0.1f;
				aim.RotateAround(Pivot(aim), aim.right, -360f * eased);
				aim.position += up * (Mathf.Sin(Mathf.PI * t) * lift);
			}
		}

		private static float Smooth(float x)
		{
			x = Mathf.Clamp01(x);
			return x * x * (3f - 2f * x);
		}

		// The flip is about a point a little behind the middle of the held gun, where the lever is.
		private Vector3 Pivot(Transform aim)
		{
			if (heldBody == null)
			{
				return aim.position;
			}
			Bounds b = heldBody.bounds;
			return b.center - aim.forward * (Mod.PivotBack.Value * b.size.magnitude * 0.6f);
		}

		// Wipes the game's spin tracker (turns counted, twirl on or off), and our mark with it.
		private void ForgetSpin()
		{
			ResetPendulum?.Invoke(gun, null);
			mark = Angle();
		}

		// The next shell goes into the chamber, like a pump stroke: a spent case is thrown out first.
		private void Chamber()
		{
			chamberedThisFlip = true;
			if (gun.bullets <= 0 || gun.loaded)
			{
				return;
			}
			gun.bullets--;
			gun.loaded = true;
			gun.racked = false;
			if (gun.chambered)
			{
				gun.chambered = false;
				if (gun.spentCasing != null)
				{
					gun.spentCasing.Play();
				}
				StartCoroutine(Eject());
			}
			if (Mod.DumpInfo.Value)
			{
				Mod.Log.LogInfo($"Spin-cock: chambered, bullets left {gun.bullets}");
			}
		}

		private IEnumerator Eject()
		{
			yield return new WaitForSeconds(1.5f);
			SoundController.PlayRandomSound2D(gun.ejectShellSounds, gameObject);
		}
	}
}
