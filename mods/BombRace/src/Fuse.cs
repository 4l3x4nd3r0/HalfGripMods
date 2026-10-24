using UnityEngine;

namespace HalfGripBombRace
{
	/// <summary>
	/// One car's bomb. Under the limit the fuse burns and the red bulb flashes, faster the shorter the fuse; back over the limit the
	/// fuse burns down again and the green bulb flashes at the same rate it would have flashed red at that point.
	/// </summary>
	internal class FuseState
	{
		// How bright a bulb is when it is not flashing, and the green one's steady glow when the bomb is armed and the fuse is out.
		internal const float Dim = 0.1f;

		private const float SafeGlow = 0.4f;

		// Seconds per flash with the fuse just lit, and with it nearly burnt through.
		private const float SlowPeriod = 0.8f;

		private const float FastPeriod = 0.1f;

		internal float Fuse;

		internal bool Under;

		internal float Red = Dim;

		internal float Green = Dim;

		/// <summary>True for the one frame a red flash starts: when to beep.</summary>
		internal bool Ticked;

		private float phase = 1f;

		/// <summary>Moves the fuse on by one frame. Returns true when it has burnt right through.</summary>
		internal bool Step(float dt, float speed, float limit, float fuseTime, bool armed)
		{
			Ticked = false;
			if (!armed)
			{
				Reset();
				return false;
			}
			fuseTime = Mathf.Max(0.1f, fuseTime);
			Under = speed < limit;
			Fuse = Mathf.Clamp(Fuse + (Under ? dt : -dt), 0f, fuseTime);

			if (Fuse <= 0f)
			{
				// Nothing burning: wait with a flash ready for the moment it starts.
				phase = 1f;
				Red = Dim;
				Green = Under ? Dim : SafeGlow;
				return false;
			}

			float period = Mathf.Lerp(SlowPeriod, FastPeriod, Fuse / fuseTime);
			phase += dt / period;
			if (phase >= 1f)
			{
				phase -= Mathf.Floor(phase);
				Ticked = Under;
			}
			float flash = Dim + (1f - Dim) * Mathf.Exp(-6f * phase);
			Red = Under ? flash : Dim;
			Green = Under ? Dim : flash;
			return Fuse >= fuseTime;
		}

		/// <summary>Not armed (grace period, race over, car gone): fuse out, both bulbs dim.</summary>
		internal void Reset()
		{
			Fuse = 0f;
			Under = false;
			phase = 1f;
			Red = Dim;
			Green = Dim;
		}

		internal void Off()
		{
			Reset();
			Red = 0f;
			Green = 0f;
		}
	}
}
