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