using System;
using System.Linq;
using BepInEx.Configuration;

namespace Multiplayer
{
	/// <summary>What the host decides for a race. Lives in the room, is sent to everybody, and is what the rules are read from.</summary>
	internal sealed class RaceSettings
	{
		internal const int ModeRace = 0;
		internal const int ModeDemolition = 1;
		internal const int WreckSpectate = 0;
		internal const int WreckRespawn = 1;

		internal string Map = "";
		internal int Variant;
		internal int Laps = 3;
		internal int Mode = ModeRace;
		internal int Ai;
		internal int Wreck = WreckSpectate;
		internal float RespawnSeconds = 5f;

		// Multipliers on the damage the same hit would do to an AI car. 0.5 = half.
		internal float Gun = 0.5f;
		internal float Explosion = 0.5f;
		internal float Item = 0.5f;
		internal float Ram = 1f;
		internal float AiGun = 1f;
		internal bool Tires = true;

		internal static string ModeName(int mode)
		{
			return mode == ModeDemolition ? "DEMOLITION" : "RACE";
		}

		internal RaceSettings Copy()
		{
			return (RaceSettings)MemberwiseClone();
		}

		internal void Write(W w)
		{
			w.Str(Map).I32(Variant).I32(Laps).U8(Mode).U8(Ai).U8(Wreck).F32(RespawnSeconds);
			w.F32(Gun).F32(Explosion).F32(Item).F32(Ram).F32(AiGun).Bool(Tires);
		}

		internal static RaceSettings Read(R r)
		{
			RaceSettings s = new RaceSettings();
			s.Map = r.Str();
			s.Variant = r.I32();
			s.Laps = Clamp(r.I32(), 1, 20);
			s.Mode = r.U8() == ModeDemolition ? ModeDemolition : ModeRace;
			s.Ai = Clamp(r.U8(), 0, 5);
			s.Wreck = r.U8() == WreckRespawn ? WreckRespawn : WreckSpectate;
			s.RespawnSeconds = Clamp(r.F32(), 1f, 60f);
			s.Gun = Clamp(r.F32(), 0f, 10f);
			s.Explosion = Clamp(r.F32(), 0f, 10f);
			s.Item = Clamp(r.F32(), 0f, 10f);
			s.Ram = Clamp(r.F32(), 0f, 10f);
			s.AiGun = Clamp(r.F32(), 0f, 10f);
			s.Tires = r.Bool();
			return s;
		}

		internal static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
		internal static float Clamp(float v, float lo, float hi) { return float.IsNaN(v) ? lo : v < lo ? lo : v > hi ? hi : v; }
	}

	/// <summary>
	/// The host's defaults for player-versus-player damage, as ordinary BepInEx config entries
	/// (<c>BepInEx\config\halfgrip.multiplayer.cfg</c>). The host can change them in the lobby; the values the host has when a race starts are what everybody uses.
	/// </summary>
	internal static class Pvp
	{
		private static ConfigEntry<float> gun;
		private static ConfigEntry<float> explosion;
		private static ConfigEntry<float> item;
		private static ConfigEntry<float> ram;
		private static ConfigEntry<float> aiGun;
		private static ConfigEntry<bool> tires;
		private static ConfigEntry<int> laps;
		private static ConfigEntry<int> ai;
		private static ConfigEntry<int> wreck;
		private static ConfigEntry<float> respawn;

		internal static void Bind(ConfigFile config)
		{
			gun = config.Bind("PvP", "GunDamage", 0.5f, "Damage guns do to another player, as a share of what the same shot does to an AI car. 0.5 = half. The revolver's per-hit damage and hits-to-wreck are logged when a race starts.");
			explosion = config.Bind("PvP", "ExplosionDamage", 0.5f, "Same, for explosions (rockets, blasts).");
			item = config.Bind("PvP", "ItemDamage", 0.5f, "Same, for melee items and anything else you hit a player with that is not a gun, a blast or a crash.");
			ram = config.Bind("PvP", "RamDamage", 1f, "Crash damage you take when you hit another player's car. 1 = the game's normal crash damage.");
			aiGun = config.Bind("PvP", "AiGunDamage", 1f, "Damage the AI rivals' guns do to a player. 1 = the game's normal.");
			tires = config.Bind("PvP", "TireShots", true, "Whether shooting a tire of another player's car punctures it.");
			laps = config.Bind("Room", "Laps", 3, "Laps of a race, as the host.");
			ai = config.Bind("Room", "AiRivals", 0, "AI rivals to fill the grid with, 0 to 5, as the host.");
			wreck = config.Bind("Room", "WreckRule", 0, "0 = a wrecked player is out and watches, 1 = respawns after a delay. As the host.");
			respawn = config.Bind("Room", "RespawnSeconds", 5f, "Seconds until a wrecked player respawns, when the wreck rule is 1.");
		}

		/// <summary>The host's starting values for a new room.</summary>
		internal static RaceSettings Defaults()
		{
			RaceSettings s = new RaceSettings();
			s.Gun = gun.Value;
			s.Explosion = explosion.Value;
			s.Item = item.Value;
			s.Ram = ram.Value;
			s.AiGun = aiGun.Value;
			s.Tires = tires.Value;
			s.Laps = laps.Value;
			s.Ai = RaceSettings.Clamp(ai.Value, 0, 5);
			s.Wreck = wreck.Value == RaceSettings.WreckRespawn ? RaceSettings.WreckRespawn : RaceSettings.WreckSpectate;
			s.RespawnSeconds = respawn.Value;
			return s;
		}

		/// <summary>The stock revolver's numbers against another player, for the lobby and the log: "Revolver: 17 per hit, 6 hits to wreck 100 HP".</summary>
		internal static string RevolverInfo(RaceSettings s)
		{
			GunUpgrade revolver = Loadout.Items().OfType<GunUpgrade>().FirstOrDefault(g => g != null && g.gunType == GunType.REVOLVER && !g.disabled);
			if (revolver == null)
			{
				return "";
			}
			float perHit = revolver.damage / Math.Max(1, revolver.projectilesPerShot) * s.Gun;
			if (perHit <= 0.001f)
			{
				return "Revolver: harmless";
			}
			int hits = (int)Math.Ceiling(100f / perHit - 0.0001f);
			return "Revolver: " + perHit.ToString("0.#") + " per hit, " + hits + " hit(s) to wreck 100 HP";
		}

		internal static void LogRevolver(RaceSettings s)
		{
			string text = RevolverInfo(s);
			if (text.Length > 0)
			{
				Mod.Log.LogInfo("PvP damage: " + text + " (guns x" + s.Gun + ", explosions x" + s.Explosion + ", items x" + s.Item + ", ram x" + s.Ram + ", AI guns x" + s.AiGun + ")");
			}
		}

		/// <summary>What the host chose in the lobby is kept as the next default.</summary>
		internal static void Save(RaceSettings s)
		{
			if (gun == null)
			{
				return;
			}
			gun.Value = s.Gun;
			explosion.Value = s.Explosion;
			item.Value = s.Item;
			ram.Value = s.Ram;
			aiGun.Value = s.AiGun;
			tires.Value = s.Tires;
			laps.Value = s.Laps;
			ai.Value = s.Ai;
			wreck.Value = s.Wreck;
			respawn.Value = s.RespawnSeconds;
		}
	}
}
