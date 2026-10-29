# Installs BepInEx, HalfGripTools and the HalfGripMods mods into Half Grip Demo. Safe to run again: it updates.
# Double-click Install-HalfGrip-Mods.bat, or run this file in PowerShell:
#   .\Install-HalfGrip-Mods.ps1
#   .\Install-HalfGrip-Mods.ps1 -GameDir "D:\Games\Half Grip Demo"
#   .\Install-HalfGrip-Mods.ps1 -Only M1911,RPG        only these mods
# What it does, in order (all of it is in this file, read it first if you like):
#   1. finds the game folder through Steam (or asks you)
#   2. BepInEx 5.4.23.5 x64 from github.com/BepInEx, checked against a fixed SHA-256, unless already installed
#   3. the latest HalfGripTools release from github.com/4l3x4nd3r0/HalfGripTools, checked against its SHA256SUMS.txt
#   4. the mods from github.com/4l3x4nd3r0/HalfGripMods (main branch) into BepInEx\HalfGripMods
# It touches nothing else: no save files, no other mods, no registry.
#   .\Install-HalfGrip-Mods.ps1 -Uninstall            removes HalfGripTools and the mods (asks first); BepInEx only if you say yes
#   .\Install-HalfGrip-Mods.ps1 -Uninstall -Yes       no questions; add -RemoveBepInEx to remove BepInEx too
# Uninstall never touches your saves.
param([string]$GameDir = "", [string[]]$Only = @(), [switch]$Uninstall, [switch]$Yes, [switch]$RemoveBepInEx)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Only = @($Only | ForEach-Object { $_ -split "[,;\s]+" } | Where-Object { $_ })
$AppId = "4163360"
$BepInExUrl = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip"
$BepInExSha = "82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4"
$ToolsApi = "https://api.github.com/repos/4l3x4nd3r0/HalfGripTools/releases/latest"
$ModsUrl = "https://github.com/4l3x4nd3r0/HalfGripMods/archive/refs/heads/main.zip"

function Step($n, $text) { Write-Host ""; Write-Host "[$n/4] $text" -ForegroundColor Cyan }
function Ok($text) { Write-Host "      $text" -ForegroundColor Green }
function Info($text) { Write-Host "      $text" }
function Stop-Install($text) { throw $text }

function Confirm-Yes($question, $default) {
	if ($Yes) { return $default }
	$answer = Read-Host "      $question (y/n)"
	return ($answer -match "^\s*y")
}

