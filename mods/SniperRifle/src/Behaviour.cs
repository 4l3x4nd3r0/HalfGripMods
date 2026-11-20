using HalfGripTools;
using UnityEngine;

namespace HalfGripSniper
{
	// The rifle takes the SMG's place in the car.
	internal sealed class SniperBehaviour : GunBehaviour
	{
		// The gun's damage the shot swapped out, and whether it swapped one (a shot cannot start another before its own ray is done).
		private bool swapped;

		private float saved;

		// As the SMG's gun component wakes with the rifle equipped, it is replaced by the rifle's own gun and does not get to wake at all.
		public override bool BeforeAwake(Gun gun)
		{
			if (!(gun is NAutomaticGun) || !RifleItem.IsEquipped || gun.RequiredGunType != GunType.SMG_RIFLE)
			{
				return true;
			}
			Conversion.ConvertGun((NAutomaticGun)gun);
			return false;
		}

		// The same for the magazines in the rack: they become cartridges before the rack counts and copies them.
		public override void BeforeRackAwake(MagazineManager rack)
		{
			if (RifleItem.IsEquipped)
			{
				Conversion.ConvertMagazines(rack);
			}
		}

		// A shot's damage is the fitted item's damage (through Gun.Damage), so for the length of the shot the item's damage is the
		// charged damage, and it is put back afterwards, even if the shot throws.
		public override bool BeforeFireRay(Gun gun, Vector3 origin, Vector3 direction, ref Vector3 end)
		{
			if (gun is SniperGun rifle && RifleItem.Item != null)
			{
				saved = RifleItem.Item.damage;
				swapped = true;
				RifleItem.Item.damage = rifle.ShotDamage;
			}
			return true;
		}

		public override void AfterFireRay(Gun gun)
		{
			if (swapped && RifleItem.Item != null)
			{
				RifleItem.Item.damage = saved;
			}
			swapped = false;
		}
	}
}
