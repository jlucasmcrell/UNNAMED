@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup.ps1" %*
set "setup_exit=%errorlevel%"
echo.
if not defined OTHERREACH_SETUP_NOPAUSE pause
exit /b %setup_exit%
