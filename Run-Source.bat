@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo The .NET 8 SDK is required.
  echo Download it from: https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)

dotnet run --project AVMatrixStudio.csproj
if errorlevel 1 pause

