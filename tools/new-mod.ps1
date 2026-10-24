# Makes a new mod folder from a template: mods\<Name>\mod.json and src\Mod.cs.
#   .\new-mod.ps1 MyMod
param([Parameter(Mandatory = $true)][string]$Name)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dir = "$root\mods\$Name"
if (Test-Path $dir) { throw "$dir already exists" }
New-Item -ItemType Directory -Force "$dir\src", "$dir\sprites" | Out-Null
$id = $Name.ToLowerInvariant() -replace "[^a-z0-9_-]", ""
@"
{
  "id": "$id",
  "name": "$Name",
  "version": "0.1.0",
  "author": "",
  "description": "",
  "gameVersion": "0.418"
}
"@ | Set-Content "$dir\mod.json" -Encoding utf8
@"
using HalfGripTools;

namespace $Name
{
	public class Mod : HalfGripMod
	{
		public override void Load(ModContext ctx)
		{
			ctx.Log.LogInfo("$Name says hello");
			// ctx.Config.Bind(...)   settings, saved to BepInEx\config\halfgrip.$id.cfg
			// ctx.PatchAll();        apply every [HarmonyPatch] class in this mod
			// ctx.Items.Add(...)     put an item in the game's catalog
		}
	}
}
"@ | Set-Content "$dir\src\Mod.cs" -Encoding utf8
Write-Host "Created $dir. Copy it to BepInEx\HalfGripMods in the game folder (or run install.ps1) and start the game."

