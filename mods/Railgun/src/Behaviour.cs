using HalfGripTools;
using UnityEngine;

namespace HalfGripRailgun
{
	internal sealed class RailgunBehaviour : GunBehaviour
	{
		private static int logged;

		// The charge the shot is on and the damage it swapped out of the fitted item (null when it did not swap).
		private GunUpgrade item;

		private float itemDamage;

		// The pump shotgun keeps doing the loading and chambering; with the Railgun fitted a RailCharge is put on it as it wakes.
		public override void AfterAwake(Gun gun)
		{
			if (gun is NPumpActionGun pump && RailgunItem.IsEquipped && pump.RequiredGunType == GunType.SHOTGUN && pump.GetComponent<RailCharge>() == null)
			{
				pump.gameObject.AddComponent<RailCharge>().Setup(pump);
			}
		}

		// A shot's damage is the fitted item's damage (through Gun.Damage), so for the length of the shot the item's damage is the
		// charged damage, and it is put back afterwards, even if the shot throws. The beam's width follows the charge. The item that is
		// changed is the one the gun itself reads (Gun.Fitted), not a copy kept by the mod, so the shot cannot silently keep the card's
		// full damage.
		public override bool BeforeFireRay(Gun gun, Vector3 origin, Vector3 direction, ref Vector3 end)
		{
			item = null;
			RailCharge charge = gun.GetComponent<RailCharge>();
			GunUpgrade fitted = gun.Fitted;
			if (charge == null || fitted == null)
			{
				return true;
			}
			item = fitted;
			itemDamage = fitted.damage;
			fitted.damage = charge.ShotDamage;
			charge.ShapeBeam();
			// The first few shots say what really left the gun: the charged damage, and what the game then works out from it.
			if (Mod.DumpInfo.Value && logged < 12)
			{
				logged++;
				Mod.Log.LogInfo($"Railgun shot: charge {charge.Charge:0.00}, item damage {fitted.damage:0.#} (card {itemDamage:0.#}), Gun.Damage {gun.Damage:0.#}, same item as the mod's: {ReferenceEquals(fitted, RailgunItem.Item)}");
			}
			return true;
		}

		public override void AfterFireRay(Gun gun)
		{
			if (item == null)
			{
				return;
			}
			item.damage = itemDamage;
			item = null;
			RailCharge charge = gun.GetComponent<RailCharge>();
			if (charge != null)
			{
				charge.Spend();
			}
		}
	}
}
