# Copy the den gorge's own models (gorge_*) from the owned depot into a playtest package's fallback folder (spec 28).
# Without them the gorge draws its cliff runs, roots, broken stone and Halda's rope in greybox. Keep the 4K sources full resolution:
# the BC7 texture cache compresses them.
param([Parameter(Mandatory)][string]$Fallback, [string]$Depot = 'D:\Otherreach_Depot\gorge')
$ErrorActionPreference = 'Stop'
if (-not (Test-Path (Join-Path $Fallback 'ready'))) { throw "$Fallback is not a package fallback folder (no ready\)" }
$copied = 0
foreach ($asset in Get-ChildItem (Join-Path $Depot 'ready') -Directory -Filter 'gorge_*') {
    robocopy $asset.FullName (Join-Path $Fallback "ready\$($asset.Name)") /E /XF *_preview.png /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy $($asset.Name) failed ($LASTEXITCODE)" }
    $copied++
}
Copy-Item (Join-Path $Depot 'CREDITS.txt') (Join-Path $Fallback 'GORGE_CREDITS.txt') -Force
"Gorge assets: $copied model folders and credits copied into $Fallback"
