using HalfGripTools;
using UnityEngine.SceneManagement;

namespace HalfGripInfinitePlayground
{
	public class Mod : HalfGripMod
	{
		internal static ModLog Log;

		internal static ModSetting<bool> FloorEnabled;
		internal static ModSetting<float> HeightOffset;
		internal static ModSetting<float> Size;
		internal static ModSetting<float> TileSize;

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;

			FloorEnabled = ctx.Config.Bind("Floor", "Enabled", true, "Add an endless flat floor to the console's playground. Off leaves the playground as the game made it.");
			HeightOffset = ctx.Config.Bind("Floor", "HeightOffset", -0.05f, "Metres added to the measured ground height, so the playground's own roads stay on top and do not flicker.");
			Size = ctx.Config.Bind("Floor", "Size", 4000f, "Side of the floor square that follows the car, in metres.");
			TileSize = ctx.Config.Bind("Floor", "TileSize", 10f, "Side of one checker square, in metres, so speed can be seen.");

			// No patch: the loader's SceneReady event says when the playground (or any scene) has loaded.
			ctx.Events.SceneReady += OnSceneReady;
		}

		// The wheels cast against every layer except Ignore Raycast (CarControllerNew.CastGround), and a collider with no HandlingModifier
		// gives plain grip, so a box collider is drivable road. Hold R still resets to the playground's own roads.
		private static void OnSceneReady(Scene scene)
		{
			Playground playground = UnityEngine.Object.FindFirstObjectByType<Playground>();
			if (playground == null)
			{
				PlayerResetLogic.noHints = false; // left the playground
				return;
			}
			if (!FloorEnabled.Value || playground.GetComponent<EndlessFloor>() != null)
			{
				return;
			}
			// The car is more than 1000 m from the path soon after leaving, which would nag "Hold R to reset".
			PlayerResetLogic.noHints = true;
			playground.gameObject.AddComponent<EndlessFloor>();
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
