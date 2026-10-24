namespace HalfGripBombRace
{
	/// <summary>
	/// A Bomb race is a "Just drive" objective (kind None, which the game counts as met as long as you finish alive) carrying a tag in its
	/// <c>target</c>. The game saves <c>target</c> with the run, and rejects a saved run whose objective kind it does not know, so a new
	/// <c>ObjectiveKind</c> would lose the run without the mod. The first version tagged a "Don't be last" objective, so that still counts.
	/// </summary>
	internal static class Bomb
	{
		internal const string Tag = "bombrace.bomb";

		internal static bool IsBomb(RaceObjective objective)
		{
			return objective.target == Tag && (objective.kind == ObjectiveKind.None || objective.kind == ObjectiveKind.NotLast);
		}

		internal static RaceObjective Make(RunConfig config)
		{
			RaceObjective objective = config.Fixed(ObjectiveKind.None, 0);
			objective.target = Tag;
			return objective;
		}

		/// <summary>The words on the map tooltip and the pause screen. The bomb on the dash tells you the speed.</summary>
		internal static string Line(bool coloured)
		{
			return coloured ? "<color=#FF5A4A>Bomb</color>" : "Bomb";
		}

		/// <summary>The speed a car must stay above, in the units of the dashboard: a share of its engine's top speed.</summary>
		internal static float Limit(float topSpeed, float share)
		{
			return topSpeed * share * SpeedDashboard.SpeedToDisplay;
		}
	}
}
