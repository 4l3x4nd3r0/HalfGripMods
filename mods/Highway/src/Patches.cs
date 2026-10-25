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

	/// <summary>Remembers which car the lap manager is measuring, so <see cref="LapCounterPatch"/> knows where it is.</summary>
	[HarmonyPatch(typeof(LapManager), "GetCarProjectedPoint")]
	internal static class MeasureScope
	{
		private static void Prefix(GameObject car)
		{
			HighwayScene.MeasuredCar = HighwayScene.Loaded ? car : null;
		}

		private static void Finalizer()
		{
			HighwayScene.MeasuredCar = null;
		}
	}

	/// <summary>
	/// The game reads a change of nearest track sample as the shorter way round the loop. A dirt shortcut jumps about half a lap, so one of
	/// them (the east road) read as driving backwards: a backwards lap was counted and the next line crossing only paid it back. On a dirt
	/// road a big jump is read from the car's own direction instead: along the road is forward, against it is backward.
	/// </summary>
	[HarmonyPatch(typeof(LapManager), "LapCounter")]
	internal static class LapCounterPatch
	{
		private static bool Prefix(int prevIndex, int currentIndex, int count, int lapCount, int backwardsLaps, ref (int, int, int) __result)
		{
			GameObject car = HighwayScene.MeasuredCar;
			if (car == null || count <= 0)
			{
				return true;
			}
			int forward = (currentIndex - prevIndex + count) % count;
			if (Mathf.Min(forward, count - forward) <= count / 4)
			{
				return true;
			}
			if (!HighwayScene.OnDirt(car.transform.position, out Vector3 along))
			{
				return true;
			}
			Rigidbody body = car.GetComponent<Rigidbody>();
			Vector3 heading = body != null && body.linearVelocity.sqrMagnitude > 1f ? body.linearVelocity : car.transform.forward;
			heading.y = 0f;
			bool ahead = Vector3.Dot(heading.normalized, along) >= 0f;
			int step = ahead ? forward : forward - count;
			if (ahead && currentIndex < prevIndex)
			{
				if (backwardsLaps == 0)
				{
					lapCount++;
				}
				else
				{
					backwardsLaps--;
				}
			}
			else if (!ahead && currentIndex > prevIndex)
			{
				backwardsLaps++;
			}
			__result = (step, lapCount, backwardsLaps);
			return false;
		}
	}

	/// <summary>
	/// The "Wrong way" hint compares the car's heading with the nearest sample of the racing line. The north dirt road runs where the real
	/// south-west ramp's mirror image is, so its nearest samples point the other way. On dirt the road's own direction is used.
	/// </summary>
	[HarmonyPatch(typeof(PlayerResetLogic), "IsGoingBackwards")]
	internal static class WrongWayPatch
	{
		private static bool Prefix(PlayerResetLogic __instance, CarControllerNew ___player, ref bool __result)
		{
			if (!HighwayScene.Loaded || ___player == null || ___player.rb == null
				|| !HighwayScene.OnDirt(___player.transform.position, out Vector3 along))
			{
				return true;
			}
			if (___player.debugSpeed < __instance.wrongWaySpeed || ___player.forwardSpeed < 0f)
			{
				__result = false;
				return false;
			}
			Vector3 velocity = ___player.rb.linearVelocity;
			velocity.y = 0f;
			__result = Vector3.Dot(velocity.normalized, along) < -0.5f;
			return false;
		}
	}
}
