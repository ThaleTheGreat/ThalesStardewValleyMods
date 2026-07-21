@echo off
setlocal
pushd "%~dp0" >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "package-release.ps1"
set "exitCode=%ERRORLEVEL%"
if not "%exitCode%"=="0" goto build_failed

powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; $source = [System.IO.Path]::GetFullPath('%~dp0'); $names = @('obj', 'bin', 'Release'); $targets = @(Get-ChildItem -LiteralPath $source -Directory -Recurse -Force -ErrorAction SilentlyContinue | Where-Object { $names -contains $_.Name }); $parentRelease = [System.IO.Path]::GetFullPath((Join-Path $source '..\Release')); if (Test-Path -LiteralPath $parentRelease) { $targets += Get-Item -LiteralPath $parentRelease }; $targets = @($targets | Sort-Object FullName -Unique | Sort-Object { $_.FullName.Length } -Descending); foreach ($target in $targets) { Remove-Item -LiteralPath $target.FullName -Recurse -Force -ErrorAction Stop }; $remaining = @(Get-ChildItem -LiteralPath $source -Directory -Recurse -Force -ErrorAction SilentlyContinue | Where-Object { $names -contains $_.Name }); if (Test-Path -LiteralPath $parentRelease) { $remaining += Get-Item -LiteralPath $parentRelease }; if ($remaining.Count -gt 0) { throw ('Cleanup failed: ' + (($remaining | ForEach-Object { $_.FullName }) -join '; ')) }"
set "exitCode=%ERRORLEVEL%"
if not "%exitCode%"=="0" goto cleanup_failed
goto finish

:build_failed
echo.
pause
goto finish

:cleanup_failed
echo.
echo Build succeeded, but cleanup of obj, bin, or Release failed.
pause

:finish
popd >nul
exit /b %exitCode%
