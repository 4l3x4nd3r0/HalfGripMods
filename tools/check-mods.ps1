# Compiles every mod's C# source against the game, BepInEx and HalfGripTools, without starting the game, to catch errors early.
# It uses the same language version (C# 10) as the compiler inside HalfGripTools. Nothing is written to the repo.
# HalfGripTools must be installed in the game (see the README), its DLL is the one the mods compile against.
#   .\check-mods.ps1                          all mods
#   .\check-mods.ps1 -Mod BombRace            one mod
#   .\check-mods.ps1 -GameDir "D:\Games\Half Grip Demo"
#   .\check-mods.ps1 -Tools "C:\path\HalfGripTools.dll"    use another copy of the DLL
param([string]$GameDir = "F:\SteamLibrary\steamapps\common\Half Grip Demo", [string]$Mod = "", [string]$Tools = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") {
	$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
	$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

if ($Tools -eq "") { $Tools = "$GameDir\BepInEx\plugins\HalfGripTools\HalfGripTools.dll" }
if (-not (Test-Path $Tools)) { throw "HalfGripTools.dll not found at $Tools. Install HalfGripTools first (README, step 2), or pass -Tools." }

$failed = @()
foreach ($dir in Get-ChildItem "$root\mods" -Directory) {
	if ($Mod -ne "" -and $dir.Name -ne $Mod) { continue }
	if (-not (Test-Path "$($dir.FullName)\src")) { Write-Host "$($dir.Name): no code, nothing to compile"; continue }
	$work = Join-Path $env:TEMP ("halfgrip-check-" + $dir.Name)
	New-Item -ItemType Directory -Force $work | Out-Null
	$managed = "$GameDir\HalfGrip_Data\Managed"
	@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>10.0</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AssemblyName>HalfGripMod.$($dir.Name)</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$($dir.FullName)\src\**\*.cs" />
    <Reference Include="$managed\*.dll" Exclude="$managed\mscorlib.dll;$managed\netstandard.dll;$managed\System*.dll" />
    <Reference Include="$GameDir\BepInEx\core\BepInEx.dll" />
    <Reference Include="$GameDir\BepInEx\core\0Harmony.dll" />
    <Reference Include="$Tools" />
  </ItemGroup>
</Project>
"@ | Set-Content "$work\check.csproj" -Encoding utf8
	Write-Host "== $($dir.Name)"
	dotnet build "$work\check.csproj" -c Release -v q --nologo "-p:OutDir=$work\out\"
	if ($LASTEXITCODE -ne 0) { $failed += $dir.Name }
}
if ($failed.Count -gt 0) { throw "These mods do not compile: $($failed -join ', ')" }
Write-Host "All mods compile."
