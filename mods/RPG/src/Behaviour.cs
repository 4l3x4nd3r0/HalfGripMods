using HalfGripTools;
using UnityEngine;

namespace HalfGripRpg
{
	// The RPG takes the SMG's place in the car.
	internal sealed class RpgBehaviour : GunBehaviour
	{
		// As the SMG's gun component wakes with the RPG equipped, it is replaced by the RPG's own launcher and does not get to wake at all.
		public override bool BeforeAwake(Gun gun)
		{
			if (!(gun is NAutomaticGun) || !RpgItem.IsEquipped || gun.RequiredGunType != GunType.SMG_RIFLE)
			{
				return true;
			}
			Conversion.ConvertGun((NAutomaticGun)gun);
			return false;
		}

		// The same for the magazines in the rack: they become rockets before the rack counts and copies them.
		public override void BeforeRackAwake(MagazineManager rack)
		{
			if (RpgItem.IsEquipped)
			{
				Conversion.ConvertMagazines(rack);
			}
		}

		// A shot is one bullet fired along a ray. The RPG sends a rocket instead, so its shot skips the ray.
		public override bool BeforeFireRay(Gun gun, Vector3 origin, Vector3 direction, ref Vector3 end)
		{
			if (!(gun is RpgLauncher launcher))
			{
				return true;
			}
			end = launcher.LaunchRocket(origin, direction);
			return false;
		}
	}
}
