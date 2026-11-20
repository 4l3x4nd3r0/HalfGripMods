using System;
using HalfGripTools;
using UnityEngine;

namespace HalfGripMinigun
{
	internal sealed class MinigunBehaviour : GunBehaviour
	{
		// The Minigun rides on the SMG's gun object. When it is the fitted item, the gun is redressed and given a heat meter.
		public override void AfterStart(Gun gun)
		{
			if (gun is NAutomaticGun smg && MinigunItem.IsFitted(smg))
			{
				MinigunItem.Equip(smg);
			}
		}

		// The remote can be raised unless the gun is jammed: there is no chamber to be empty.
		public override bool CanAim(Gun gun, ref bool result)
		{
			if (!MinigunItem.IsFitted(gun))
			{
				return false;
			}
			MiniHeat heat = gun.GetComponent<MiniHeat>();
			result = heat == null || heat.CanAim;
			return true;
		}

		// Never "out of ammo": the game's checks for a full gun and for rounds left would otherwise look at the (missing) magazine.
		public override bool IsFull(Gun gun, ref bool result)
		{
			if (!MinigunItem.IsFitted(gun))
			{
				return false;
			}
			result = true;
			return true;
		}

		public override bool ShotsLeft(Gun gun, ref int result)
		{
			if (!MinigunItem.IsFitted(gun))
			{
				return false;
			}
			result = int.MaxValue;
			return true;
		}

		// The trigger: the game calls Shoot every frame the button is held; the heat meter decides whether a bullet leaves.
		public override bool BeforeShoot(Gun gun)
		{
			if (!(gun is NAutomaticGun) || !MinigunItem.IsFitted(gun))
			{
				return true;
			}
			MiniHeat heat = gun.GetComponent<MiniHeat>();
			if (heat != null)
			{
				heat.Pull();
			}
			return false;
		}

		// The rack that held the SMG's magazines holds nothing for the Minigun: its rounds are removed as the race starts, and a heat
		// bar is drawn where they sat.
		public override void AfterRackStart(MagazineManager rack)
		{
			if (!Owns(rack))
			{
				return;
			}
			try
			{
				Empty(rack);
			}
			catch (Exception e)
			{
				Mod.Log.LogError("Could not clear the magazine rack for the Minigun: " + e);
			}
		}

		// Magazines the game would put in the rack (ammo boxes, kills, refills) cool the gun instead. Ammo boxes are the only way
		// to take heat off while firing.
		public override bool GrantMagazines(MagazineManager rack, int magazines, ref int taken)
		{
			if (!Owns(rack))
			{
				return false;
			}
			int wanted = Mathf.Max(1, Mathf.RoundToInt(Stats.Get(Stat.AmmoPickup, magazines)));
			MiniHeat heat = MiniHeat.Current;
			taken = heat != null && heat.CoolByMagazines(wanted) ? wanted : 0;
			return true;
		}

		public override bool SpawnMagazines(MagazineManager rack, ref int made)
		{
			if (!Owns(rack))
			{
				return false;
			}
			made = 0;
			return true;
		}

		public override bool SpawnMagazineObjects(MagazineManager rack, ref int made)
		{
			if (!Owns(rack))
			{
				return false;
			}
			made = 0;
			return true;
		}

		private static bool Owns(MagazineManager rack)
		{
			if (MinigunItem.Item == null)
			{
				return false;
			}
			Gun owner = OwnerOf(rack);
			return owner != null && owner.Fitted == MinigunItem.Item;
		}

		private static void Empty(MagazineManager rack)
		{
			bool any = EmptyRack(rack, out Bounds area, out int layer, out int sortingLayer, out int order);
			if (any)
			{
				HeatBar.Make(rack.transform, area, layer, sortingLayer, order + 1);
			}
			// The padlock the game puts on the rack for the opening ceasefire goes onto the remote instead, as it does for the
			// blood-fed SMG (the Bloodletter), which has no rack either.
			if (!CallRack(rack, "RideLockOnGun"))
			{
				Mod.Log.LogWarning("The rack had no magazines to take the heat bar's place from, so there is no bar");
			}
			Mod.Log.LogInfo($"Emptied rack '{rack.name}' for the Minigun (bar {(any ? area.size.ToString() : "none")})");
		}
	}
}
