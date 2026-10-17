param(
    [string]$Depot = 'D:\Otherreach_Depot\barrowworks_codex',
    [string]$Shared = 'C:\Otherreach_Recovery\Otherreach_Playtest_RESTORE_20261015b\fallback'
)
$ErrorActionPreference = 'Stop'
# Keep the original shared assets available alongside this dungeon's additions. No source workspace is modified.
# Whole directories are junctions wherever possible; only the creatures clip family and clip metadata overlap.
foreach ($category in Get-ChildItem -LiteralPath $Shared -Directory) {
    $destination = Join-Path $Depot $category.Name
    if (-not (Test-Path -LiteralPath $destination)) {
        New-Item -ItemType Junction -Path $destination -Target $category.FullName | Out-Null
    } elseif ($category.Name -in @('ready','rigged')) {
        foreach ($asset in Get-ChildItem -LiteralPath $category.FullName -Directory) {
            $target = Join-Path $destination $asset.Name
            if (-not (Test-Path -LiteralPath $target)) { New-Item -ItemType Junction -Path $target -Target $asset.FullName | Out-Null }
        }
    } elseif ($category.Name -eq 'animation') {
        foreach ($family in Get-ChildItem -LiteralPath "$Shared\animation\ready" -Directory) {
            $target = Join-Path "$Depot\animation\ready" $family.Name
            if (-not (Test-Path -LiteralPath $target)) {
                New-Item -ItemType Junction -Path $target -Target $family.FullName | Out-Null
            } else {
                foreach ($file in Get-ChildItem -LiteralPath $family.FullName -File) {
                    $path = Join-Path $target $file.Name
                    if (-not (Test-Path -LiteralPath $path)) { Copy-Item -LiteralPath $file.FullName -Destination $path }
                }
            }
        }
        foreach ($file in Get-ChildItem -LiteralPath "$Shared\animation\clips" -File) {
            $path = Join-Path "$Depot\animation\clips" $file.Name
            if (-not (Test-Path -LiteralPath $path)) { Copy-Item -LiteralPath $file.FullName -Destination $path }
        }
    }
}