function Find-Game {
	$libs = New-Object System.Collections.Generic.List[string]
	$steamRoots = @()
	foreach ($key in "HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam") {
		$p = Get-ItemProperty $key -ErrorAction SilentlyContinue
		if ($p -and $p.SteamPath) { $steamRoots += ($p.SteamPath -replace "/", "\") }
		if ($p -and $p.InstallPath) { $steamRoots += $p.InstallPath }
	}
	$steamRoots += "C:\Program Files (x86)\Steam", "C:\Program Files\Steam"
	foreach ($root in ($steamRoots | Select-Object -Unique)) {
		if (-not (Test-Path $root)) { continue }
		$libs.Add($root)
		$vdf = Join-Path $root "steamapps\libraryfolders.vdf"
		if (Test-Path $vdf) {
			foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) { $libs.Add(($m.Groups[1].Value -replace "\\\\", "\")) }
		}
	}
	foreach ($lib in ($libs | Select-Object -Unique)) {
		$dir = "Half Grip Demo"
		$manifest = Join-Path $lib "steamapps\appmanifest_$AppId.acf"
		if (Test-Path $manifest) {
			$m = [regex]::Match((Get-Content $manifest -Raw), '"installdir"\s+"([^"]+)"')
			if ($m.Success) { $dir = $m.Groups[1].Value }
		}
		$candidate = Join-Path $lib "steamapps\common\$dir"
		if (Test-Path (Join-Path $candidate "HalfGrip.exe")) { return $candidate }
	}
	return $null
}

function Get-Download($url, $dest) {
	for ($try = 1; $try -le 3; $try++) {
		try { Invoke-WebRequest $url -OutFile $dest -UseBasicParsing; return }
		catch { if ($try -eq 3) { Stop-Install "Could not download $url`n      $($_.Exception.Message)`n      Check your internet connection and run the installer again." } ; Start-Sleep -Seconds 2 }
	}
}

function Test-Hash($file, $expected, $what) {
	$actual = (Get-FileHash $file -Algorithm SHA256).Hash
	if ($actual -ne $expected.ToUpper()) { Stop-Install "$what failed its SHA-256 check (got $actual, expected $expected). Nothing was installed from it. Try again later." }
}

$work = Join-Path $env:TEMP ("HalfGripInstaller-" + [guid]::NewGuid().ToString("N"))
try {
	Write-Host ""
	Write-Host "Half Grip mods installer" -ForegroundColor Yellow
	Write-Host "BepInEx + HalfGripTools + mods by 4l3x4nd3r"

	if ($Uninstall) { Write-Host ""; Write-Host "Looking for Half Grip Demo" -ForegroundColor Cyan } else { Step 1 "Looking for Half Grip Demo" }
	if ($GameDir -eq "") { $GameDir = Find-Game }
	if (-not $GameDir) {
		Info "Not found through Steam."
		Add-Type -AssemblyName System.Windows.Forms
		$dlg = New-Object System.Windows.Forms.FolderBrowserDialog
		$dlg.Description = "Pick the Half Grip Demo folder (the one with HalfGrip.exe). In Steam: right click the game, Manage, Browse local files."
		if ($dlg.ShowDialog() -eq "OK") { $GameDir = $dlg.SelectedPath }
	}
	if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir "HalfGrip.exe"))) { Stop-Install "HalfGrip.exe was not found in '$GameDir'. Run the installer again and pick the folder that contains HalfGrip.exe." }
	$GameDir = (Resolve-Path $GameDir).Path
	Ok "Game folder: $GameDir"
	if (Get-Process -Name "HalfGrip" -ErrorAction SilentlyContinue) { Stop-Install "Half Grip is running. Close the game and run the installer again." }

	if ($Uninstall) {
		Write-Host ""
		Write-Host "Uninstall" -ForegroundColor Cyan
		$modsDir = "$GameDir\BepInEx\HalfGripMods"
		$toolsDir = "$GameDir\BepInEx\plugins\HalfGripTools"
		$mods = @(); if (Test-Path $modsDir) { $mods = @(Get-ChildItem $modsDir -Directory | ForEach-Object { $_.Name }) }
		if (-not (Test-Path $toolsDir) -and $mods.Count -eq 0) {
			Ok "No HalfGripTools and no mods found, nothing to remove."
		} else {
			Info "This removes HalfGripTools and these mods: $(if ($mods.Count) { $mods -join ', ' } else { '(none)' })"
			Info "It also removes their settings and cache in BepInEx\config and BepInEx\cache. Your saves are not touched."
			if (-not (Confirm-Yes "Remove them?" $true)) { Stop-Install "Cancelled, nothing was removed." }
			foreach ($p in $modsDir, $toolsDir, "$GameDir\BepInEx\cache\HalfGripMods") { if (Test-Path $p) { Remove-Item $p -Recurse -Force } }
			foreach ($pattern in "halfgrip.*", "HalfGripTools.*") { Get-ChildItem "$GameDir\BepInEx\config" -Filter $pattern -File -ErrorAction SilentlyContinue | Remove-Item -Force }
			Ok "Removed HalfGripTools and the mods."
		}
		if (Test-Path "$GameDir\BepInEx") {
			$others = @(); if (Test-Path "$GameDir\BepInEx\plugins") { $others = @(Get-ChildItem "$GameDir\BepInEx\plugins" | ForEach-Object { $_.Name }) }
			Write-Host ""
			Info "BepInEx is the base that mod loaders run on. Remove it only if you want the game fully vanilla."
			if ($others.Count -gt 0) { Info "Still in BepInEx\plugins: $($others -join ', '). Those would stop working." }
			if ($RemoveBepInEx -or (-not $Yes -and (Confirm-Yes "Remove BepInEx too?" $false))) {
				foreach ($p in "$GameDir\BepInEx", "$GameDir\winhttp.dll", "$GameDir\doorstop_config.ini", "$GameDir\.doorstop_version") { if (Test-Path $p) { Remove-Item $p -Recurse -Force } }
				Ok "Removed BepInEx. The game is vanilla again."
			} else {
				Ok "BepInEx left in place."
			}
		}
		Write-Host ""
		Write-Host "Done. Your saves were not touched." -ForegroundColor Green
		return
	}

	New-Item -ItemType Directory -Force $work | Out-Null

	Step 2 "BepInEx (the base every mod loader needs)"
	if ((Test-Path "$GameDir\BepInEx\core\BepInEx.dll") -and (Test-Path "$GameDir\winhttp.dll")) {
		Ok "Already installed, left as it is."
	} else {
		Info "Downloading BepInEx 5.4.23.5 ..."
		$zip = Join-Path $work "bepinex.zip"
		Get-Download $BepInExUrl $zip
		Test-Hash $zip $BepInExSha "BepInEx"
		Expand-Archive $zip -DestinationPath $GameDir -Force
		Ok "Installed BepInEx 5.4.23.5 (checksum matched)."
	}

	Step 3 "HalfGripTools (loads and manages the mods)"
	Info "Looking up the latest release ..."
	$release = Invoke-RestMethod $ToolsApi -UseBasicParsing
	$zipAsset = $release.assets | Where-Object { $_.name -like "HalfGripTools-*.zip" } | Select-Object -First 1
	$sumAsset = $release.assets | Where-Object { $_.name -eq "SHA256SUMS.txt" } | Select-Object -First 1
	if (-not $zipAsset -or -not $sumAsset) { Stop-Install "The latest HalfGripTools release has no zip or SHA256SUMS.txt. Report this on the HalfGripTools GitHub page." }
	$zip = Join-Path $work $zipAsset.name
	$sums = Join-Path $work "SHA256SUMS.txt"
	Get-Download $zipAsset.browser_download_url $zip
	Get-Download $sumAsset.browser_download_url $sums
	$line = Get-Content $sums | Where-Object { $_ -match [regex]::Escape($zipAsset.name) } | Select-Object -First 1
	if (-not $line) { Stop-Install "SHA256SUMS.txt has no entry for $($zipAsset.name)." }
	Test-Hash $zip ($line -split "\s+")[0] "HalfGripTools"
	Expand-Archive $zip -DestinationPath $GameDir -Force
	Ok "Installed $($release.tag_name) (checksum matched)."

	Step 4 "Mods"
	Info "Downloading the mods ..."
	$zip = Join-Path $work "mods.zip"
	Get-Download $ModsUrl $zip
	Expand-Archive $zip -DestinationPath (Join-Path $work "mods") -Force
	$modsSrc = Get-ChildItem (Join-Path $work "mods") -Directory | Select-Object -First 1 | ForEach-Object { Join-Path $_.FullName "mods" }
	if (-not $modsSrc -or -not (Test-Path $modsSrc)) { Stop-Install "The mods download has no mods folder. Report this on the HalfGripMods GitHub page." }
	$installed = @()
	foreach ($dir in Get-ChildItem $modsSrc -Directory) {
		if ($Only.Count -gt 0 -and ($Only -notcontains $dir.Name)) { continue }
		robocopy $dir.FullName "$GameDir\BepInEx\HalfGripMods\$($dir.Name)" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
		if ($LASTEXITCODE -ge 8) { Stop-Install "Copying $($dir.Name) failed (robocopy code $LASTEXITCODE)." }
		$installed += $dir.Name
		Ok $dir.Name
	}
	if ($installed.Count -eq 0) { Stop-Install "No mod matched -Only '$($Only -join ', ')'." }

	foreach ($f in "BepInEx\core\BepInEx.dll", "BepInEx\plugins\HalfGripTools\HalfGripTools.dll") {
		if (-not (Test-Path (Join-Path $GameDir $f))) { Stop-Install "Something went wrong: $f is missing after the install." }
	}

	Write-Host ""
	Write-Host "Done. $($installed.Count) mod(s) installed." -ForegroundColor Green
	Write-Host "Start the game from Steam. The first start takes a few seconds longer while the mods compile."
	Write-Host "In the main menu, the puzzle piece button above the language flag opens the mod manager."
	Write-Host 'Items turn up in shops and race rewards. To get one now: type pirgflah in Settings, then press ` for the console. The commands are in the HalfGripMods README.'
	Write-Host "Something not working? Open BepInEx\LogOutput.log in the game folder and post it in the tooling bugs thread."
}
catch {
	Write-Host ""
	Write-Host "Stopped: $($_.Exception.Message)" -ForegroundColor Red
	Write-Host "Your saves were not touched. Fix the problem above and run it again."
	$global:LASTEXITCODE = 1
}
finally {
	if (Test-Path $work) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
}
