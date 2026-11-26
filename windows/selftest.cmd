@echo off
rem Sets every wallpaper of the library on the real desktop and checks that it renders (also after a lock, a lost
rem graphics device and destroyed wallpaper windows). Result: selftest.txt next to this script. Takes a few minutes.
cd /d "%~dp0"
if not exist dist\Wallpepper.exe call build.cmd
start "" /wait dist\Wallpepper.exe --selftest --out "%~dp0selftest.txt"
type selftest.txt
