using HarmonyLib;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>The map has its own track index; the scene to load is Stadium's.</summary>
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

	/// <summary>TestMode: whatever node the player picked, the race is on Temple of Drift.</summary>
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

	/// <summary>TestMode: a new run's whole map is Temple of Drift, so the run map shows it, with time limits that fit its length.</summary>
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
			Mod.Log.LogInfo("TestMode: every node of the new run map is Temple of Drift");
		}
	}
}
