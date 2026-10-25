# Installs the latest HalfGripTools release every time, then copies the mods into the game folder: BepInEx\HalfGripMods\<Mod>.
# Other mods already there are left alone. BepInEx must be installed already (README, step 1).
#   .\install.ps1
#   .\install.ps1 -Mod SniperRifle              one mod
#   .\install.ps1 -GameDir "D:\Games\Half Grip Demo"
#   .\install.ps1 -SkipTools                    mods only, no HalfGripTools download
# The HalfGripTools zip is checked against the release's SHA256SUMS.txt before anything is copied.
# It overwrites the installed HalfGripTools, so a local build newer than the release is replaced too (use -SkipTools to keep it).
param([string]$GameDir = "F:\SteamLibrary\steamapps\common\Half Grip Demo", [string]$Mod = "", [switch]$SkipTools)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = Split-Path -Parent $PSScriptRoot
$ToolsApi = "https://api.github.com/repos/4l3x4nd3r0/HalfGripTools/releases/latest"
$toolsDll = "$GameDir\BepInEx\plugins\HalfGripTools\HalfGripTools.dll"
if (-not (Test-Path "$GameDir\BepInEx\core\BepInEx.dll")) { throw "BepInEx is not installed in $GameDir (README, step 1)" }

if (-not $SkipTools) {
	Write-Host "HalfGripTools"
	if (Get-Process -Name "HalfGrip" -ErrorAction SilentlyContinue) { throw "Half Grip is running. Close the game and run the installer again." }
	$work = Join-Path $env:TEMP ("HalfGripTools-" + [guid]::NewGuid().ToString("N"))
	try {
		$release = Invoke-RestMethod $ToolsApi -UseBasicParsing
		$zipAsset = $release.assets | Where-Object { $_.name -like "HalfGripTools-*.zip" } | Select-Object -First 1
		$sumAsset = $release.assets | Where-Object { $_.name -eq "SHA256SUMS.txt" } | Select-Object -First 1
		if (-not $zipAsset -or -not $sumAsset) { throw "The latest HalfGripTools release has no zip or SHA256SUMS.txt." }
		New-Item -ItemType Directory -Force $work | Out-Null
		$zip = Join-Path $work $zipAsset.name
		Invoke-WebRequest $zipAsset.browser_download_url -OutFile $zip -UseBasicParsing
		Invoke-WebRequest $sumAsset.browser_download_url -OutFile (Join-Path $work "SHA256SUMS.txt") -UseBasicParsing
		$line = Get-Content (Join-Path $work "SHA256SUMS.txt") | Where-Object { $_ -match [regex]::Escape($zipAsset.name) } | Select-Object -First 1
		if (-not $line) { throw "SHA256SUMS.txt has no entry for $($zipAsset.name)." }
		$expected = ($line -split "\s+")[0]
		$actual = (Get-FileHash $zip -Algorithm SHA256).Hash
		if ($actual -ne $expected.ToUpper()) { throw "HalfGripTools failed its SHA-256 check (got $actual, expected $expected). Nothing was installed." }
		Expand-Archive $zip -DestinationPath $GameDir -Force
		Write-Host "  Installed $($release.tag_name) (checksum matched)."
	}
	catch {
		if (Test-Path $toolsDll) { Write-Warning "Could not update HalfGripTools: $($_.Exception.Message) Keeping the installed one." }
		else { throw }
	}
	finally { if (Test-Path $work) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue } }
}
elseif (-not (Test-Path $toolsDll)) { Write-Warning "HalfGripTools is not installed in $GameDir. The mods will not load without it." }

foreach ($dir in Get-ChildItem "$root\mods" -Directory) {
	if ($Mod -ne "" -and $dir.Name -ne $Mod) { continue }
	robocopy $dir.FullName "$GameDir\BepInEx\HalfGripMods\$($dir.Name)" /MIR /NFL /NDL /NJH /NJS | Out-Null
	Write-Host "Installed $($dir.Name)"
}
