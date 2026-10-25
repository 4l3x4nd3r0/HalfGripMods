# Runs the Multiplayer mod in real copies of the game, headless, two or three at a time, and checks what happens.
# Needs the game installed (read-only: the copies link to its data folders), BepInEx and HalfGripTools in it, and no window: they run with -batchmode.
# Nothing here touches your save: the test copies run the mod with saves locked (-mprig), and each test also proves the in-memory save is unchanged.
#
#   .\tools\mp-test.ps1                          the quick suite: a two-player race
#   .\tools\mp-test.ps1 -Suite all               every scenario below (about 25 minutes)
#   .\tools\mp-test.ps1 -Suite combat,respawn    some of them
#
# Scenarios:
#   race        two players race one lap with bots; finish order, results, back to the garage, save unchanged
#   ai          the same with two AI rivals driven by the host
#   combat      the host shoots the other player with the revolver until wrecked (damage numbers are checked), demolition mode, spectator camera
#   respawn     the same with the respawn rule
#   hostquit    the host leaves mid-race: the client is told and returns to the menu
#   clientquit  a client leaves mid-race: the host finishes alone
#   mismatch    a player whose mod files differ is refused, with the reason
#   trio        three players race
#   highway     a room race on the Highway mod's map (needs the Highway mod)
#   unload      switching the mod off restores the menu
#   maps        every track has enough grid spots
#   steam       one game, the real Steam backend: create a lobby, find it by code (creates a Steam lobby for a few seconds)
#   idle        control: the save does not change by itself in the menu
#
# Screenshots and logs of each run are kept in <Work>\out\<scenario>.
param(
	[string[]]$Suite = @("race"),
	[string]$GameDir = "F:\SteamLibrary\steamapps\common\Half Grip Demo",
	[string]$Work = (Join-Path $env:TEMP "halfgrip-mp-rig")
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if ($Suite -contains "all") { $Suite = @("idle", "unload", "maps", "race", "ai", "combat", "respawn", "hostquit", "clientquit", "mismatch", "trio", "highway", "steam") }

function Start-Rig([string]$Name, [string]$Scenario, [string]$Label, [string]$Out, [string[]]$Mods, [string]$More, [switch]$Tamper) {
	$rig = Join-Path $Work "game\$Name"
	if (-not (Test-Path (Join-Path $rig "HalfGrip.exe"))) {
		New-Item -ItemType Directory -Force $rig | Out-Null
		robocopy $GameDir $rig /E /XD HalfGrip_Data MonoBleedingEdge HalfGripMods cache /XF LogOutput.log HalfGripTools.mods.json /NFL /NDL /NJH /NJS /NP | Out-Null
		cmd /c mklink /J "$rig\HalfGrip_Data" "$GameDir\HalfGrip_Data" | Out-Null
		cmd /c mklink /J "$rig\MonoBleedingEdge" "$GameDir\MonoBleedingEdge" | Out-Null
	}
	$modsDir = Join-Path $rig "BepInEx\HalfGripMods"
	if (Test-Path $modsDir) { Remove-Item $modsDir -Recurse -Force }
	New-Item -ItemType Directory -Force $modsDir | Out-Null
	foreach ($m in $Mods) { robocopy (Join-Path $repo "mods\$m") (Join-Path $modsDir $m) /E /NFL /NDL /NJH /NJS /NP | Out-Null }
	if ($Tamper) { Set-Content (Join-Path $modsDir "Multiplayer\tamper.txt") "different" }
	Remove-Item (Join-Path $rig "BepInEx\LogOutput.log"), (Join-Path $rig "BepInEx\cache"), (Join-Path $rig "BepInEx\config\halfgrip.*") -Recurse -Force -ErrorAction SilentlyContinue
	$log = Join-Path $rig "unity.log"
	$arguments = "-batchmode -logFile `"$log`" -screen-width 1920 -screen-height 1080 -mprig $Scenario -mpdir `"$Out`" -mplabel $Label $More"
	Start-Process -FilePath (Join-Path $rig "HalfGrip.exe") -ArgumentList $arguments -WorkingDirectory $rig | Out-Null
}

function Wait-Rigs([string]$Out, [string[]]$Labels, [int]$Seconds) {
	$deadline = (Get-Date).AddSeconds($Seconds)
	while ((Get-Date) -lt $deadline) {
		if (-not ($Labels | Where-Object { -not (Test-Path (Join-Path $Out "done_$_.txt")) })) { break }
		Start-Sleep -Seconds 2
	}
	Start-Sleep -Seconds 3
	Get-Process HalfGrip -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Work*" } | Stop-Process -Force
}

function Show-Results([string]$Out, [string[]]$Labels) {
	$failed = 0
	foreach ($l in $Labels) {
		$f = Join-Path $Out "result_$l.txt"
		$lines = if (Test-Path $f) { Get-Content $f } else { @("FAIL no result file") }
		$done = if (Test-Path (Join-Path $Out "done_$l.txt")) { Get-Content (Join-Path $Out "done_$l.txt") } else { "NOT FINISHED" }
		$bad = $lines | Where-Object { $_ -like "FAIL*" }
		Write-Host ("  rig {0}: {1} checks, {2} failed, {3}" -f $l, $lines.Count, @($bad).Count, $done)
		foreach ($b in $bad) { Write-Host "      $b" -ForegroundColor Red }
		if (@($bad).Count -gt 0 -or $done -ne "OK") { $failed++ }
	}
	return $failed
}

$summary = @()
foreach ($name in $Suite) {
	$out = Join-Path $Work "out\$name"
	if (Test-Path $out) { Remove-Item $out -Recurse -Force }
	New-Item -ItemType Directory -Force $out | Out-Null
	Write-Host "== $name"
	$mods = @("Multiplayer"); $labels = @("A", "B"); $seconds = 300
	switch ($name) {
		"race"       { $mode = "race" }
		"ai"         { $mode = "ai" }
		"combat"     { $mode = "combat"; $seconds = 360 }
		"respawn"    { $mode = "respawn"; $seconds = 420 }
		"hostquit"   { $mode = "hostquit" }
		"clientquit" { $mode = "clientquit" }
		"trio"       { $mode = "race"; $labels = @("A", "B", "C"); $seconds = 420 }
		"highway"    { $mode = "watch"; $mods = @("Multiplayer", "Highway"); $seconds = 420 }
		"mismatch"   { $mode = "race" }
		default      { $mode = "race" }
	}
	switch ($name) {
		{ $_ -in "idle", "unload", "maps", "steam" } {
			$labels = @("A")
			$scenario = if ($name -eq "steam") { "steamsmoke" } else { $name }
			Start-Rig "A" $scenario "A" $out $mods ""
			Wait-Rigs $out $labels 420
		}
		"mismatch" {
			Start-Rig "A" "hostwatch" "A" $out $mods "-mplocal 1 -mpid 101"
			Start-Sleep -Seconds 4
			Start-Rig "B" "badclient" "B" $out $mods "-mplocal 1 -mpid 202" -Tamper
			Wait-Rigs $out $labels 240
		}
		default {
			$players = $labels.Count
			$more = "-mplocal 1 -mpmode $mode -mpplayers $players" + $(if ($name -eq "highway") { " -mpmap Highway" } else { "" })
			Start-Rig "A" "host" "A" $out $mods "$more -mpid 101"
			Start-Sleep -Seconds 4
			Start-Rig "B" "client" "B" $out $mods "$more -mpid 202"
			if ($players -ge 3) { Start-Sleep -Seconds 4; Start-Rig "C" "client" "C" $out $mods "$more -mpid 303" }
			Wait-Rigs $out $labels $seconds
		}
	}
	$bad = Show-Results $out $labels
	$summary += [pscustomobject]@{ Scenario = $name; Result = $(if ($bad -eq 0) { "PASS" } else { "FAIL" }) }
}
Write-Host ""
$summary | Format-Table -AutoSize
if ($summary | Where-Object { $_.Result -ne "PASS" }) { exit 1 }
