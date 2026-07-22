@echo off
setlocal
pushd "%~dp0" >nul

call :clean
if errorlevel 1 goto cleanup_failed

dotnet build "Gatherers.sln" -c Release
set "exitCode=%ERRORLEVEL%"
if not "%exitCode%"=="0" goto build_failed

call :clean
if errorlevel 1 goto cleanup_after_failed

echo.
echo Build succeeded.
echo Both mods were deployed under Stardew Valley\Mods\Gatherers.
goto finish

:clean
if exist "Gatherers\bin" rd /s /q "Gatherers\bin"
if exist "Gatherers\obj" rd /s /q "Gatherers\obj"
if exist "Gatherers Automate Integration\bin" rd /s /q "Gatherers Automate Integration\bin"
if exist "Gatherers Automate Integration\obj" rd /s /q "Gatherers Automate Integration\obj"
if exist "Gatherers\release" rd /s /q "Gatherers\release"
if exist "Gatherers\Release" rd /s /q "Gatherers\Release"
if exist "Gatherers Automate Integration\release" rd /s /q "Gatherers Automate Integration\release"
if exist "Gatherers Automate Integration\Release" rd /s /q "Gatherers Automate Integration\Release"
if exist "release" rd /s /q "release"
if exist "Release" rd /s /q "Release"
if exist "Gatherers\bin" exit /b 1
if exist "Gatherers\obj" exit /b 1
if exist "Gatherers Automate Integration\bin" exit /b 1
if exist "Gatherers Automate Integration\obj" exit /b 1
if exist "Gatherers\release" exit /b 1
if exist "Gatherers\Release" exit /b 1
if exist "Gatherers Automate Integration\release" exit /b 1
if exist "Gatherers Automate Integration\Release" exit /b 1
if exist "release" exit /b 1
if exist "Release" exit /b 1
exit /b 0

:cleanup_failed
set "exitCode=1"
echo.
echo Cleanup failed before the build.
goto failed

:build_failed
echo.
echo Build failed.
goto failed

:cleanup_after_failed
set "exitCode=1"
echo.
echo Build and deployment succeeded, but cleanup failed.
goto failed

:failed
pause

:finish
popd >nul
exit /b %exitCode%
