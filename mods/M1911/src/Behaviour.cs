using HalfGripTools;

namespace HalfGrip1911
{
	// The M1911 rides on the SMG's gun object. When it is the fitted item, make that gun semi-auto and redress it.
	internal sealed class M1911Behaviour : GunBehaviour
	{
		public override void AfterStart(Gun gun)
		{
			if (gun is NAutomaticGun smg && M1911Item.IsFitted(smg))
			{
				M1911Item.Equip(smg);
			}
		}
	}
}
