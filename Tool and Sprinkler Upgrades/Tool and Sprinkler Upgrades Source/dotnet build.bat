@echo off
setlocal
pushd "%~dp0"

dotnet build "ToolAndSprinklerUpgrades.csproj"
set "exitCode=%ERRORLEVEL%"

if exist "obj" rmdir /s /q "obj"
if exist "bin" rmdir /s /q "bin"

popd
exit /b %exitCode%
