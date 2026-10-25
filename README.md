# HalfGripMods

Mods for Half Grip Demo (Steam, Windows), made by 4l3x4nd3r. They run on [HalfGripTools](https://github.com/4l3x4nd3r0/HalfGripTools), a mod loader that compiles mods from source when the game starts. Every mod here is plain C# and JSON, so you can read it before it runs.

| Mod | What it adds | Cheat to get it |
|---|---|---|
| M1911 | Legendary semi-auto pistol | `giveitem a_light_in_shinning_darkness` |
| RPG | Legendary rocket launcher, reloaded by hand | `giveitem rpg` |
| SniperRifle | Legendary bolt-action rifle with scope and charge shot | `giveitem sniper_rifle` |
| SpeedCharms | 3 passives for top speed and engine power. JSON only | `giveitem winners_grip`, `rabbits_foot`, `dragster_chute` |
| DaciaBody | Dacia Logan rally body with its own 3D model | `giveitem dacia_body` |
| BombRace | Race mode: stay above a speed or the bomb goes off | none, about 1 race in 10 |
| InfinitePlayground | Endless floor for the `playground` command | `playground` |
| Highway | Race map built in code: two four-lane highways, one on a bridge over the other, dirt shortcuts, breakable roadside objects. In the run map pool | none, shows up as a map |
| Multiplayer | Races for 2 to 6 players over Steam: host-run room, same mods required, PvP damage and AI rivals set by the host | none, the MULTIPLAYER button in the main menu |

Cheats: type `pirgflah` in Settings, then press `` ` `` for the console. You do not need them: M1911, RPG, SniperRifle, SpeedCharms and DaciaBody turn up in shops and race rewards like any other item.

All mods are made for game version 0.418. On another version they still load, and the mod manager shows a warning sign next to them.

## Install

You need Windows and the game on Steam.

1. Install BepInEx and HalfGripTools. Steps 1 and 2 of the [HalfGripTools README](https://github.com/4l3x4nd3r0/HalfGripTools#install) cover both. Do this once.
2. Download this repo: click **Code**, then **Download ZIP** (or `git clone`), and unzip it. Copy the folders you want from [`mods/`](mods) into `BepInEx\HalfGripMods\` in the game folder (the one with `HalfGrip.exe`). Each folder is one mod. To install all of them you get:

```
BepInEx\HalfGripMods\
  M1911\  RPG\  SniperRifle\  SpeedCharms\  DaciaBody\  BombRace\  InfinitePlayground\  Highway\  Multiplayer\
```

3. Start the game, quit, open `BepInEx\LogOutput.log`. Each mod that loaded has a line:

```
[Info   :HalfGripTools] Sniper Rifle: compiled 9 files in 66 ms
[Info   :HalfGripTools] Loaded Sniper Rifle 1.0.0
[Info   :Sniper Rifle] Added sniperrifle.rifle to the item catalog: tier=Legendary cost=60
```

The first start compiles everything and takes a few seconds. Later starts use the cache.

Switch mods on and off, and make playlists of them, in the mod manager (key and folder button in the main menu). With the game's speedrun mode on, HalfGripTools loads no mods.

## The mods

### M1911
Engraved nickel M1911, semi-auto, 8 rounds, 33 damage, revolver sound. Legendary, cost 60. Settings: `DamagePerShot`, `FireRate`, `Recoil`, `Inaccuracy`, `Capacity`.

### RPG
Drag a rocket from the rack to the front of the tube, it slides in tail first. One rocket per load, 3 spare. The rocket arms after 3 game units, so it cannot hit your own bonnet. Blast damage falls off with distance in car lengths: full to 2, half at 3, 15% at 4, zero past 5. Your car is safe unless `SelfDamage = true`. Settings: `ExplosionDamage`, `DirectHitDamage`, `CarLength`, `RocketLaunchSpeed`, `RocketMaxSpeed`, `Rockets`, `SelfDamage`, `LowerAfterShot`.

### SniperRifle
Pull the bolt back, drag a cartridge onto the open breech, push the bolt forward. Raise the rifle to aim: quick shot, 50 damage. Press Z to scope in (2.5x zoom, lens overlay, cockpit hidden). Scoped, the shot charges for 1.2 s: 50, 80 at half, 100 at full. 4 cartridges, each extra magazine adds 2. Settings: `Rounds`, `BaseDamage`, `HalfChargeDamage`, `FullChargeDamage`, `ChargeDelay`, `ChargeTime`, `Magnification`, `ZoomSpeed`, `ScopeOverlay`, `LaserDot`, `LowerAfterShot`.

### SpeedCharms
Three passives, written as JSON in `mods/SpeedCharms/items`. Percentages scale the fitted engine, so they stack with engine upgrades. To change a number, edit the JSON.

| Charm | Tier | Cost | Effect |
|---|---|---|---|
| Winner's Grip | Stock | 15 | +6% top speed, +6% engine power |
| Rabbit's Foot | RaceSpec | 25 | +12% top speed, +8% engine power, -5% tire grip |
| Dragster Chute | BlackMarket | 35 | +15% top speed, +20% engine power, -40% brake power |

### DaciaBody
Body item, Legendary, cost 45: +40% max HP, air drag 1.25 (stock 1), downforce 1, front wheel drive. Replaces the car's body, hood, glass and light meshes while fitted. Settings: the numbers above, `Tier`, `Cost`, `LightGlow`, `HeadlightSetback`, three colours.

### BombRace
About 1 race in 10 in the middle of a run is a Bomb race. Finish alive to clear the node.

- Dash: a C4 block with a screen and two bulbs. Door: a C4 on every car.
- Limit: 45% of your engine's top speed (80 on the dash with the stock engine).
- Grace period 5 s. Under the limit the fuse burns (red bulb, beeps faster). Over it the fuse burns back (green bulb). 3 s of fuse: wrecked.
- Each lap but the last, the car in last place blows up. That can be you.
- Rivals carry a C4 too: limit 30% of their top speed, fuse 10 s. Their blasts do not count as your kills.

Settings: `Chance`, `SpeedShare`, `FuseSeconds`, `GraceSeconds`, `EliminateLast`, `RivalBombs`, `RivalSpeedShare`, `RivalFuseSeconds`, `C4Width`, `DriverSide`, and `Width`, `NudgeX`, `NudgeY` under `Dash Bomb`.

### InfinitePlayground
A flat checker floor follows the car in the `playground` scene. Settings: `Enabled`, `HeightOffset`, `Size`, `TileSize`.

### Highway
A race map, added to `RunConfig.maps` as track index 20. A mod cannot add a scene, so the map is built from code inside the Stadium scene when a Highway race loads. Stadium hosts it; its own track is not used.

What is in it: two four-lane highways crossing, one on a bridge over the other, joined by two ramps. The lap is 6.5 km and starts on the bridge highway. Both highways run on to the horizon. Lanes are about twice a car's width, the median is 10 m with 2.3 m concrete walls and a gap every 120 m, with steel guardrail along the edges. The lower highway has a closed lane before the bridge (median blocks, arrow board, machinery, containers). Each highway has an unfinished dirt road on the far side of the interchange, forked off at the exits; the game scores them as shortcuts.

How it works:

- **Geometry:** meshes, textures and sprites are generated at load. Nothing is copied from the game's files. Pine trees are picked up from the forest maps at runtime (plain trees if they are not found). The ground material is borrowed from Stadium.
- **Track line:** the control points of the game's `PathManager` are replaced by the highway's loop; the racing line the AI follows is baked from it. Laps count when the nearest-sample index wraps.
- **Stacked roads:** the game finds "where on the track" a car is with a 3D nearest-sample lookup, which confuses two roads on top of each other. The mod replaces `PathManager.GetClosestPointIndex` with its own tree that weighs height four times.
- **Dirt shortcuts:** an index jump over a quarter of the samples would count as a backwards lap or "Wrong way", so the lap counter and the wrong-way check are patched to use the direction of the dirt road when the car is on one.
- **Breakables:** guardrail, signs, drums and cones are script-driven pieces, not rigid bodies (rigid bodies cost too many frames). A touched piece breaks. Each guardrail piece takes 30% of your speed, a sign or drum 3%, cones nothing.
- **Resets:** the game's reset ray is shortened so a reset under the bridge finds the lower deck.

Known: a few gaps of 1 to 3 m where a highway wall meets a ramp wall. A saved run that includes Highway needs the mod loaded to be restored.

Settings: `TestMode` (every race on Highway), `BenchmarkFactor` (scales the time objectives), `MedianGaps`, `MedianGapLength`, `SpeedLossPerPiece`, `SignSpeedLoss`, `Grip` and `Drag` (dirt roads), `ShowPath`.

### Multiplayer
Races for 2 to 6 players over Steam. It switches on the MULTIPLAYER button of the main menu (the demo locks it with "Not in demo").

**Using it.** The MULTIPLAYER screen has GARAGE, HOST ROOM and JOIN ROOM.

- GARAGE opens the game's garage with no room: no shop, no money, every item in the inventory except Slow-Mo, the Inverter, map rerolls, run boons and other consumables. What you fit is kept in `BepInEx\config\halfgrip.multiplayer.loadout.txt`. With nothing fitted you start with the Gunslinger and Nitro Head items, without Nitro Head's handicap. Esc goes back.
- HOST ROOM makes a Steam lobby and a 6-character code. Others type it under JOIN ROOM, or accept a Steam invite while they are at the main menu. INVITE FRIENDS lists your online Steam friends. The host can lock the room and kick players.
- In the room everybody but the host presses READY. The host sees START RACE (grey until all are ready) and sets the race in RACE SETTINGS: map, layout, mode (Race or Demolition), laps, up to 5 AI rivals, what a wreck does (out and watch, or respawn after a delay), and the damage multipliers. Other players can open the settings but not change them.
- During the race Pause does not stop time. Your car is released while the menu is open. Leaving from it is a disconnect.
- Once you cross the line you let go of the controls and the car cannot be hurt. After a few seconds it is hidden, the others see it wrecked away, and you watch from a drone (Tab: next car, bots too). RACE RESULTS stays locked until every player has finished or is out; then it ends the race even if bots are still driving. Only the winner gets the game's SUCCESS text.
- The results list place, best lap, total time, KOs, WOs and a score for each car, bots included. Score follows the single-player rule: place payout (finishers), survival, kills, and what you picked up on the way. A wrecked car scores 0. The host sends everybody back to the lobby.

**How it works.**

- **Host authority.** The host holds the room: phase, settings, roster. Clients send requests (ready, finished, wrecked, stats) and mirror the host's roster. Phases: lobby, loading, racing, results.
- **Network.** Steam lobbies for rooms (lobby data carries the code and the mod checksum), Steam P2P for messages: small binary packets, reliable for control, unreliable for car state at 20 Hz. Sessions are only accepted from lobby members. The mod uses the Steam library the game already ships.
- **Same mods.** Each mod gets a SHA-256 over every file in its folder. The room compares one checksum over all of them plus the game version, the HalfGripTools version and the protocol number. A mismatch is refused with the list of what differs. Edit a mod and you are in a different room. The lobby shows `checksum` and the first 8 hex digits; the full list is in the log.
- **Other players' cars** are copies of the game's enemy-car prefab: kinematic, moved towards the last received state with up to 0.25 s of extrapolation. Because they are enemies, the game's own gun rays, tire shots, blasts, rams and damage marks hit them unchanged.
- **PvP damage.** The shooter's game finds the hit with the game's code, multiplies the damage by the host's setting and sends it to the owner, whose game applies it to the real car through `CarLink.TakeCrashDamage` (so Max HP, regeneration and wreck-saving items work). Ramming is handled by both games with the game's own crash damage. Defaults: guns, explosions and items 50%, ramming and AI guns 100%, tire shots on. The stock revolver does 35 per shot, so 17.5 per hit and six hits wreck 100 HP.
- **AI rivals** are the host's real enemy cars; clients get stand-ins updated at 15 Hz. A client's hits on an AI car go to the host. AI gunfire at a player is simulated on that player's own game.
- **Races** start through the garage's own start button (that is where the game applies your loadout), so the garage scene loads unseen behind a black screen. The track, weather and lap rules are the game's.
- **Your save is not touched.** On entering a room (or the garage) the save tables are copied in memory and every write to disk and to Steam achievements is blocked. On leaving they are put back and the game's caches are dropped.

**Limits:** other players' gunfire, rockets and tracers are not drawn on your screen, only what they do. Slow-Mo is off in a room. If the host leaves, the room closes. The AI cars run in the host's game, so if the host leaves the room closes. The AI KO count only knows about gun hits, not rams.

Settings (`halfgrip.multiplayer.cfg`, also changed from the lobby and remembered): `GunDamage`, `ExplosionDamage`, `ItemDamage`, `RamDamage`, `AiGunDamage`, `TireShots`, and `Laps`, `AiRivals`, `WreckRule`, `RespawnSeconds` for new rooms.

The mod also contains a test harness (`Rig*.cs`, the local backend in `Backend.cs`). It does nothing unless the game is started with `-mprig`; see `tools/mp-test.ps1`.

Have fun.
## Settings, update, remove

- Settings: each code mod writes `BepInEx\config\halfgrip.<mod id>.cfg` on first run, for example `halfgrip.sniperrifle.cfg`. Every entry has a comment. Edit with the game closed.
- Update: download the repo again and replace the mod folder. It recompiles at the next start.
- Remove: delete the folder, or switch it off in the mod manager.
- Saves: items are saved by id. Without their mod, a saved run loses those items. Nothing else in the save changes.
- The game marks runs with a `cheats` flag. For a vanilla run, switch the mods off and cheats off.

## Trust

Mods are real C# and run with the game's permissions. Read them first. No mod here starts a process or writes your save. Only Multiplayer uses the network (Steam lobbies and Steam's peer-to-peer messages, through the Steam library the game already ships) and writes files (its loadout file, and test files in a folder you name when the game is started with `-mprig`). Check the others:

```powershell
git grep -nE "HttpClient|WebRequest|WebClient|Net\.Sockets|Process\.Start|File\.(Write|Delete|Create|Copy|Move|Append)|SaveData\.Set|PlayerPrefs|DllImport|Application\.Quit" -- mods ':!mods/Multiplayer'
```

Expected: no output. In Multiplayer the same search finds the Steam calls, the loadout file, the test harness, and `SaveData.Set` in its save sandbox (which writes only to memory; the game's `Save()` is blocked while a room is open). The only compiled code you run is HalfGripTools, see its [Trust](https://github.com/4l3x4nd3r0/HalfGripTools#trust) section for how to check it.

## Tools

You need the .NET 8 SDK and HalfGripTools installed in the game. All scripts take `-GameDir "C:\path\to\Half Grip Demo"`.

```powershell
.\tools\install.ps1                       # copy the mods into BepInEx\HalfGripMods in the game
.\tools\check-mods.ps1                    # compile every mod without starting the game
.\tools\check-mods.ps1 -Mod BombRace      # one mod
.\tools\package.ps1 -Version 1.0.0        # release zip in release\
.\tools\new-mod.ps1 MyMod                 # new mod folder from a template
.\tools\redraw-art.ps1                    # redraw the pixel art (Python with Pillow)
.\tools\mp-test.ps1 -Suite all            # run Multiplayer in real game copies, headless (see the script)
```

How to write a mod: [WRITING_MODS.md](https://github.com/4l3x4nd3r0/HalfGripTools/blob/main/docs/WRITING_MODS.md) in the HalfGripTools repo.

## Art

`art/` has the Python scripts that draw the sprites. The finished PNGs are already in `mods/*/sprites`, so you only need Python to redraw them. `redraw-art.ps1` runs the scripts and copies the results. The RPG art also needs UnityPy (it reads the game's launcher texture), the Sniper art needs numpy.

## Credits and licence

Code and sprites drawn by the scripts in `art/`: 4l3x4nd3r, [MIT](LICENSE).

Not covered by MIT:

- `mods/DaciaBody/dacia_model.txt`: converted from the model `2026-dacia-logan-ollis-garage-racing`. The archive had no licence, so its terms are unknown. If you are the author and want it removed or credited differently, open an issue.
- `mods/SniperRifle/sprites/sniper_*.png` and `art/sniper_render.png`: a render of a low-poly TF2 Sniper Rifle model, licence unknown. Team Fortress 2 belongs to Valve.

Half Grip belongs to its developers. This project is fan-made and not affiliated with them.
