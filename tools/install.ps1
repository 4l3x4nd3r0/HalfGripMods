# Copies the mods into the game folder: BepInEx\HalfGripMods\<Mod>. Other mods already there are left alone.
# HalfGripTools must be installed too (README, step 2). HalfGripTools compiles the mods when the game starts.
#   .\install.ps1
#   .\install.ps1 -Mod SniperRifle              one mod
#   .\install.ps1 -GameDir "D:\Games\Half Grip Demo"
param([string]$GameDir = "F:\SteamLibrary\steamapps\common\Half Grip Demo", [string]$Mod = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path "$GameDir\BepInEx\core\BepInEx.dll")) { throw "BepInEx is not installed in $GameDir (README, step 1)" }
if (-not (Test-Path "$GameDir\BepInEx\plugins\HalfGripTools\HalfGripTools.dll")) { Write-Warning "HalfGripTools is not installed in $GameDir. The mods will not load without it (README, step 2)." }
foreach ($dir in Get-ChildItem "$root\mods" -Directory) {
	if ($Mod -ne "" -and $dir.Name -ne $Mod) { continue }
	robocopy $dir.FullName "$GameDir\BepInEx\HalfGripMods\$($dir.Name)" /MIR /NFL /NDL /NJH /NJS | Out-Null
	Write-Host "Installed $($dir.Name)"
}
