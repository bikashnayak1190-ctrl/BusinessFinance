@echo off
setlocal
cd /d "%~dp0"
dotnet restore Finance.Desktop\Finance.Desktop.csproj
if errorlevel 1 exit /b 1
dotnet run --project Finance.Tests\Finance.Tests.csproj -c Release
if errorlevel 1 exit /b 1
dotnet publish Finance.Desktop\Finance.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o out\BusinessFinance
if errorlevel 1 exit /b 1
echo Developer build complete. Output: out\BusinessFinance
