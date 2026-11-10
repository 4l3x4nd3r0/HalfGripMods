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
		private static readonly MethodInfo AimGetter = AccessTools.PropertyGetter(typeof(Gun), "AimScreenPoint");

		private static readonly MethodInfo ResetPendulum = AccessTools.Method(typeof(Gun), "ResetPendulum");

		private NPumpActionGun gun;

		private MeshRenderer heldBody;

		private Art.HeldLever heldLever;

		private int rackFrames;

		private bool rackDone;

		// The turn in progress: where it started, how far it has got in one direction, and for how long. A back-and-forth swing of the
		// mouse (the weight sloshing side to side) never adds up; only a steady circle does.
		private float mark;

		private float extreme;

		private float runTime;

		private const float BackStep = 0.8f;

		// The circle is measured on the cursor itself, not on the game's swinging weight (which lags and swings, so one circle cocked it
		// at once and the next not at all): the direction the cursor travels in is smoothed, and the angle it turns through is summed.
		// A slow cursor does not count, and a turn of more than MaxTurnStep in one frame (a reversal, as in swinging side to side) ends the run.
		private const float MinSpeed = 0.15f;

		private const float VelocitySmoothing = 30f;

		private const float MaxTurnStep = 2f;

		private Vector2 lastPoint;

		private Vector2 smoothVelocity;

		private float lastHeading;

		private float turned;

		private bool hasPoint;

		private bool hasHeading;

		private bool reversed;

		private bool wasGrabbed;

		private float flipTime = -1f;

		private bool chamberedThisFlip;

		private bool sawEmptyTube;

		private MagazineManager shells;

		private int oldShellsPerPickup;

		public bool Flipping => flipTime >= 0f;

		// The shot may be fired as soon as the round is in the chamber, without waiting for the flip to finish.
		public bool CanFire => !Flipping || chamberedThisFlip;

		public void Setup(NPumpActionGun owner)
		{
			gun = owner;
			gun.gunSpin = true;
			// Every ammo pickup or kill refill is this many shells (a "magazine" of a shotgun is one shell for the pump).
			shells = owner.shellHolder != null ? owner.shellHolder.GetComponent<MagazineManager>() : null;
			if (shells != null)
			{
				oldShellsPerPickup = shells.entitiesPerMagazine;
				shells.entitiesPerMagazine = Mathf.Max(1, Mod.ShellsPerPickup.Value);
			}
			else
			{
				Mod.Log.LogWarning("No MagazineManager on the shell holder: pickups give the pump shotgun's one shell");
			}
			ShellArt.Casing(owner.spentCasing);
			Art.HeldParts parts = Art.ReskinHeldModel(owner);
			heldBody = parts != null ? parts.Body : null;
			heldLever = parts != null ? parts.Lever : null;
			if (AimGetter == null)
			{
				Mod.Log.LogWarning("Gun.AimScreenPoint was not found: the spin-cock reads the mouse position instead");
			}
			if (Mod.DumpInfo.Value)
			{
				Mod.Log.LogInfo($"Judgment Day set up on '{owner.name}': capacity={owner.capacity}, bullets={owner.bullets}, spin trigger {owner.spinRotationsToTrigger} turns in {owner.spinDetectionWindow} s (the game's own twirl); shells per pickup {oldShellsPerPickup} -> {(shells != null ? shells.entitiesPerMagazine : 0)}");
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
			if (shells != null)
			{
				shells.entitiesPerMagazine = oldShellsPerPickup;
			}
		}

		private Vector2 AimPoint()
		{
			if (AimGetter != null)
			{
				return (Vector2)AimGetter.Invoke(gun, null);
			}
			return Input.mousePosition;
		}

		// The cursor's total turning so far, in radians (the angle its direction of travel has turned through, counted signed).
		private float Heading()
		{
			Vector2 point = AimPoint() / Mathf.Max(1f, Screen.height);
			float dt = InteriorMath.DeltaTime;
			if (!hasPoint || dt <= 0.00001f)
			{
				lastPoint = point;
				hasPoint = true;
				return turned;
			}
			Vector2 velocity = (point - lastPoint) / dt;
			lastPoint = point;
			smoothVelocity = Vector2.Lerp(smoothVelocity, velocity, 1f - Mathf.Exp(-VelocitySmoothing * dt));
			if (smoothVelocity.magnitude < MinSpeed)
			{
				hasHeading = false;
				return turned;
			}
			float heading = Mathf.Atan2(smoothVelocity.y, smoothVelocity.x);
			if (hasHeading)
			{
				float step = Mathf.DeltaAngle(lastHeading * Mathf.Rad2Deg, heading * Mathf.Rad2Deg) * Mathf.Deg2Rad;
				if (Mathf.Abs(step) > MaxTurnStep)
				{
					reversed = true;
				}
				else
				{
					turned += step;
				}
			}
			lastHeading = heading;
			hasHeading = true;
			return turned;
		}

		private void LateUpdate()
		{
			if (gun == null)
			{
				return;
			}
			// The shells' row on the dash is laid out a few frames in, once everything has found its place.
			if (!rackDone && ++rackFrames >= 5)
			{
				rackDone = true;
				Rack.Layout(gun);
			}
			if (!gun.grabbed)
			{
				wasGrabbed = false;
				hasPoint = false;
				hasHeading = false;
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
			float angle = Heading();
			if (reversed)
			{
				reversed = false;
				LogDropped("reversed");
				ResetRun(angle);
			}
			if (!wasGrabbed)
			{
				wasGrabbed = true;
				ResetRun(angle);
			}
			if (Flipping)
			{
				ResetRun(angle);
				if (chamberedThisFlip && !gun.loaded)
				{
					// Fired as soon as it was chambered: the rest of the flip is dropped.
					flipTime = -1f;
					heldLever?.Set(0f);
					ForgetSpin();
					return;
				}
				Animate();
				return;
			}
			if (gun.loaded)
			{
				// A live shell: nothing to cock. Keep following the spin so the next turn starts from here.
				ResetRun(angle);
				sawEmptyTube = false;
				return;
			}
			if (!Turned(angle))
			{
				return;
			}
			ResetRun(angle);
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

		private void ResetRun(float angle)
		{
			mark = angle;
			extreme = angle;
			runTime = 0f;
		}

		// True once the weight has gone round TurnsPerCock turns in ONE direction within SpinWindow seconds. Turning back by more than
		// BackStep radians, or taking too long, starts the count again from where it is.
		private bool Turned(float angle)
		{
			float run = extreme - mark;
			float step = angle - extreme;
			if (run == 0f || Mathf.Sign(step) == Mathf.Sign(run))
			{
				extreme = angle;
			}
			else if (Mathf.Abs(step) > BackStep)
			{
				LogDropped("turned back");
				ResetRun(angle);
				return false;
			}
			if (extreme != mark)
			{
				runTime += InteriorMath.DeltaTime;
				if (runTime > Mod.SpinWindow.Value)
				{
					LogDropped("too slow");
					ResetRun(angle);
					return false;
				}
			}
			return Mathf.Abs(extreme - mark) >= Mod.TurnsPerCock.Value * 2f * Mathf.PI;
		}

		// For tuning: a run that got well on its way and then failed.
		private void LogDropped(string why)
		{
			float turns = Mathf.Abs(extreme - mark) / (2f * Mathf.PI);
			if (Mod.DumpInfo.Value && turns > 0.4f)
			{
				Mod.Log.LogInfo($"Spin-cock: a turn dropped at {turns:0.00} of {Mod.TurnsPerCock.Value} turns ({why}, {runTime:0.0} s)");
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

		// The flip takes FlipSeconds; the shell goes into the chamber halfway through, with the pump's release sound; firing is
		// blocked until it ends.
		private void Animate()
		{
			float seconds = Mathf.Max(0.2f, Mod.FlipSeconds.Value);
			flipTime += InteriorMath.DeltaTime;
			float t = flipTime / seconds;
			// Keep the game's twirl from picking up the spin that cocked the gun: a new one has to be made.
			ForgetSpin();
			if (!chamberedThisFlip && t >= 0.5f)
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
			ResetRun(turned);
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
