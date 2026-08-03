@echo off
setlocal

pushd "%~dp0.." >nul
if errorlevel 1 exit /b 1

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0launch-smoke.ps1"
set "launchExitCode=%ERRORLEVEL%"

popd >nul
exit /b %launchExitCode%
