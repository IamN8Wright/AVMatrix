@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo The .NET 8 SDK is required to build InNasc.
  echo Download it from: https://dotnet.microsoft.com/download/dotnet/8.0
  echo Install the SDK, then run this file again.
  echo.
  pause
  exit /b 1
)

echo.
echo Restoring InNasc supplemental branding...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Restore-BrandAssets.ps1"
if errorlevel 1 (
  echo.
  echo Production branding could not be restored. Build stopped.
  echo.
  pause
  exit /b 1
)

echo.
echo Building InNasc for 64-bit Windows...
echo.

dotnet publish InNasc.csproj ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained true ^
  --output "publish\win-x64" ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false

if errorlevel 1 (
  echo.
  echo Build failed. Review the message above for details.
  echo.
  pause
  exit /b 1
)

echo.
echo Build complete:
echo %~dp0publish\win-x64\InNasc.exe
echo.
explorer "%~dp0publish\win-x64"
pause
