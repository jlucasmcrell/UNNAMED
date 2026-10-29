# The scholar's robe: only the female kimono part is exported, which is the one the scholar (Sel Arien) wears.
$ErrorActionPreference = 'Stop'
$root = 'D:\Otherreach_Scratch\codex_f1_siann_wt'
$contract = Join-Path $root 'work\creator\game\primary\rigged\humgen_creator_f_outfit_outfit_extra_outfits_pack_kimono\creator_part.json'
if (Test-Path $contract) { Move-Item $contract (Join-Path $root 'work\creator\guard_pre_correctives\role_f_kimono.json') -Force }
& (Join-Path $root 'work\creator\spawn_cpu_tool.ps1') -Executable 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' `
    -JobArguments @('-b', '--python', (Join-Path $root 'tools\creator\export_humgen_creator_parts.py'), '--', 'D:\oc',
        (Join-Path $root 'src\Presentation\Art\humgen_creator.json'), 'outfit.extra_outfits_pack.kimono') `
    -Log (Join-Path $root 'work\creator\correctives.log') -Job 'GateC-outfit-kimono' -LimitSeconds 3600 -FailIfStderrMatches 'Traceback'
Write-Host 'kimono exported'
