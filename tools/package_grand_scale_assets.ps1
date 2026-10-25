# Copy the grand-scale landmarks' own models (gs_*) from the owned depot into a playtest package's fallback folder.
# Without them the Foldscar draws its stones, heart and debris in greybox. Rerun after each grand-scale landmark lands.
param([Parameter(Mandatory)][string]$Fallback, [string]$Depot = 'D:\Otherreach_Depot\grand_scale')
$ErrorActionPreference = 'Stop'
if (-not (Test-Path (Join-Path $Fallback 'ready'))) { throw "$Fallback is not a package fallback folder (no ready\)" }
$copied = 0
foreach ($asset in Get-ChildItem (Join-Path $Depot 'ready') -Directory -Filter 'gs_*') {
    robocopy $asset.FullName (Join-Path $Fallback "ready\$($asset.Name)") /E /XF *_preview.png /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy $($asset.Name) failed ($LASTEXITCODE)" }
    $copied++
}
Copy-Item (Join-Path $Depot 'CREDITS.txt') (Join-Path $Fallback 'GRAND_SCALE_CREDITS.txt') -Force
"Grand-scale assets: $copied model folders and credits copied into $Fallback"
