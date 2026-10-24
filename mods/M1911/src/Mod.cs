using BepInEx.Configuration;
using BepInEx.Logging;
using HalfGripTools;

namespace HalfGrip1911
{
	public class Mod : HalfGripMod
	{
		internal static ManualLogSource Log;

		internal static ModContext Ctx;

		internal static ConfigEntry<float> Damage;

		internal static ConfigEntry<float> FireRate;

		internal static ConfigEntry<float> Recoil;

		internal static ConfigEntry<float> Inaccuracy;

		internal static ConfigEntry<int> Capacity;

		internal static ConfigEntry<bool> DumpInfo;

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;
			Ctx = ctx;

			Damage = ctx.Config.Bind("M1911", "DamagePerShot", 33f, "Damage per shot. 0 = halfway between the SMG and the revolver.");
			FireRate = ctx.Config.Bind("M1911", "FireRate", 4f, "Highest shots per second. It is semi-auto, so this is the cap on how fast you can click.");
			Recoil = ctx.Config.Bind("M1911", "Recoil", 0f, "Crosshair jump and gun kick per shot. 0 = halfway between the SMG and the revolver.");
			Inaccuracy = ctx.Config.Bind("M1911", "Inaccuracy", 0f, "Degrees the shot wobbles by. 0 = halfway between the SMG and the revolver.");
			Capacity = ctx.Config.Bind("M1911", "Capacity", 8, "Rounds in one magazine.");
			DumpInfo = ctx.Config.Bind("Debug", "DumpInfo", true, "Write every gun item and the SMG's sprite layout to the BepInEx log. Useful when tuning.");

			M1911Item.Register(ctx);
			ctx.PatchAll();
		}
	}
}
