using System;
using System.Linq;
using UnityEngine;

namespace HalfGripHighway
{
	/// <summary>Adds Highway to <c>RunConfig.maps</c>. A saved run finds its maps there by name, so this must happen before one is restored.</summary>
	internal static class MapEntry
	{
		/// <summary>The scene Highway is built in: Stadium's.</summary>
		internal static int HostTrack;

		/// <summary>Stadium's lap is about 1457 m; times scale with the ratio of lap lengths.</summary>
		private const float StadiumLap = 1457f;

		/// <summary>The index Highway reports as its track, so its records and neighbour rules do not mix with Stadium's.</summary>
		private const int TrackId = 20;

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
						Mod.Log.LogWarning("The run config has no map to copy, Highway is not added.");
						return;
					}
					Mod.Map = Make(source);
					Mod.Log.LogInfo("Highway map made from " + source.name + " (host track " + HostTrack + ", benchmark " + Mod.Map.timeEarly.ToString("0") + "/" + Mod.Map.timeMid.ToString("0") + "/" + Mod.Map.timeLate.ToString("0") + " s)");
				}
				if (!config.maps.Contains(Mod.Map))
				{
					config.maps.Add(Mod.Map);
					Mod.Log.LogInfo("Highway added to the run config (" + config.maps.Count + " maps)");
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogError("Could not add the Highway map: " + e);
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

		private static MapDefinition Make(MapDefinition source)
		{
			HostTrack = source.trackIndex;
			MapDefinition map = UnityEngine.Object.Instantiate(source);
			map.hideFlags = HideFlags.HideAndDontSave;
			map.name = "Highway";
			map.displayName = "Highway";
			map.conditions = string.Empty;   // no other map has one, and it shows in the run map's tooltip
			map.trackIndex = TrackId;
			map.allowMirror = false;
			map.allowReverse = false;
			map.allowMirrorReverse = false;
			map.mirrorIcon = null;
			map.reverseIcon = null;
			map.mirrorReverseIcon = null;

			float scale = Route.Build().Length / StadiumLap * Mod.BenchmarkFactor.Value;
			map.timeEarly = source.timeEarly * scale;
			map.timeMid = source.timeMid * scale;
			map.timeLate = source.timeLate * scale;

			if (source.weather != null)
			{
				WeatherProfile weather = UnityEngine.Object.Instantiate(source.weather);
				weather.hideFlags = HideFlags.HideAndDontSave;
				weather.rain = false;
				weather.snow = false;
				weather.ash = false;
				weather.fogActive = false;
				weather.headlightsActive = false;
				weather.gripExponent = 1f;
				weather.gripMultiplier = 1f;
				map.weather = weather;
			}

			Sprite icon = Mod.Ctx.Sprites.LoadSprite("highway_icon.png", source.icon);
			if (icon != null)
			{
				map.icon = icon;
			}
			return map;
		}
	}
}
