using HarmonyLib;
using UnityEngine;

namespace HalfGripBombRace
{
	/// <summary>Middle-of-run races (including the game's re-roll of a kill-heavy map) sometimes come out as a Bomb race.</summary>
	[HarmonyPatch(typeof(RunConfig), nameof(RunConfig.RollObjective))]
	internal static class RollObjectivePatch
	{
		private static void Postfix(RunConfig __instance, ref RaceObjective __result)
		{
			if (Random.value < Mod.Chance.Value)
			{
				__result = Bomb.Make(__instance);
			}
		}
	}

	/// <summary>The overlay round a map node. The game draws its objectives as rings; this is the Bomb's.</summary>
	[HarmonyPatch(typeof(RunMapSkin), nameof(RunMapSkin.ObjectiveSprite))]
	internal static class ObjectiveSpritePatch
	{
		private static void Postfix(RunMapSkin __instance, RaceObjective objective, ref Sprite __result)
		{
			if (!Bomb.IsBomb(objective))
			{
				return;
			}
			// Take the pixel density and pivot from the game's own ring of the same shape (24x30, the one with flames), so it sits round the node
			// the same way. Failing that, from any ring with a crest.
			Sprite like = null;
			foreach (ObjectiveIcon icon in __instance.objectiveIcons)
			{
				if (icon.sprite == null)
				{
					continue;
				}
				if (Mod.DumpInfo.Value)
				{
					Mod.Log.LogInfo($"Objective icon: {icon.kind} {icon.amount} = {icon.sprite.name} {icon.sprite.rect.width}x{icon.sprite.rect.height} ppu {icon.sprite.pixelsPerUnit} pivot {icon.sprite.pivot}");
				}
				if (Mathf.Approximately(icon.sprite.rect.width, 24f) && Mathf.Approximately(icon.sprite.rect.height, 30f))
				{
					like = icon.sprite;
				}
				else if (like == null && icon.sprite.rect.height > icon.sprite.rect.width)
				{
					like = icon.sprite;
				}
			}
			if (like == null)
			{
				like = __result;
			}
			Sprite ring = Mod.Ctx.Sprites.LoadSprite("bomb_icon.png", like);
			if (ring != null)
			{
				__result = ring;
			}
		}
	}

	/// <summary>The words on the map tooltip, the pause screen and the call-out at the start of the race.</summary>
	[HarmonyPatch(typeof(RaceObjective), nameof(RaceObjective.Describe))]
	internal static class DescribePatch
	{
		private static void Postfix(ref RaceObjective __instance, bool coloured, ref string __result)
		{
			if (Bomb.IsBomb(__instance))
			{
				__result = Bomb.Line(coloured);
			}
		}
	}
}
