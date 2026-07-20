@echo off
setlocal
pushd "%~dp0" >nul
dotnet build "Mapster.csproj"
set "exitCode=%ERRORLEVEL%"
popd >nul
if not "%exitCode%"=="0" (
  echo.
  pause
)
exit /b %exitCode%
