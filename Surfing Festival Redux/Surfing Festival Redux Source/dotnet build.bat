@echo off
setlocal
cd /d "%~dp0"

dotnet build
set "exitCode=%errorlevel%"

for %%D in (bin obj release) do if exist "%%D" rmdir /s /q "%%D"

endlocal & exit /b %exitCode%
