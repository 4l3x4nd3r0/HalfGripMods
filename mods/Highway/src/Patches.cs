using HarmonyLib;
using UnityEngine;

namespace HalfGripHighway
{
	/// <summary>Highway has its own track index; the scene to load is Stadium's.</summary>
	[HarmonyPatch(typeof(MenuManager), "TrackIndex")]
	internal static class TrackIndexPatch
	{
		private static bool Prefix(ref int __result)
		{
			if (Mod.Map != null && RunState.Active && RunState.HasSelection && RunState.Selected.map == Mod.Map)
			{
				__result = MapEntry.HostTrack;
				return false;
			}
			return true;
		}
	}

	/// <summary>TestMode: whatever node the player picked, the race is on Highway.</summary>
	[HarmonyPatch(typeof(RunState), nameof(RunState.BeginRace))]
	internal static class TestModePatch
	{
		private static void Prefix()
		{
			if (Mod.TestMode.Value && Mod.Map != null && RunState.HasSelection && !RunState.Over)
			{
				RunState.Selected.map = Mod.Map;
				RunState.Selected.variant = TrackVariant.Normal;
			}
		}
	}

	/// <summary>TestMode: a new run's whole map is Highway, so the run map shows it, with time limits that fit its length.</summary>
	[HarmonyPatch(typeof(RunMap), nameof(RunMap.Generate))]
	internal static class TestModeMapPatch
	{
		private static void Postfix(RunConfig config, RunMap __result)
		{
			if (!Mod.TestMode.Value || Mod.Map == null || __result == null || config == null)
			{
				return;
			}
			int columns = Mathf.Max(1, __result.columns.Count);
			foreach (RunNode node in __result.nodes)
			{
				node.map = Mod.Map;
				node.variant = TrackVariant.Normal;
				node.unknownMap = false;
				if (node.objective.kind == ObjectiveKind.Time)
				{
					float limit = Mod.Map.TimeLimit(Mathf.Min(2, node.column * 3 / columns));
					node.objective.amount = limit > 0f ? Mathf.CeilToInt(limit * config.timeLimitMargin) : 0;
				}
			}
			Mod.Log.LogInfo("TestMode: every node of the new run map is Highway");
		}
	}

	/// <summary>
	/// The game finds where a car is on the track by the nearest sample of the track line, in three dimensions. On Highway the lower
	/// road passes 9 m under the bridge, so a car on its far carriageway is nearer the bridge's line than its own. Heights count four
	/// times over here, which keeps every lap, position, AI and reset check on the road the car is really on.
	/// </summary>
	[HarmonyPatch(typeof(PathManager), nameof(PathManager.GetClosestPointIndex), new[] { typeof(Vector3) })]
	internal static class ClosestPointPatch
	{
		private static bool Prefix(PathManager __instance, Vector3 position, ref int __result)
		{
			return !HighwayScene.TryClosest(__instance, position, out __result);
		}
	}
}
