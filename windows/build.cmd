@echo off
rem Builds dist\Wallpepper.exe (needs the .NET 10 SDK). Output and errors go to build.log next to this script.
cd /d "%~dp0"
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1
echo === build %date% %time% > build.log
dotnet publish Wallpepper.csproj -c Release -o dist -nologo -clp:NoSummary >> build.log 2>&1
echo === exit %errorlevel% >> build.log
