@echo off
rem Developer loop: stop a running instance, build, start the new build without installing (debug.log in dist).
cd /d "%~dp0"
if exist dist\Wallpepper.exe start "" /wait dist\Wallpepper.exe --exit
timeout /t 2 /nobreak >nul
call build.cmd
findstr /c:"=== exit 0" build.log >nul || exit /b 1
if not exist dist\debuglog.on echo on> dist\debuglog.on
if exist dist\debug.log del dist\debug.log
start "" dist\Wallpepper.exe --portable
