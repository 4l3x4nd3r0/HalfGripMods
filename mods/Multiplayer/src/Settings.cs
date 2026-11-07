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
		internal const int ModeTournament = 2;
		/// <summary>Up to 20 racers, up to 100 laps; what a game mode from another mod builds on.</summary>
		internal const int ModeCustom = 3;
		/// <summary>A mode another mod registered through HalfGripTools.Net; <see cref="ModeId"/> says which.</summary>
		internal const int ModeMod = 4;
		internal const int WreckSpectate = 0;
		internal const int WreckRespawn = 1;

		/// <summary>The host's MAP setting for "pick one for me": turned into a real map and layout when the race starts.</summary>
		internal const string RandomMap = "*random";

		internal string Map = "";
		internal int Variant;
		internal int Laps = 3;
		internal int Mode = ModeRace;
		/// <summary>Races in a tournament, the mode that adds up the score of every race.</summary>
		internal int Rounds = 3;
		internal int Ai;
		internal int Wreck = WreckSpectate;
		internal float RespawnSeconds = 5f;
		internal string ModeId = "";

		// The bots (host only): how aggressive, how they are kitted, and whether they are all the same.
		/// <summary>0 NORMAL, 1 +25%, 2 HARDCORE, 3 +75%, 4 ULTRA.</summary>
		internal int Aggression;
		/// <summary>A <see cref="Bots"/> kit: starter, half kitted, fully kitted, speed demons, combat, like a run, random.</summary>
		internal int Kit;
		/// <summary>For the LIKE A RUN kit: 0 early, 1 middle, 2 late.</summary>
		internal int Stage = 1;
		/// <summary>0 SAME for every bot, 1 MIXED (within <see cref="SpreadSteps"/> of the choice), 2 RANDOM (every bot rolls its own).</summary>
		internal int Spread;
		internal int SpreadSteps = 1;

		/// <summary>Item ids the host banned: not offered in the room's garages, taken off any car, READY blocked while one is fitted.</summary>
		internal System.Collections.Generic.HashSet<string> Banned = new System.Collections.Generic.HashSet<string>();

		internal bool IsBanned(string id) { return id != null && Banned.Contains(id); }

		// Multipliers on the damage the same hit would do to an AI car. 0.5 = half.
		internal float Gun = 0.5f;
		internal float Explosion = 0.5f;
		internal float Item = 0.5f;
		internal float Ram = 1f;
		internal float AiGun = 1f;
		internal bool Tires = true;

		internal static string ModeName(int mode)
		{
			return mode == ModeDemolition ? "DEMOLITION" : mode == ModeTournament ? "TOURNAMENT" : mode == ModeCustom ? "CUSTOM" : "RACE";
		}

		/// <summary>The name for the MODE row: a mod's mode shows the name the mod gave it.</summary>
		internal string ModeLabel()
		{
			HalfGripTools.NetMode def = ModDef;
			return Mode == ModeMod ? (def == null ? ModeId.ToUpperInvariant() : def.Name.ToUpperInvariant()) : ModeName(Mode);
		}

		/// <summary>The registered mode this room plays, or null.</summary>
		internal HalfGripTools.NetMode ModDef
		{
			get { return Mode == ModeMod ? HalfGripTools.Net.Modes.FirstOrDefault(m => m.Id == ModeId) : null; }
		}

		/// <summary>The id the mods see (<c>HalfGripTools.Net.ModeId</c>): race, demolition, tournament, custom or the mod's own.</summary>
		internal string PublicModeId()
		{
			return Mode == ModeMod ? ModeId : Mode == ModeDemolition ? "demolition" : Mode == ModeTournament ? "tournament" : Mode == ModeCustom ? "custom" : "race";
		}

		/// <summary>How many players the room takes in this mode.</summary>
		internal int Cap
		{
			get
			{
				if (Mode == ModeCustom)
				{
					return CustomPlayers;
				}
				HalfGripTools.NetMode def = ModDef;
				return def == null ? Room.RaceMax : Clamp(def.MaxPlayers, 2, Room.Ceiling);
			}
		}

		/// <summary>The most laps the host can set in this mode.</summary>
		internal int MaxLaps
		{
			get
			{
				HalfGripTools.NetMode def = ModDef;
				return Mode == ModeCustom ? 100 : def != null ? Clamp(def.MaxLaps, 1, 100) : 10;
			}
		}

		internal const int CustomPlayers = 20;

		/// <summary>A tournament is a series of ordinary races: laps, finishing order and the wreck rule all apply.</summary>
		internal bool RaceLike { get { return Mode != ModeDemolition; } }

		internal RaceSettings Copy()
		{
			RaceSettings c = (RaceSettings)MemberwiseClone();
			c.Banned = new System.Collections.Generic.HashSet<string>(Banned);
			return c;
		}

		internal void Write(W w)
		{
			w.Str(Map).I32(Variant).I32(Laps).U8(Mode).U8(Rounds).U8(Ai).U8(Wreck).F32(RespawnSeconds);
			w.F32(Gun).F32(Explosion).F32(Item).F32(Ram).F32(AiGun).Bool(Tires);
			w.Str(ModeId).U8(Aggression).U8(Kit).U8(Stage).U8(Spread).U8(SpreadSteps);
			string[] ids = Banned.Where(b => !string.IsNullOrEmpty(b) && b.Length <= 60).Take(600).ToArray();
			w.I32(ids.Length);
			foreach (string id in ids)
			{
				w.Str(id);
			}
		}

		internal static RaceSettings Read(R r)
		{
			RaceSettings s = new RaceSettings();
			s.Map = r.Str();
			s.Variant = r.I32();
			s.Laps = Clamp(r.I32(), 1, 100);
			int mode = r.U8();
			s.Mode = mode == ModeDemolition || mode == ModeTournament || mode == ModeCustom || mode == ModeMod ? mode : ModeRace;
			s.Rounds = Clamp(r.U8(), 2, 10);
			s.Ai = Clamp(r.U8(), 0, Room.Ceiling - 1);
			s.Wreck = r.U8() == WreckRespawn ? WreckRespawn : WreckSpectate;
			s.RespawnSeconds = Clamp(r.F32(), 1f, 60f);
			s.Gun = Clamp(r.F32(), 0f, 10f);
			s.Explosion = Clamp(r.F32(), 0f, 10f);
			s.Item = Clamp(r.F32(), 0f, 10f);
			s.Ram = Clamp(r.F32(), 0f, 10f);
			s.AiGun = Clamp(r.F32(), 0f, 10f);
			s.Tires = r.Bool();
			s.ModeId = r.Str();
			if (s.Mode == ModeMod && s.ModDef == null)
			{
				s.Mode = ModeRace; // this machine does not have that mode (it should: the room's mods match)
			}
			s.Aggression = Clamp(r.U8(), 0, Bots.AggressionSteps - 1);
			s.Kit = Clamp(r.U8(), 0, Bots.KitCount - 1);
			s.Stage = Clamp(r.U8(), 0, 2);
			s.Spread = Clamp(r.U8(), 0, 2);
			s.SpreadSteps = Clamp(r.U8(), 1, 2);
			int banned = Clamp(r.I32(), 0, 600);
			for (int i = 0; i < banned; i++)
			{
				string id = r.Str();
				if (!string.IsNullOrEmpty(id))
				{
					s.Banned.Add(id);
				}
			}
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
		private static ConfigEntry<int> rounds;
		private static ConfigEntry<int> ai;
		private static ConfigEntry<int> wreck;
		private static ConfigEntry<float> respawn;
		private static ConfigEntry<int> aggression;
		private static ConfigEntry<int> kit;
		private static ConfigEntry<int> stage;
		private static ConfigEntry<int> spread;
		private static ConfigEntry<int> spreadSteps;
		private static ConfigEntry<string> banned;

		internal static void Bind(ConfigFile config)
		{
			gun = config.Bind("PvP", "GunDamage", 0.5f, "Damage guns do to another player, as a share of what the same shot does to an AI car. 0.5 = half. The revolver's per-hit damage and hits-to-wreck are logged when a race starts.");
			explosion = config.Bind("PvP", "ExplosionDamage", 0.5f, "Same, for explosions (rockets, blasts).");
			item = config.Bind("PvP", "ItemDamage", 0.5f, "Same, for melee items and anything else you hit a player with that is not a gun, a blast or a crash.");
			ram = config.Bind("PvP", "RamDamage", 1f, "Crash damage you take when you hit another player's car. 1 = the game's normal crash damage.");
			aiGun = config.Bind("PvP", "AiGunDamage", 1f, "Damage the AI rivals' guns do to a player. 1 = the game's normal.");
			tires = config.Bind("PvP", "TireShots", true, "Whether shooting a tire of another player's car punctures it.");
			laps = config.Bind("Room", "Laps", 3, "Laps of a race, as the host.");
			rounds = config.Bind("Room", "TournamentRounds", 3, "Races in a tournament, 2 to 10, as the host.");
			ai = config.Bind("Room", "AiRivals", 0, "AI rivals to fill the grid with, 0 to 5, as the host.");
			wreck = config.Bind("Room", "WreckRule", 0, "0 = a wrecked player is out and watches, 1 = respawns after a delay. As the host.");
			respawn = config.Bind("Room", "RespawnSeconds", 5f, "Seconds until a wrecked player respawns, when the wreck rule is 1.");
			aggression = config.Bind("Bots", "Aggression", 0, "0 normal, 1 +25%, 2 hardcore, 3 +75%, 4 ultra. As the host.");
			kit = config.Bind("Bots", "Kit", 0, "0 starter, 1 half kitted, 2 fully kitted, 3 speed demons, 4 combat, 5 like a run, 6 random. As the host.");
			stage = config.Bind("Bots", "RunStage", 1, "For the 'like a run' kit: 0 early, 1 middle, 2 late.");
			spread = config.Bind("Bots", "Spread", 0, "0 every bot the same, 1 mixed around the choice, 2 every bot random.");
			spreadSteps = config.Bind("Bots", "SpreadSteps", 1, "For mixed: how many steps either side of the choice a bot may roll, 1 or 2.");
			banned = config.Bind("Items", "Banned", "", "Item ids the host has banned (comma separated). Set in the lobby: RACE SETTINGS > ITEMS. Kept between lobbies.");
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
			s.Rounds = RaceSettings.Clamp(rounds.Value, 2, 10);
			s.Ai = RaceSettings.Clamp(ai.Value, 0, 5);
			s.Aggression = RaceSettings.Clamp(aggression.Value, 0, Bots.AggressionSteps - 1);
			s.Kit = RaceSettings.Clamp(kit.Value, 0, Bots.KitCount - 1);
			s.Stage = RaceSettings.Clamp(stage.Value, 0, 2);
			s.Spread = RaceSettings.Clamp(spread.Value, 0, 2);
			s.SpreadSteps = RaceSettings.Clamp(spreadSteps.Value, 1, 2);
			s.Wreck = wreck.Value == RaceSettings.WreckRespawn ? RaceSettings.WreckRespawn : RaceSettings.WreckSpectate;
			s.RespawnSeconds = respawn.Value;
			foreach (string id in (banned.Value ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				s.Banned.Add(id.Trim());
			}
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
			rounds.Value = s.Rounds;
			ai.Value = s.Ai;
			wreck.Value = s.Wreck;
			respawn.Value = s.RespawnSeconds;
			aggression.Value = s.Aggression;
			kit.Value = s.Kit;
			stage.Value = s.Stage;
			spread.Value = s.Spread;
			spreadSteps.Value = s.SpreadSteps;
			banned.Value = string.Join(",", s.Banned.OrderBy(b => b, StringComparer.Ordinal).ToArray());
		}
	}
}
