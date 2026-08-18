@echo off
setlocal
cd /d "%~dp0"

if not exist "scripts\setup-and-build.ps1" (
  echo ERROR: scripts\setup-and-build.ps1 was not found.
  exit /b 2
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup-and-build.ps1" -BuildInstaller
exit /b %ERRORLEVEL%
