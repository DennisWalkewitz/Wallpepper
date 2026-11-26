@echo off
rem Compiles every shader in ..\shaders with the app's own compiler and measures GPU ms per 1080p frame.
rem Result: shader-check.txt next to this script (name ^| OK or ERROR ^| gpu_ms ^| first error line).
cd /d "%~dp0"
if not exist dist\Wallpepper.exe call build.cmd
if exist shader-check.txt del shader-check.txt
start "" /wait dist\Wallpepper.exe --check "%~dp0..\shaders" "%~dp0shader-check.txt"
