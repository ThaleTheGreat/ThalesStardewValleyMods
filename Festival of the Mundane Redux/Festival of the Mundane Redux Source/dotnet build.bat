@echo off
setlocal
pushd "%~dp0" >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "package-release.ps1"
set "exitCode=%ERRORLEVEL%"
popd >nul
if not "%exitCode%"=="0" (
  echo.
  pause
)
exit /b %exitCode%
