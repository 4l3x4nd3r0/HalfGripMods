using BepInEx.Configuration;
using BepInEx.Logging;
using HalfGripTools;
using UnityEngine;

namespace JudgmentDay
{
	public class Mod : HalfGripMod
	{
		internal static ManualLogSource Log;

		internal static ModContext Ctx;

		internal static ConfigEntry<float> CostMultiplier;

		internal static ConfigEntry<float> TurnsPerCock;

		internal static ConfigEntry<float> FlipSeconds;

		internal static ConfigEntry<float> LeverAngle;

		internal static ConfigEntry<float> PivotBack;

		internal static ConfigEntry<float> SpringShare;

		internal static ConfigEntry<float> LoadedHold;

		internal static ConfigEntry<float> LeverReach;

		internal static ConfigEntry<float> GrabMargin;

		internal static ConfigEntry<bool> DragDown;

		internal static ConfigEntry<bool> DumpInfo;

		internal static ConfigEntry<int> ConfigVersion;

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;
			Ctx = ctx;

			CostMultiplier = ctx.Config.Bind("Judgment Day", "CostMultiplier", 2f, "Shop price as a multiple of the pump shotgun's price.");
			TurnsPerCock = ctx.Config.Bind("Judgment Day", "TurnsPerCock", 1f, "How many turns of the aim's swinging weight (the game's own gun-spin trick) cock the gun. One turn is about one quick circle of the mouse; raise it if the gun cocks too easily.");
			FlipSeconds = ctx.Config.Bind("Judgment Day", "FlipSeconds", 0.8f, "How long the one-handed flip takes. You cannot fire while it runs.");
			LeverAngle = ctx.Config.Bind("Judgment Day", "LeverAngle", 65f, "Degrees the lever swings open (down and forward) when pulled all the way. The real 1887 opens about 70.");
			PivotBack = ctx.Config.Bind("Judgment Day", "FlipPivotBack", 0.15f, "Where the held gun flips about: this share of its length behind its middle (the lever is near the back).");
			SpringShare = ctx.Config.Bind("Judgment Day", "LeverSpring", 0.35f, "How hard the lever is pulled back to rest, as a share of the pump shotgun's spring. Lower = lighter to work by hand.");
			LoadedHold = ctx.Config.Bind("Judgment Day", "LoadedHold", 1.5f, "Extra spring on the lever while a shell is chambered (the pump shotgun uses 10, so it cannot be opened on a live shell). 1 = none.");
			LeverReach = ctx.Config.Bind("Judgment Day", "LeverReach", 1.6f, "How far the lever moves for a drag of the mouse, compared with the pump (1). Higher = a shorter drag works the lever.");
			GrabMargin = ctx.Config.Bind("Judgment Day", "GrabMargin", 1.5f, "Extra grab margin round the lever, in game units (the lever is about 4 units wide). Higher = easier to catch with the cursor.");
			DragDown = ctx.Config.Bind("Judgment Day", "DragDown", true, "Work the lever by dragging down (it swings down). Off = drag along the gun like the pump.");
			DumpInfo = ctx.Config.Bind("Debug", "DumpInfo", true, "Write the game's gun items, the gun's parts and each spin-cock to the BepInEx log. Useful when tuning.");

			// BepInEx keeps a value already in the config file when a default changes, so after a retune the old numbers would linger.
			ConfigVersion = ctx.Config.Bind("Debug", "ConfigVersion", 0, "Used by the plugin to reset retuned defaults after an update. Leave it.");
			if (ConfigVersion.Value < 5)
			{
				FlipSeconds.Value = (float)FlipSeconds.DefaultValue;
				LeverAngle.Value = (float)LeverAngle.DefaultValue;
				PivotBack.Value = (float)PivotBack.DefaultValue;
				SpringShare.Value = (float)SpringShare.DefaultValue;
				LoadedHold.Value = (float)LoadedHold.DefaultValue;
				LeverReach.Value = (float)LeverReach.DefaultValue;
				GrabMargin.Value = (float)GrabMargin.DefaultValue;
				ConfigVersion.Value = 5;
				ctx.Config.Save();
			}

			JudgmentDayItem.Register(ctx);
			ctx.PatchAll();
		}

		public override void Unload()
		{
			foreach (SpinCock spin in Object.FindObjectsByType<SpinCock>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				Object.Destroy(spin);
			}
			foreach (Lever lever in Object.FindObjectsByType<Lever>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				lever.Remove();
				Object.Destroy(lever);
			}
		}
	}
}
