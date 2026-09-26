# Animated Armour v2: build the plates on the old skeleton, bake the 2k PBR atlas, export the rigged GLB.
# Usage: powershell -File run_pipeline.ps1 [-Out <dir>]   (default: assets\rigged\creature_animated_armour_v2)
param([string]$Out = "G:\UNNAMED_PHASEB\assets\rigged\creature_animated_armour_v2")
$B = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
$T = $PSScriptRoot
$ST = "G:\UNNAMED_PHASEB\assets\_staging\armour"
New-Item -ItemType Directory -Force $ST, $Out | Out-Null
& $B -b --factory-startup -P "$T\build_armour.py" -- "$ST\armour_v2_build.blend" "$ST\armour_v2_build_report.json" 2>&1 |
    Select-String -Pattern "(BUILD|SAVED|UNMAPPED|Error|Traceback)" | ForEach-Object { $_.Line }
& $B -b --factory-startup -P "$T\bake_export.py" -- "$ST\armour_v2_build.blend" $Out creature_animated_armour_v2 2048 48 2>&1 |
    Select-String -Pattern "(BAKED|MAPS|EXPORTED|FINAL|Error|Traceback)" | ForEach-Object { $_.Line }
