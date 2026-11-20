using HalfGripTools;

namespace JudgmentDay
{
	internal sealed class JudgmentDayBehaviour : GunBehaviour
	{
		// The pump shotgun keeps doing the loading and chambering; with Judgment Day fitted a Lever and a SpinCock are put on it as it wakes.
		public override void AfterAwake(Gun gun)
		{
			if (gun is NPumpActionGun pump && JudgmentDayItem.IsEquipped && pump.RequiredGunType == GunType.SHOTGUN && pump.GetComponent<SpinCock>() == null)
			{
				pump.gameObject.AddComponent<Lever>().Setup(pump);
				pump.gameObject.AddComponent<SpinCock>().Setup(pump);
			}
		}

		// No firing while the gun is flipping over, until the round is in the chamber.
		public override bool BeforeShoot(Gun gun)
		{
			if (!(gun is NPumpActionGun))
			{
				return true;
			}
			SpinCock spin = gun.GetComponent<SpinCock>();
			return spin == null || spin.CanFire;
		}

		// The lever stays down once it has been pulled all the way open with the chamber empty, so there are two hands' worth of work done with
		// one cursor: pull it open, drop the shells in, push it shut (which chambers the next one). The game's pump would spring shut at once
		// whenever there are shells to feed.
		public override void AfterUpdatePump(NPumpActionGun gun)
		{
			if (Mod.OpenToLoad.Value && gun.racked && !gun.loaded && gun.pump != null && !gun.pump.IsGrabbed
				&& gun.GetComponent<SpinCock>() != null)
			{
				gun.pump.targetValue = 1f;
			}
			// Open the breach on a live round to load more: the lever can be dragged open even with a round chambered (the game would hold it
			// shut), and the round goes back into the tube.
			if (Mod.OpenToLoad.Value && gun.loaded && gun.pump != null && gun.pump.IsGrabbed && gun.pump.value > 0.6f
				&& gun.GetComponent<SpinCock>() != null)
			{
				gun.loaded = false;
				gun.bullets++;
				gun.racked = true;
				SoundController.PlayRandomSound2D(gun.reloadPullSounds, gun.gameObject);
				if (Mod.DumpInfo.Value)
				{
					Mod.Log.LogInfo($"Judgment Day: breach opened on a live round, bullets now {gun.bullets}");
				}
			}
		}

		// With a round chambered the game turns a grab on the pump into "pick the gun up to aim" (the pump is one of the gun's aim parts). The
		// lever of this gun has to be workable then (to open the breach and load more), so only the rest of the gun picks it up.
		public override void AfterPartsAim(NPumpActionGun gun, ref bool result)
		{
			if (result && Mod.OpenToLoad.Value && gun.GetComponent<SpinCock>() != null)
			{
				result = false;
			}
		}
	}
}
