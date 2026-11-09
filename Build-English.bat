@echo off
rem Double-click to build the apps into the HoloStudio3D-App folder (it opens when the build is done).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-English.ps1"
echo.
pause
