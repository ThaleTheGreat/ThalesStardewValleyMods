@echo off
setlocal
pushd "%~dp0" >nul
dotnet build "Gatherers\Gatherers.csproj"
set "exitCode=%ERRORLEVEL%"
if not "%exitCode%"=="0" goto build_failed

powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; $source = [System.IO.Path]::GetFullPath('%~dp0'); $targets = @(); foreach ($name in @('obj', 'bin', 'Release')) { $direct = Join-Path $source $name; if (Test-Path -LiteralPath $direct) { $targets += Get-Item -LiteralPath $direct }; $targets += @(Get-ChildItem -LiteralPath $source -Directory -Recurse -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq $name }) }; $parentRelease = [System.IO.Path]::GetFullPath((Join-Path $source '..\Release')); if (Test-Path -LiteralPath $parentRelease) { $targets += Get-Item -LiteralPath $parentRelease }; $targets | Sort-Object { $_.FullName.Length } -Descending -Unique | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop }"
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
