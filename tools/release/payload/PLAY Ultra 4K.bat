@echo off
cd /d "%~dp0"
set "UNNAMED_SCATTER_RESIDENCY=0"
set "UNNAMED_LOD_STIPPLE=0"
"%~dp0Otherreach.exe" --resolution 3840x2160 -- --region region.starting_frontier --content-root "%~dp0content" --bindings-overlay "%~dp0content\_art_bindings.json" --asset-root "%~dp0primary" --asset-fallback "%~dp0fallback" --scatter-rules "%~dp0content\_scatter_rules.json" --profile "%~dp0saves" --visual tier=ultra,terrain=terrain3d,sky=sky3d,people=production,foldscar=proof
if errorlevel 1 pause
