@echo off
setlocal
cd /d "%~dp0"

if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
if exist "release" rmdir /s /q "release"

dotnet build
set "exitCode=%errorlevel%"

if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
if exist "release" rmdir /s /q "release"

endlocal & exit /b %exitCode%
