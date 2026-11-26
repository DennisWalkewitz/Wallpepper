@echo off
rem Runs the unit tests (needs the .NET 10 SDK). Result: test.log next to this script.
cd /d "%~dp0"
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1
echo === test %date% %time% > test.log
dotnet test tests\Wallpepper.Tests\Wallpepper.Tests.csproj -c Release -nologo >> test.log 2>&1
echo === exit %errorlevel% >> test.log
