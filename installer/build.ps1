# Builds the installer: out\HalfGrip-Mods-Installer.exe and out\SHA256SUMS.txt. Needs the .NET SDK (8 or newer); players need nothing.
#   .\build.ps1
$ErrorActionPreference = "Stop"
if (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") {
	$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
	$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$out = Join-Path $PSScriptRoot "out"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet build (Join-Path $PSScriptRoot "HalfGripModsInstaller.csproj") -c Release -o $out --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
$exe = Join-Path $out "HalfGrip-Mods-Installer.exe"
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
"$hash  HalfGrip-Mods-Installer.exe" | Set-Content (Join-Path $out "SHA256SUMS.txt") -Encoding ascii
Write-Host "Built $exe"
Write-Host "SHA-256 $hash"
