# Half Grip mods installer

One program that puts BepInEx, HalfGripTools and the mods into Half Grip Demo, and can also add mods from other people's GitHub pages. Everything it does is in plain C# in this folder.

## Using it

1. Download `HalfGrip-Mods-Installer.exe` from the [latest installer release](https://github.com/4l3x4nd3r0/HalfGripMods/releases) and double-click it.
   Windows may say "Windows protected your PC" because the file is new and unsigned. Press **More info**, then **Run anyway**.
2. Close the game first. The installer finds Half Grip through Steam (if not, it asks for the folder).
3. Press **INSTALL ALL**. It puts in what the mods need and every official mod. Or press **INSTALL** on a single card.
4. Start the game from Steam. The first start is a bit slower while the mods get ready.

To update, open the installer again. To take everything out, open the menu (the three lines, top right) and choose **Remove the mods from the game**. Your saves are never touched.

More, if you want it:
- **Click a card** to read about a mod: a big picture, the full text, where it comes from, and its button.
- **+ ADD**: paste the link of someone else's mods. Only add links from people you trust.
- **SUGGESTED** is one section with the mods the author recommends, whichever repository they come from. Nothing from it is installed until you press its button. Once you have, the mod moves up into **MODS**, with the official ones, and gets updates like any other mod. Mods from links you add yourself are in **MODS** too.
- **STAR** opens the GitHub page; press Star there if you like the mods.
- **CHECK NOW** asks GitHub straight away. Otherwise the installer asks **once an hour**, not every time it opens.
- The window can be any size from 640x360 up, the sizes the game itself runs windowed at. It reflows as you drag it.

## What it will and will not do

- Downloads only from `github.com`: BepInEx (the version in `config.json`, checked against its SHA-256), the newest HalfGripTools (checked against its `SHA256SUMS.txt`), the mods.
- Writes in the game folder only: `BepInEx\`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, plus `BepInEx\HalfGripMods\installer-sources.txt` (the links you added and where each mod came from). Its own files are in `%USERPROFILE%\AppData\LocalLow\Half Games\HalfGripMods`: `config.json`, and a `cache` of GitHub's answers. Nothing about you is kept there.
- Refuses any mod that contains a program file (`.dll .exe .so .dylib .bat .cmd .ps1 .vbs .msi .scr .com`): a mod here is readable source and pictures only.
- No registry writes (it only reads Steam's path), no saves, no other mods. A log of what it did is `%TEMP%\HalfGrip-Mods-Installer.log`.

---

## For the author

### config.json: what can change without a new installer

`installer\config.json` in this repository. The installer keeps a saved copy in `LocalLow\Half Games\HalfGripMods\config.json`:

- **First run**: the saved copy is written from the copy built into the .exe.
- **Every start, and a window left open**: if the last try was over an hour ago, it asks GitHub for `raw.githubusercontent.com/4l3x4nd3r0/HalfGripMods/main/installer/config.json`. **CHECK NOW** and `--now` always ask.
- **GitHub's copy wins.** A valid answer replaces the saved file. One that is not valid (a typo, a bad address) or no connection leaves the saved file alone and writes a line in the log, so a mistake on GitHub cannot break an installer that is already out.

```json
{
  "bepinex": {
    "version": "5.4.23.5",
    "url": "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip",
    "sha256": "82F9...32C4",
    "icon": "https://raw.githubusercontent.com/4l3x4nd3r0/HalfGripMods/main/installer/assets/bepinex.png"
  },
  "halfGripTools": { "repo": "4l3x4nd3r0/HalfGripTools", "icon": "https://raw.githubusercontent.com/4l3x4nd3r0/HalfGripTools/main/icon.png" },
  "mods": { "repo": "4l3x4nd3r0/HalfGripMods" },
  "installer": { "repo": "4l3x4nd3r0/HalfGripMods" },
  "recommended": [
    "https://github.com/htubdawg/ProcTrack"
  ]
}
```

- **A new BepInEx**: change `version`, `url` and `sha256`. The address must be an official BepInEx release on GitHub and the checksum 64 hex digits, or the whole file is refused. Players with an older BepInEx get UPDATE on its card.
- **Pictures** (`icon`): `https` links on `github.com` or `raw.githubusercontent.com`, a PNG under 256 KB. Left out, or not reachable, the picture built into the .exe is used.
- **Repositories**: `owner/repo`, where the loader's releases, the official mods and the installer's own releases are.
- **Recommended mods**: paste the GitHub link, nothing else. Anything the **+ ADD** box takes works (`https://github.com/owner/repo`, `owner/repo`, `.../tree/<branch>/<folder>`, a direct `.zip`); at most 12. The installer downloads it like any pasted link and takes the name, version, author, description and picture from the mod's own `mod.json`; the line under the section title is the repository's description on GitHub. A link that fails the rules is not shown. **Install all** never takes suggestions. Installing one makes its link one of the player's own, so it gets updates from then on.

Whoever can push to this repository decides what players are offered, so keep two-factor on the account. The installer cannot judge what a mod's code does.

### Mod pictures

A mod's `mod.json` can say `"icon": "icon.png"`: a PNG inside the mod's folder (HalfGripTools 1.0.5 and newer show it in the in-game mod manager too). It is only a picture; the sprites the game uses for the mod's items stay in `sprites\`. Without it the installer takes the first `sprites\*icon*.png`, then shows a letter. `art\make_icons.py` draws the official ones from the mods' own sprites, plus the HalfGripTools hand with a wrench, the BepInEx plug and the installer's picture and `.ico` (all original, drawn there).

### How the hourly check works

Answers from GitHub (the config, the HalfGripTools release, the repository descriptions, the newest installer release) and the downloaded mod zips are kept for one hour. Opening the installer within the hour asks GitHub nothing. After an hour, or on **CHECK NOW**, it asks again. If GitHub cannot be reached, the saved copy is used and the window says so. The official mods always come from the `main` branch, never from a release, so a release of the installer does not freeze the mods.

GitHub lets one PC ask its API 60 times an hour without logging in. The installer uses a handful, so players will not notice; a day of testing will, and the installer then says so in plain words.

### Releasing (by hand, only when the installer itself changes)

Mod updates need no release: push to `main` and players get them. Release the installer only when this folder's code changes. Push `installer\config.json` to `main` before the first release: until it is there the installer runs on the copy built into it.

1. Raise `<Version>` in `HalfGripModsInstaller.csproj` (for example `1.0.1`).
2. `.\build.ps1` makes `out\HalfGrip-Mods-Installer.exe` and `out\SHA256SUMS.txt` (close the installer first, Windows will not replace a running .exe).
3. On GitHub: Releases, **Draft a new release**, tag `installer-v1.0.1` (the tag must hold the version number), upload the two files from `out\`.
4. Players with an older installer see a yellow strip in the window telling them to download it. It compares the tag with their own version.

Do not put the .exe in the repository (`installer/out/` is ignored). Write the release notes in plain words about what changed for players.

### Building and reading the code

Needs the .NET SDK 8. Players need nothing: .NET Framework 4.8 is part of Windows 10 and 11.

```powershell
.\build.ps1      # out\HalfGrip-Mods-Installer.exe and out\SHA256SUMS.txt
```

| File | What it does |
|---|---|
| `src\Program.cs` | start-up, command line, the log, the picture test |
| `src\MainForm.cs` | the window: decides what happens, picks the scale for the window size, tells the views what to show |
| `src\TopBar.cs` | title, add / star / menu buttons, tabs, search, the add-link box (flows into rows) |
| `src\HubCanvas.cs` | the grid of cards (and `Face`, what a card shows), sections |
| `src\DetailsPanel.cs` | the page that opens on a card click, and the How to use and About pages |
| `src\StatusBar.cs` | the bottom bar: state, "updated N min ago", the main button |
| `src\Surface.cs`, `src\Theme.cs` | how the views draw and react; colours, fonts, the window scale, the game-style buttons |
| `src\Config.cs` | `config.json`: the saved copy, the hourly refresh, the rules it is checked against |
| `src\GameFolder.cs` | finds the game through Steam |
| `src\Sources.cs` | turns a pasted link into a download address |
| `src\Recommendations.cs` | turns the suggested links into sources |
| `src\Checker.cs` | downloads, unpacks, finds mods, all the rules |
| `src\Setup.cs` | BepInEx, HalfGripTools, copying mods, remembering links, removal |
| `src\Cache.cs`, `src\Net.cs` | the data folder and one-hour cache; downloads, checksums, plain error messages |
| `src\SelfUpdate.cs` | notices a newer installer release |
| `src\MiniJson.cs` | copy of HalfGripTools' JSON reader, so a `mod.json` is read the same way |
| `config.json`, `assets\` | the first copy of the config, the pictures and the pixel font (VT323, SIL Open Font License, `assets\OFL.txt`), all built into the .exe |

Without a window (for testing):

```powershell
HalfGrip-Mods-Installer.exe --check   --game-dir "D:\Games\Half Grip Demo" --add https://github.com/owner/repo
HalfGrip-Mods-Installer.exe --install --game-dir "D:\Games\Half Grip Demo"
```

Also `--now` (ask GitHub again) and `--config <file or address>` (read another config, to try one before pushing it). The report goes to the log file. To try things without touching the real settings or game: `--data-dir <folder>` keeps `config.json` and the cache in that folder, `--mods-dir <folder>` takes the official mods from a folder on this PC, and `--render out.png --size 640x360 [--page how|about|<card key>]` draws the window into a picture (off screen, nothing is captured from the desktop).

### The old installer

`legacy_installer\` holds the first version, a PowerShell script with two .bat files. It still works and does the same steps in a console. It does not get new features.
