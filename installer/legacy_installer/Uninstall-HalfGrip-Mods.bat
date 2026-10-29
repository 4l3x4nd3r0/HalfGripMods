@echo off
title Half Grip mods uninstaller
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-HalfGrip-Mods.ps1" -Uninstall %*
echo.
pause
