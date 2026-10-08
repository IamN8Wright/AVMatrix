@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-QC.ps1" %*
set "innascQcExit=%errorlevel%"
echo.
pause
exit /b %innascQcExit%
