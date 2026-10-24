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
| Highway | New race map: two four-lane highways, one on a bridge over the other, with dirt shortcuts. Joins the run map pool | none, shows up as a map |

Cheats: type `pirgflah` in Settings, then press `` ` `` for the console. You do not need them: M1911, RPG, SniperRifle, SpeedCharms and DaciaBody turn up in shops and race rewards like any other item.

All mods are made for game version 0.418. On another version they still load, and the mod manager shows a warning sign next to them.

## Install

You need Windows and the game on Steam.

1. Install BepInEx and HalfGripTools. Steps 1 and 2 of the [HalfGripTools README](https://github.com/4l3x4nd3r0/HalfGripTools#install) cover both. Do this once.
2. Download this repo: click **Code**, then **Download ZIP** (or `git clone`), and unzip it. Copy the folders you want from [`mods/`](mods) into `BepInEx\HalfGripMods\` in the game folder (the one with `HalfGrip.exe`). Each folder is one mod. To install all of them you get:

```
BepInEx\HalfGripMods\
  M1911\  RPG\  SniperRifle\  SpeedCharms\  DaciaBody\  BombRace\  InfinitePlayground\  Highway\
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
A new race map, added to the run map's pool. Two four-lane highways cross in the middle, one on a bridge over the other, joined by two ramps into a 6.5 km lap that starts on the bridge highway. Both highways run on to the horizon.

- **The road:** lanes about twice a car's width, a 10 m median with 2.3 m concrete walls and 120 m gaps (breakable signs stand in them), steel guardrail along the edges, concrete only on the bridge. Exit boards at 500, 250 and 150 m, in the style of Romanian motorway signs.
- **Everything on the road breaks:** guardrail, signs, drums and cones come apart when your car touches them. Each guardrail piece takes 30% of your speed (three hits slow you a lot, five or six nearly stop you), a sign or drum takes 3%, cones take none.
- **Roadworks:** the lower highway has a lane closed for works just before the bridge: median blocks, an arrow board, solid machinery and containers.
- **Shortcuts:** each highway has an unfinished dirt road on the other side of the interchange, forked off at the exits and marked with cones. Heavy machinery stands along the edges with a wide free corridor. The game counts them as shortcuts. They are dirt, so your tyres decide how much they cost you.
- **Nothing is copied from the game's files.** Meshes, textures and sprites are made by the mod at start; the pine trees are picked up from the forest maps at runtime (plain trees if they cannot be found).

Settings: `TestMode` (every race on Highway), `BenchmarkFactor` (scales the time objectives), `MedianGaps`, `MedianGapLength`, `SpeedLossPerPiece`, `SignSpeedLoss`, `Grip` and `Drag` (dirt roads), `ShowPath`.
## Settings, update, remove

- Settings: each code mod writes `BepInEx\config\halfgrip.<mod id>.cfg` on first run, for example `halfgrip.sniperrifle.cfg`. Every entry has a comment. Edit with the game closed.
- Update: download the repo again and replace the mod folder. It recompiles at the next start.
- Remove: delete the folder, or switch it off in the mod manager.
- Saves: items are saved by id. Without their mod, a saved run loses those items. Nothing else in the save changes.
- The game marks runs with a `cheats` flag. For a vanilla run, switch the mods off and cheats off.

## Trust

Mods are real C# and run with the game's permissions. Read them first. No mod here uses the network, starts a process, or writes a file or your save. Check:

```powershell
git grep -nE "HttpClient|WebRequest|WebClient|Net\.Sockets|Process\.Start|File\.(Write|Delete|Create|Copy|Move|Append)|SaveData\.Set|PlayerPrefs|DllImport|Application\.Quit" -- mods
```

Expected: no output. The only compiled code you run is HalfGripTools, see its [Trust](https://github.com/4l3x4nd3r0/HalfGripTools#trust) section for how to check it.

## Tools

You need the .NET 8 SDK and HalfGripTools installed in the game. All scripts take `-GameDir "C:\path\to\Half Grip Demo"`.

```powershell
.\tools\install.ps1                       # copy the mods into BepInEx\HalfGripMods in the game
.\tools\check-mods.ps1                    # compile every mod without starting the game
.\tools\check-mods.ps1 -Mod BombRace      # one mod
.\tools\package.ps1 -Version 1.0.0        # release zip in release\
.\tools\new-mod.ps1 MyMod                 # new mod folder from a template
.\tools\redraw-art.ps1                    # redraw the pixel art (Python with Pillow)
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
