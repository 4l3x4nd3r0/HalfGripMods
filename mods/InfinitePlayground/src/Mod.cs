using BepInEx.Configuration;
using BepInEx.Logging;
using HalfGripTools;

namespace HalfGripInfinitePlayground
{
	public class Mod : HalfGripMod
	{
		internal static ManualLogSource Log;

		internal static ConfigEntry<bool> FloorEnabled;
		internal static ConfigEntry<float> HeightOffset;
		internal static ConfigEntry<float> Size;
		internal static ConfigEntry<float> TileSize;

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;

			FloorEnabled = ctx.Config.Bind("Floor", "Enabled", true, "Add an endless flat floor to the console's playground. Off leaves the playground as the game made it.");
			HeightOffset = ctx.Config.Bind("Floor", "HeightOffset", -0.05f, "Metres added to the measured ground height, so the playground's own roads stay on top and do not flicker.");
			Size = ctx.Config.Bind("Floor", "Size", 4000f, "Side of the floor square that follows the car, in metres.");
			TileSize = ctx.Config.Bind("Floor", "TileSize", 10f, "Side of one checker square, in metres, so speed can be seen.");

			// One class at a time, so a patch that does not fit this game build is logged and skipped, not the whole mod.
			ctx.PatchAll();
		}

		public override void Unload()
		{
			PlayerResetLogic.noHints = false;
			foreach (EndlessFloor floor in UnityEngine.Object.FindObjectsByType<EndlessFloor>(UnityEngine.FindObjectsSortMode.None))
			{
				UnityEngine.Object.Destroy(floor.gameObject);
			}
		}
	}
}
