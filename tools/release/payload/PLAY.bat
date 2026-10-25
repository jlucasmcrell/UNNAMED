@echo off
cd /d "%~dp0"
set "UNNAMED_SCATTER_RESIDENCY=0"
set "UNNAMED_LOD_STIPPLE=0"
"%~dp0Otherreach.exe"
if errorlevel 1 pause

