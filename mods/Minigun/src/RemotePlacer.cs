using UnityEngine;

namespace HalfGripMinigun
{
	/// <summary>
	/// Puts the resting remote between the rear-view mirror and the leaderboard. Every frame, with the live interior camera, it
	/// measures where the remote's centre is against the middle of the gap and moves the remote's holder by the difference, until it is
	/// close (or about a second and a half has gone). The camera's field of view and the window's shape are settled by then, which a
	/// placement at start was not: it put the remote half off the screen. If the mirror or the board cannot be found it falls back
	/// to <c>RemoteShift</c>.
	/// </summary>
	internal class RemotePlacer : MonoBehaviour
	{
		private const int MaxFrames = 90;

		private const float Tolerance = 0.004f;

		private NAutomaticGun gun;

		private int frames;

		private int settled;

		private bool logged;

		public void Begin(NAutomaticGun owner)
		{
			gun = owner;
		}

		private void LateUpdate()
		{
			if (gun == null || gun.body == null || gun.body.transform.parent == null)
			{
				Destroy(this);
				return;
			}
			frames++;
			if (gun.grabbed)
			{
				return;
			}
			GameObject eye = GameObject.Find("InteriorCamera");
			Camera cam = eye != null ? eye.GetComponent<Camera>() : null;
			if (cam == null || !MinigunItem.Measure(cam, gun, out float mirrorRight, out float boardLeft, out float bodyLeft, out float bodyRight))
			{
				if (frames >= 20)
				{
					Mod.Log.LogInfo("Remote: no mirror or board found to place it by, using RemoteShift");
					MinigunItem.Shift(gun);
					Destroy(this);
				}
				return;
			}
			float half = (bodyRight - bodyLeft) * 0.5f * MinigunItem.RemoteArtShare;
			float centre = (bodyLeft + bodyRight) * 0.5f;
			float want = (mirrorRight + boardLeft) * 0.5f;
			// Wider than the gap: keep it off the board and let it overlap the mirror's corner rather than the other way round.
			want = Mathf.Min(want, boardLeft - 0.012f - half);
			want = Mathf.Max(want, mirrorRight - 0.5f * half);
			float error = want - centre;
			if (!logged)
			{
				logged = true;
				Mod.Log.LogInfo($"Remote: viewport x of the mirror's right edge {mirrorRight:0.000}, the board's left edge {boardLeft:0.000}, the remote {bodyLeft:0.000}..{bodyRight:0.000} (art {half * 2f:0.000} wide), aiming for a centre of {want:0.000}, camera fov {cam.fieldOfView:0.0} aspect {cam.aspect:0.00}");
			}
			if (Mathf.Abs(error) < Tolerance)
			{
				settled++;
			}
			else
			{
				settled = 0;
				Transform holder = gun.body.transform.parent;
				Vector3 view = cam.WorldToViewportPoint(holder.position);
				view.x += error;
				holder.position = cam.ViewportToWorldPoint(view);
			}
			if (settled >= 3 || frames >= MaxFrames)
			{
				MinigunItem.Rest(gun);
				Mod.Log.LogInfo($"Remote placed after {frames} frames: centre {centre:0.000}, wanted {want:0.000}, holder at {gun.body.transform.parent.position}");
				Destroy(this);
			}
		}
	}
}
