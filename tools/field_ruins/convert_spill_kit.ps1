# The Spill's ruin pieces (docs/FIELD_RUINS.md): Red Echo and Monolith Megastructure meshes from their UE exports, converted by
# convert_ue.py at the scale and turn each placement needs. No LODs on purpose: the site is drawn at full detail on Ultra.
param(
    [string]$Depot = 'D:\Otherreach_Depot\field_ruins',
    [string]$Scratch = 'D:\Otherreach_Scratch\field_ruins_convert',
    [string[]]$Only = @(),
    [int]$Res = 0   # every piece at this texture side instead of its own (4096: the full-resolution set for the BC7 texture cache)
)
$ErrorActionPreference = 'Stop'
$blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
$converter = Join-Path $PSScriptRoot '..\asset_pipeline\ue_export\convert_ue.py'
$exports = 'C:\Otherreach_External_Assets\UE_Exports'
# id = kit, manifest, scale, yaw degrees, texture side
$pieces = [ordered]@{
    'ruin_fs_rib_a'    = 'Red_Echo', 'Meshes__Modules__SM_Alien_module_06', 1.0, 0, 2048
    'ruin_fs_rib_b'    = 'Red_Echo', 'Meshes__Modules__SM_Alien_module_06', 0.9, 33, 2048
    'ruin_fs_rib_c'    = 'Red_Echo', 'Meshes__Modules__SM_Alien_module_06', 0.8, 118, 2048
    'ruin_fs_block'    = 'Red_Echo', 'Meshes__Alien_Rock_modules__Medium_modules__SM_medium_module_06', 0.8, 15, 2048
    'ruin_fs_slab'     = 'Red_Echo', 'Meshes__Alien_Rock_modules__Large_modules__SM_large_module_02', 0.2, 5, 2048
    'ruin_fs_pylon_a'  = 'Red_Echo', 'Meshes__Modules__SM_Alien_module_05', 2.5, 0, 2048
    'ruin_fs_pylon_b'  = 'Red_Echo', 'Meshes__Modules__SM_Alien_module_07', 2.2, 0, 2048
    'ruin_fs_pylon_c'  = 'Red_Echo', 'Meshes__Modules__SM_Alien_module_03', 2.5, 0, 2048
    'ruin_fs_stub_a'   = 'Red_Echo', 'Meshes__Modules__SM_Alien_module_08', 1.5, 0, 2048
    'ruin_fs_stub_b'   = 'Red_Echo', 'Meshes__Modules__SM_Alien_module_01', 1.5, 0, 2048
    'ruin_fs_stub_c'   = 'Red_Echo', 'Meshes__Modules__SM_Alien_module_04', 1.5, 0, 2048
    'ruin_fs_needle'   = 'Red_Echo', 'Meshes__Modules__SM_Alien_module_09', 12.0, 0, 2048
    'ruin_fs_gate'     = 'Red_Echo', 'Meshes__Portal__SM_vertical_portal', 0.8, 0, 2048
    'ruin_fs_colonnade'= 'Red_Echo', 'Meshes__Modules__SM_alien_Arch', 1.0, 0, 2048
    'ruin_fs_panel'    = 'Red_Echo', 'Meshes__Modules__SM_Alien_module_02', 1.0, 0, 2048
    'ruin_fs_anchor'   = 'Monolith_megastructure', 'Meshes__Generator__SM_generator', 0.3, 0, 2048
    'ruin_fs_frame'    = 'Monolith_megastructure', 'Meshes__Pillars__SM_large_arch_pillar', 0.5, 0, 2048
}
New-Item -ItemType Directory -Force -Path "$Depot\ready", "$Scratch" | Out-Null
foreach ($piece in $pieces.GetEnumerator()) {
    $asset = $piece.Key
    if ($Only.Count -and $asset -notin $Only) { continue }
    $kit, $mesh, $scale, $yaw, $res = $piece.Value
    if ($Res) { $res = $Res }
    & $blender --background --factory-startup --threads 4 --python-exit-code 1 --python $converter -- `
        --manifest "$exports\$kit\Manifests\$mesh.json" --asset-id $asset --res $res --lods ',' `
        --scale $scale --yaw $yaw --category structure --out "$Depot\ready" --work "$Scratch" `
        --licence "Fab Standard License, owned ($($kit -replace '_', ' ') UE export)" `
        --note 'The Spill, docs/FIELD_RUINS.md' *> "$Scratch\convert-$asset.log"
    if ($LASTEXITCODE -ne 0) { throw "Conversion failed: $asset (see $Scratch\convert-$asset.log)" }
    "converted $asset"
}
