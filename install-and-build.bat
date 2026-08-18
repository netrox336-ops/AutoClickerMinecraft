@echo off
setlocal
cd /d "%~dp0"

if not exist "scripts\setup-and-build.ps1" (
  echo ERROR: scripts\setup-and-build.ps1 was not found.
  echo Extract the complete ZIP before running this file.
  pause
  exit /b 2
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup-and-build.ps1" -InstallDependencies -BuildInstaller -Run
set "APP_EXIT=%ERRORLEVEL%"

if not "%APP_EXIT%"=="0" (
  echo.
  echo Build failed with exit code %APP_EXIT%.
  pause
)

exit /b %APP_EXIT%
