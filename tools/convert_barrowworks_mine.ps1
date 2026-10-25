param(
    [string]$Depot = 'D:\Otherreach_Depot\barrowworks_codex',
    [string]$Scratch = 'D:\Otherreach_Scratch\barrowworks\codex',
    [string[]]$Only = @()
)
# The Old Mine kit for the room builder's shoring, rails, carts, tools and rubble. Megascans scans are Nanite sources of
# up to 3 M triangles; each is decimated to a prop budget here, at its source texture resolution.
$ErrorActionPreference = 'Stop'
$blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
$converter = Join-Path $PSScriptRoot 'asset_pipeline\ue_export\convert_ue.py'
$sources = 'C:\Otherreach_External_Assets\UE_Exports\Scene_OldMine\Manifests'
$modules = [ordered]@{
    'His_Med_Beam_Wood_Worn_03' = @('bw_codex_mine_beam', 1200)
    'His_Med_Beam_Wood_Worn_01' = @('bw_codex_mine_beam_b', 1200)
    'Ind_Min_Rail_Tracks_Wood_Modular_01__SM_Ind_Min_Rail_Tracks_Wood_Modular_01_A' = @('bw_codex_mine_rails', 6000)
    'Ind_Min_Cart_Metal_Old_01' = @('bw_codex_mine_cart', 24000)
    'Ind_Min_Cart_Log_Wood_Old_01' = @('bw_codex_mine_log_cart', 24000)
    'His_Med_Tools_Pickaxe_Metal_Old_03' = @('bw_codex_mine_pickaxe', 4000)
    'Ind_Min_Tool_Shovel_Old_01' = @('bw_codex_mine_shovel', 4000)
    'His_Med_Tools_Hammer_Metal_Old_01' = @('bw_codex_mine_hammer', 2500)
    'Ind_Rub_Debris_Board_Wood_Old_02' = @('bw_codex_mine_board', 4000)
    'Des_Wes_Rock_Sand_Weathered_25' = @('bw_codex_mine_slab', 3000)
    'Des_Pak_Patch_Ground_Rocky_06' = @('bw_codex_mine_rubble', 4000)
    'Coa_Har_Patch_Ground_Coarse_01' = @('bw_codex_mine_spoil', 4000)
}
New-Item -ItemType Directory -Force -Path "$Depot\ready", "$Scratch\convert" | Out-Null
foreach ($module in $modules.GetEnumerator()) {
    $asset, $budget = $module.Value
    if ($Only.Count -and $asset -notin $Only) { continue }
    $manifest = Get-ChildItem $sources -Filter "*$($module.Key).json" | Select-Object -First 1
    if (-not $manifest) { throw "No Old Mine manifest for $($module.Key)" }
    & $blender --background --factory-startup --threads 4 --python-exit-code 1 --python $converter -- `
        --manifest $manifest.FullName --asset-id $asset --res 2048 --max-tris $budget `
        --out "$Depot\ready" --work "$Scratch\convert" `
        --licence 'Fab Standard Personal; owned Old Mine scene (Quixel Megascans)' `
        --licence-evidence 'C:\Otherreach_Recovery\ASSET_REBUILD\FAB_LIBRARY_CATALOGUE.md' `
        --source-url 'Fab library: Scene_OldMine (C:\Otherreach_External_Assets\Fab\FabAssets\Content\Scene_OldMine)' `
        *> "$Scratch\convert-$asset.log"
    if ($LASTEXITCODE -ne 0) { throw "Conversion failed: $asset (see $Scratch\convert-$asset.log)" }
    Write-Output "converted $asset"
}
