param(
    [string]$Depot = 'D:\Otherreach_Depot\barrowworks_codex',
    [string]$Scratch = 'D:\Otherreach_Scratch\barrowworks\codex'
)
$ErrorActionPreference = 'Stop'
$blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
$converter = Join-Path $PSScriptRoot 'asset_pipeline\ue_export\convert_ue.py'
$sources = 'C:\Otherreach_External_Assets\UE_Exports\LostTemple\Manifests'
$modules = @{
    'SM_middlewall01_a' = 'bw_codex_masonry'
    'SM_ceiling01' = 'bw_codex_ceiling'
    'SM_floor02' = 'bw_codex_floor'
    'SM_small_pillar' = 'bw_codex_pillar'
    'SM_brizer' = 'bw_codex_brazier'
}
New-Item -ItemType Directory -Force -Path "$Depot\ready", "$Scratch\convert" | Out-Null
foreach ($module in $modules.GetEnumerator()) {
    $asset = $module.Value
    & $blender --background --factory-startup --threads 4 --python-exit-code 1 --python $converter -- `
        --manifest "$sources\$($module.Key).json" --asset-id $asset --res 2048 --max-tris 18000 `
        --out "$Depot\ready" --work "$Scratch\convert" --save-blend `
        --licence 'Fab Standard Personal; owned Lost Temple Environment by DanStudios' `
        --licence-evidence 'C:\Otherreach_Recovery\ASSET_REBUILD\FAB_LICENCES.md' `
        --source-url 'https://www.fab.com/listings/48148dbe-67c8-40a1-af9b-041fa4b9354e' `
        *> "$Scratch\convert-$asset.log"
    if ($LASTEXITCODE -ne 0) { throw "Conversion failed: $asset (see $Scratch\convert-$asset.log)" }
}
