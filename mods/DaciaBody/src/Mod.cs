using BepInEx.Configuration;
using BepInEx.Logging;
using HalfGripTools;

namespace HalfGripDaciaBody
{
	public class Mod : HalfGripMod
	{
		internal static ManualLogSource Log;

		internal static ModContext Ctx;

		internal static ConfigEntry<bool> DumpInfo;

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;
			Ctx = ctx;

			DumpInfo = ctx.Config.Bind("Debug", "DumpInfo", true, "Write every body (chassis) item and the one the Dacia is copied from to the BepInEx log.");
			Body.BindConfig(ctx.Config);
			Body.Register(ctx);
			ctx.PatchAll();
		}
	}
}
