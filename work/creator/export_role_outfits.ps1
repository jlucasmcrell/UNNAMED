# Export the role outfits Gate C dresses its NPCs in, each with the garment lining (so no skin shows through an opening).
$ErrorActionPreference = 'Stop'
$root = 'D:\Otherreach_Scratch\codex_f1_siann_wt'
$outfits = 'outfit.casual.weekend_warrior', 'outfit.extra_outfits_pack.lab_tech', 'outfit.winter.frosty_evening', 'outfit.casual.stylish_casual'
$log = Join-Path $root 'work\creator\correctives.log'
$index = 0
foreach ($outfit in $outfits) {
    $index++
    $job = 'GateC-outfit-' + $index
    Write-Host "=== $outfit ==="
    & (Join-Path $root 'work\creator\spawn_cpu_tool.ps1') -Executable 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' `
        -JobArguments @('-b', '--python', (Join-Path $root 'tools\creator\export_humgen_creator_parts.py'), '--', 'D:\oc',
            (Join-Path $root 'src\Presentation\Art\humgen_creator.json'), $outfit) `
        -Log $log -Job $job -LimitSeconds 3600 -FailIfStderrMatches 'Traceback'
}
Write-Host 'role outfits exported'
