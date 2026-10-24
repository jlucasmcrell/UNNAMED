# Copy the Barrowworks' own runtime assets from the owned depot into a playtest package's fallback folder.
# The ring ships on by default, so a package without these draws greybox rooms and an unanimated Custodian.
# Then build the ring's native scenes in place (they hold absolute paths): one headless review route with the package's own
# launch arguments plus --barrowworks-review <scratch dir> --build-scene-cache. Without them the ring loads from its GLBs
# behind the entry veil, slower but identical.
param([Parameter(Mandatory)][string]$Fallback, [string]$Depot = 'D:\Otherreach_Depot\barrowworks_codex')
$ErrorActionPreference = 'Stop'
if (-not (Test-Path (Join-Path $Fallback 'ready'))) { throw "$Fallback is not a package fallback folder (no ready\)" }
$copied = 0
function Copy-Tree([string]$from, [string]$to) {
    New-Item -ItemType Directory -Force $to | Out-Null
    robocopy $from $to /E /XF *.scn *.scn.sha256 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy $from failed ($LASTEXITCODE)" }
    $script:copied++
}
foreach ($asset in Get-ChildItem (Join-Path $Depot 'ready') -Directory -Filter 'bw_codex_*') {
    Copy-Tree $asset.FullName (Join-Path $Fallback "ready\$($asset.Name)")
}
Copy-Tree (Join-Path $Depot 'rigged\bw_codex_custodian') (Join-Path $Fallback 'rigged\bw_codex_custodian')
foreach ($pattern in 'clips\anim.creature.bw_codex_custodian.*', 'ready\creatures\anim.creature.bw_codex_custodian.*') {
    $target = Join-Path $Fallback ('animation\' + (Split-Path $pattern))
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item (Join-Path $Depot "animation\$pattern") $target -Force
}
Copy-Item (Join-Path $Depot 'CREDITS.txt') (Join-Path $Fallback 'BARROWWORKS_CREDITS.txt') -Force
"Barrowworks assets: $copied model folders, Custodian clips and credits copied into $Fallback"
