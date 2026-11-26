@echo off
rem Runs the GPU benchmark (docs\BENCHMARK.md). Result: bench-result.json next to this script. Pass --quick for the short run.
cd /d "%~dp0"
if not exist dist\Wallpepper.exe call build.cmd
start "" /wait dist\Wallpepper.exe --bench --out "%~dp0bench-result.json" %*
type bench-result.json
