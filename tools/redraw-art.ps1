# Redraws the pixel art with the scripts in art\ and copies the results into each mod's sprites folder.
# Not needed to play or to build: the finished PNGs are already in mods\*\sprites. Needs Python with Pillow
# (numpy for the Sniper art; UnityPy for the RPG art, which reads the game's launcher texture).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$art = "$root\art"
foreach ($script in "make_sprites", "make_rpg_sprites", "make_sniper_sprites", "make_bomb_sprites", "make_dacia_sprites", "make_speed_sprites", "make_highway_icon") {
	python -I "$art\$script.py"
	if ($LASTEXITCODE -ne 0) { throw "$script failed" }
}
function Put($pattern, $mod, $exclude = @()) {
	Get-ChildItem "$art\$pattern" -Exclude $exclude | Copy-Item -Destination "$root\mods\$mod\sprites" -Force
}
Put "m1911_*.png" "M1911"
Put "rpg_*.png" "RPG" @("rpg_preview.png")
Put "sniper_*.png" "SniperRifle" @("sniper_preview.png", "sniper_render.png")
Put "bomb_*.png" "BombRace" @("bomb_preview.png")
Put "dacia_icon.png" "DaciaBody"
Put "highway_icon.png" "Highway"
foreach ($stem in "glove", "rabbitfoot", "chute") { Put "${stem}_*.png" "SpeedCharms" }
Write-Host "Done."
