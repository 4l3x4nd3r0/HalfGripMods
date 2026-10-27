using HarmonyLib;

namespace Multiplayer
{
	/// <summary>Whenever the game starts loading the main menu while a room or the garage is open, the screen goes black first: the menu is then opened on the MULTIPLAYER screen unseen.</summary>
	internal static class MenuReturn
	{
		internal static void Check(string sceneName)
		{
			if (sceneName == Flow.MenuScene && (Garage.On || Room.Active || Garage.ReturnToScreen))
			{
				Cover.ShowForMenu();
			}
		}
	}

	// After leaving a room from a race, nothing but the menu may load (the race scene's own end would open the shop).
	[HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.LoadSceneWithEffects))]
	internal static class LeavingEffectsLoad
	{
		[HarmonyPriority(Priority.First)]
		private static bool Prefix(string sceneName)
		{
			return !Room.Leaving || sceneName == Flow.MenuScene;
		}
	}

	[HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.LoadScene), new[] { typeof(string) })]
	internal static class LeavingPlainLoad
	{
		[HarmonyPriority(Priority.First)]
		private static bool Prefix(string sceneName)
		{
			return !Room.Leaving || sceneName == Flow.MenuScene;
		}
	}

	[HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.LoadSceneWithEffects))]
	internal static class CoverEffectsLoad
	{
		private static void Prefix(string sceneName)
		{
			MenuReturn.Check(sceneName);
		}
	}

	[HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.LoadScene), new[] { typeof(string) })]
	internal static class CoverPlainLoad
	{
		private static void Prefix(string sceneName)
		{
			MenuReturn.Check(sceneName);
		}
	}
}