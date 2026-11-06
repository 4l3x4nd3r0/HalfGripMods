using System;
using System.Linq;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>Adds Temple of Drift to <c>RunConfig.maps</c>. A saved run finds its maps there by name, so this must happen before one is restored.</summary>
	internal static class MapEntry
	{
		/// <summary>The scene the map is built in: Stadium's.</summary>
		internal static int HostTrack;

		/// <summary>Stadium's lap is about 1457 m; times scale with the ratio of lap lengths.</summary>
		private const float StadiumLap = 1457f;

		/// <summary>The index the map reports as its track (Highway uses 20), so its records and neighbour rules do not mix with another map's.</summary>
		internal const int TrackId = 21;

		/// <summary>The map's name in a save. Never change it.</summary>
		internal const string MapName = "TempleOfDrift";

		internal static void Ensure()
		{
			try
			{
				RunConfig config = RunConfig.Instance;
				if (config == null || config.maps == null)
				{
					return;
				}
				if (Mod.Map == null)
				{
					MapDefinition source = config.FindMap("StadiumAsh") ?? config.maps.FirstOrDefault(m => m != null);
					if (source == null)
					{
						Mod.Log.LogWarning("The run config has no map to copy, Temple of Drift is not added.");
						return;
					}
					Mod.Map = Make(config, source);
					Mod.Log.LogInfo("Temple of Drift map made from " + source.name + " (host track " + HostTrack + ", benchmark " + Mod.Map.timeEarly.ToString("0") + "/" + Mod.Map.timeMid.ToString("0") + "/" + Mod.Map.timeLate.ToString("0") + " s)");
				}
				if (!config.maps.Contains(Mod.Map))
				{
					config.maps.Add(Mod.Map);
					Mod.Log.LogInfo("Temple of Drift added to the run config (" + config.maps.Count + " maps)");
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogError("Could not add the Temple of Drift map: " + e);
			}
		}

		internal static void Remove()
		{
			RunConfig config = RunConfig.Instance;
			if (config != null && Mod.Map != null)
			{
				config.maps.RemoveAll(m => m == Mod.Map);
			}
		}

		private static MapDefinition Make(RunConfig config, MapDefinition source)
		{
			HostTrack = source.trackIndex;
			MapDefinition map = UnityEngine.Object.Instantiate(source);
			map.hideFlags = HideFlags.HideAndDontSave;
			map.name = MapName;
			map.displayName = "Temple of Drift";
			map.conditions = string.Empty;   // no other map has one, and it shows in the run map's tooltip
			map.trackIndex = TrackId;
			map.allowMirror = false;
			map.allowReverse = false;
			map.allowMirrorReverse = false;
			map.mirrorIcon = null;
			map.reverseIcon = null;
			map.mirrorReverseIcon = null;
			map.excludeFromLast = false;
			map.notBesideSameTrack = false;
			map.altMusicTrack = null;
			map.altMusicChance = 0f;

			float scale = Data.Lap / StadiumLap * Mod.BenchmarkFactor.Value;
			map.timeEarly = source.timeEarly * scale;
			map.timeMid = source.timeMid * scale;
			map.timeLate = source.timeLate * scale;

			// Daylight forest weather with the rain switched off and a light mist; the Stadium's own is ash.
			MapDefinition forest = config.FindMap("ForestRainyDay") ?? config.FindMap("ForestRed");
			WeatherProfile from = forest != null && forest.weather != null ? forest.weather : source.weather;
			if (from != null)
			{
				WeatherProfile weather = UnityEngine.Object.Instantiate(from);
				weather.hideFlags = HideFlags.HideAndDontSave;
				weather.rain = false;
				weather.snow = false;
				weather.ash = false;
				weather.headlightsActive = false;
				weather.fogActive = true;
				weather.fogColor = new Color(0.70f, 0.78f, 0.78f);
				weather.fogStart = 120f;
				weather.fogEnd = 2600f;
				weather.gripExponent = 1f;
				weather.gripMultiplier = 1f;
				map.weather = weather;
			}

			Sprite icon = Mod.Ctx.Sprites.LoadSprite("templeofdrift_icon.png", source.icon);
			if (icon != null)
			{
				map.icon = icon;
			}
			if (Mod.PlayMusic.Value)
			{
				AudioClip clip = Music.Load(Mod.Ctx.Folder);
				if (clip != null)
				{
					map.musicTrack = clip;
				}
			}
			return map;
		}
	}
}
