@echo off
setlocal
pushd "%~dp0" >nul
dotnet build "UniversalUIMoreStats.csproj"
set "exitCode=%ERRORLEVEL%"
if not "%exitCode%"=="0" goto build_failed

powershell -NoProfile -ExecutionPolicy Bypass -Command "$source = [System.IO.Path]::GetFullPath('%~dp0'); $targets = @(Get-ChildItem -LiteralPath $source -Directory -Recurse -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -in @('obj', 'bin', 'Release') }); $parentRelease = [System.IO.Path]::GetFullPath((Join-Path $source '..\Release')); if (Test-Path -LiteralPath $parentRelease) { $targets += Get-Item -LiteralPath $parentRelease }; $targets | Sort-Object { $_.FullName.Length } -Descending -Unique | Remove-Item -Recurse -Force -ErrorAction Stop"
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
