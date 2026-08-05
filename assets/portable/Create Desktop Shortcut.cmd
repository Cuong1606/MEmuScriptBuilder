@echo off
setlocal

set "MEMU_STUDIO_EXE=%~dp0MEmuScriptStudio.exe"

powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; $exe = [IO.Path]::GetFullPath($env:MEMU_STUDIO_EXE); if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw ('MEmuScriptStudio.exe was not found: ' + $exe) }; $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory); if ([string]::IsNullOrWhiteSpace($desktop)) { throw 'The current user Desktop folder could not be located.' }; $shortcutPath = Join-Path $desktop 'MEmu Script Studio.lnk'; $shell = New-Object -ComObject WScript.Shell; $shortcut = $shell.CreateShortcut($shortcutPath); $shortcut.TargetPath = $exe; $shortcut.WorkingDirectory = [IO.Path]::GetDirectoryName($exe); $shortcut.IconLocation = $exe + ',0'; $shortcut.Save(); if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) { throw ('The shortcut was not created: ' + $shortcutPath) }; Write-Output ('Desktop shortcut created or updated: ' + $shortcutPath)"
set "shortcutExitCode=%ERRORLEVEL%"

if not "%shortcutExitCode%"=="0" (
    echo Failed to create or update the Desktop shortcut.
    exit /b %shortcutExitCode%
)

echo Shortcut setup completed successfully.
exit /b 0
