@echo off
title Half Grip mods installer
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-HalfGrip-Mods.ps1" %*
echo.
pause
