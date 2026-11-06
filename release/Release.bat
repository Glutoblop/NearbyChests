@echo off
rem Double-click to bump the patch version, build, commit + push, and publish a GitHub release.
rem "Release.bat --build-only" just builds the package (no bump, no publish).
rem Other options are passed through, e.g.:  Release.bat -Bump minor -Changes "Added X"
if /i "%~1"=="--build-only" (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" -BuildOnly
) else (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" %*
)
echo.
pause
