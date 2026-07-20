@echo off
setlocal
pushd "%~dp0" >nul
dotnet build "ItRubsTheMayoIn.csproj"
set "exitCode=%ERRORLEVEL%"
popd >nul
if not "%exitCode%"=="0" (
  echo.
  pause
)
exit /b %exitCode%
