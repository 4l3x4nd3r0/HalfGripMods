using BepInEx.Logging;
using HalfGripTools;
using UnityEngine;

namespace Multiplayer
{
	public class Mod : HalfGripMod
	{
		internal static ManualLogSource Log;
		private static GameObject runner;
		internal static ModContext Ctx;
		/// <summary>True on the frame the spectate key (Tab, rebindable in Settings > Controls) goes down.</summary>
		internal static System.Func<bool> SpectateNext = () => Input.GetKeyDown(KeyCode.Tab);
		/// <summary>True on the frame the chat key (H by default, rebindable in Settings > Controls > Multiplayer) goes down.</summary>
		internal static System.Func<bool> ChatKey = () => Input.GetKeyDown(KeyCode.H);
		internal const string ChatLabel = "Chat";
		internal static string ChatKeyName() { return ModKeyShim.NameOf(ChatLabel, "H"); }

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;
			Ctx = ctx;
			if (!ModKeyShim.Supported(ctx))
			{
				Log.LogError("Multiplayer needs HalfGripTools 1.0.2 or newer (this one is older), so it is not loaded. Update HalfGripTools.");
				return;
			}
			// both keys have their own "Multiplayer" mini section in Settings > Controls (the Race section if the game's list cannot be extended)
			Controls.Section keySection = KeySection.Supported ? KeySection.Own : Controls.Section.Race;
			SpectateNext = ModKeyShim.Add(ctx, KeyCode.Tab, "Spectate: next car", keySection);
			ChatKey = ModKeyShim.Add(ctx, KeyCode.H, ChatLabel, keySection);
			Pvp.Bind(ctx.Config);
			Rig.Init();
			if (Rig.On)
			{
				SaveSandbox.ReadOnly = true; // a test copy of the game must never write the real save
			}
			ctx.PatchAll();
			Tx.Merge(Loc.Language); // the game may have built its text table before this patch existed
			runner = new GameObject("MultiplayerRunner");
			Object.DontDestroyOnLoad(runner);
			runner.AddComponent<Runner>();
			RigRunner.Start();
			Log.LogInfo("Multiplayer " + ctx.Manifest.version + " loaded.");
		}

		public override void Unload()
		{
			Ui.ReleaseInputs(); Pause.Release(); Cover.Remove(); Garage.Solo = false; Room.Shutdown();
			MenuHook.Remove();
			GarageUi.Remove();
			Hud.Remove();
			RaceSync.Remove();
			Cues.Remove();
			Chat.Remove();
			SaveSandbox.Leave();
			Restore.AfterRoom();
			if (runner != null)
			{
				Object.Destroy(runner);
			}
		}
	}

	/// <summary>Drives the mod once a frame, in every scene: the network, the menu screen, the race.</summary>
	internal sealed class Runner : MonoBehaviour
	{
		private void Update()
		{
			try
			{
				MenuHook.Tick();
				Ui.TickInputs();
				Room.Tick();
				RaceSync.Tick(); Pause.Tick(); Chat.Tick(); Skins.Tick(); Garage.Tick(); Cover.Tick();
			}
			catch (System.Exception e)
			{
				Mod.Log.LogError("Multiplayer update failed: " + e);
			}
		}
	}
}
