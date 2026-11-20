using HalfGripTools;
using UnityEngine;

namespace JudgmentDay
{
	public class Mod : HalfGripMod
	{
		internal static ModLog Log;

		internal static ModContext Ctx;

		internal static ModSetting<float> CostMultiplier;

		internal static ModSetting<float> Damage;

		internal static ModSetting<int> TubeShells;

		internal static ModSetting<int> StartShells;

		internal static ModSetting<int> ShellsPerPickup;

		internal static ModSetting<float> TurnsPerCock;

		internal static ModSetting<float> SpinWindow;

		internal static ModSetting<bool> OpenToLoad;

		internal static ModSetting<float> FlipSeconds;

		internal static ModSetting<float> LeverAngle;

		internal static ModSetting<float> PivotBack;

		internal static ModSetting<float> SpringShare;

		internal static ModSetting<float> LoadedHold;

		internal static ModSetting<float> LeverDrag;

		internal static ModSetting<float> GrabMargin;

		internal static ModSetting<bool> DragDown;

		internal static ModSetting<bool> DumpInfo;

		internal static ModSetting<int> ConfigVersion;

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;
			Ctx = ctx;

			CostMultiplier = ctx.Config.Bind("Judgment Day", "CostMultiplier", 2f, "Shop price as a multiple of the pump shotgun's price.");
			Damage = ctx.Config.Bind("Judgment Day", "Damage", 50f, "Damage of a shot (the pump shotgun does 60). Takes effect for a new run.");
			TubeShells = ctx.Config.Bind("Judgment Day", "TubeShells", 5, "Shells in the tube once a round is chambered. The gun carries this plus one in all (the pump shotgun carries 4); with the lever open all of them can be loaded before the first is chambered.");
			StartShells = ctx.Config.Bind("Judgment Day", "StartShells", 6, "Spare shells on the dash (the rack) at the start of a race (the pump shotgun has 4).");
			ShellsPerPickup = ctx.Config.Bind("Judgment Day", "ShellsPerPickup", 3, "Shells an ammo pickup or a kill refill gives (the pump shotgun gives 1).");
			TurnsPerCock = ctx.Config.Bind("Judgment Day", "TurnsPerCock", 0.6f, "How many turns of the mouse's direction of travel cock the gun: 1 is one full circle, big or small. Raise it if the gun cocks too easily.");
			SpinWindow = ctx.Config.Bind("Judgment Day", "SpinWindow", 1.5f, "The circle must be made in one direction within this many seconds, or the count starts again. Swinging the mouse side to side never counts.");
			OpenToLoad = ctx.Config.Bind("Judgment Day", "OpenToLoad", true, "Shells only go in while the lever is open, as on the real 1887, and the lever stays down once it is pulled open with the chamber empty. Off = load any time like the pump shotgun.");
			FlipSeconds = ctx.Config.Bind("Judgment Day", "FlipSeconds", 0.45f, "How long the one-handed flip takes. The round is chambered halfway through and you can fire from then on.");
			LeverAngle = ctx.Config.Bind("Judgment Day", "LeverAngle", 50f, "Degrees the lever swings open (down and forward) when pulled all the way. The real 1887 opens about 70.");
			PivotBack = ctx.Config.Bind("Judgment Day", "FlipPivotBack", 0.15f, "Where the held gun flips about: this share of its length behind its middle (the lever is near the back).");
			SpringShare = ctx.Config.Bind("Judgment Day", "LeverSpring", 0.85f, "How hard the lever is pulled back to rest, as a share of the pump shotgun's spring. Lower = lighter to work by hand.");
			LoadedHold = ctx.Config.Bind("Judgment Day", "LoadedHold", 1.5f, "Extra spring on the lever while a shell is chambered (the pump shotgun uses 10, so it cannot be opened on a live shell). 1 = none.");
			LeverDrag = ctx.Config.Bind("Judgment Day", "LeverDrag", 0.15f, "How far the mouse moves (as a share of the screen height) to pull the lever all the way open. Lower = a shorter drag works it. The lever follows the mouse's up and down movement, down opens it, up closes it.");
			GrabMargin = ctx.Config.Bind("Judgment Day", "GrabMargin", 0.8f, "Extra grab margin round the lever's handle (the bottom of the loop), in game units. Higher = easier to catch with the cursor.");
			DragDown = ctx.Config.Bind("Judgment Day", "DragDown", true, "Work the lever by dragging down (it swings down). Off = drag along the gun like the pump.");
			DumpInfo = ctx.Config.Bind("Debug", "DumpInfo", true, "Write the game's gun items, the gun's parts and each spin-cock to the log. Useful when tuning.");

			// The config file keeps a value already in it when a default changes, so after a retune the old numbers would linger.
			ConfigVersion = ctx.Config.Bind("Debug", "ConfigVersion", 0, "Used by the plugin to reset retuned defaults after an update. Leave it.");
			if (ConfigVersion.Value < 16)
			{
				TurnsPerCock.Value = (float)TurnsPerCock.DefaultValue;
				StartShells.Value = (int)StartShells.DefaultValue;
				ShellsPerPickup.Value = (int)ShellsPerPickup.DefaultValue;
				SpinWindow.Value = (float)SpinWindow.DefaultValue;
				FlipSeconds.Value = (float)FlipSeconds.DefaultValue;
				LeverAngle.Value = (float)LeverAngle.DefaultValue;
				PivotBack.Value = (float)PivotBack.DefaultValue;
				SpringShare.Value = (float)SpringShare.DefaultValue;
				LoadedHold.Value = (float)LoadedHold.DefaultValue;
				LeverDrag.Value = (float)LeverDrag.DefaultValue;
				Damage.Value = (float)Damage.DefaultValue;
				GrabMargin.Value = (float)GrabMargin.DefaultValue;
				ConfigVersion.Value = 16;
				ctx.Config.Save();
			}

			JudgmentDayItem.Register(ctx);
			ctx.Guns.Add(new JudgmentDayBehaviour());
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
