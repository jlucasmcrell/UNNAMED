# Builds the full playable playtest folder: export (exe + data), content, assets, launchers. Prints and records the size.
# usage: powershell -File tools\release\build_playtest.ps1 -Label 20261017a [-Commit release/pipeline] [-AssetBase <last playable folder>]
#
# Output: D:\Otherreach_Scratch\release\<Label>\game      the folder to ship (package_parts.ps1 zips it)
#         D:\Otherreach_Scratch\release\<Label>\logs      export log and build_report.json
# primary\ and fallback\ are the asset workspaces. They are not generated from the repo, so they come from -AssetBase
# (the last hand-assembled playable folder); content\ comes from the repo at -Commit.
param(
    [Parameter(Mandatory)][ValidatePattern('^\d{8}[a-z]$')][string]$Label,
    [string]$Commit = 'release/pipeline',
    [string]$AssetBase = 'C:\Otherreach_Recovery\Otherreach_Playtest_RESTORE_20261015b',
    [string]$OutRoot = 'D:\Otherreach_Scratch\release',
    [string]$ExportWorktree = 'C:\Otherreach_Build\release_export_wt'
)
$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'
$here = Split-Path $MyInvocation.MyCommand.Path
$repo = Resolve-Path "$here\..\.."
$out = "$OutRoot\$Label"; $game = "$out\game"; $logs = "$out\logs"
$payload = "$here\payload"

function Size($path) { if (Test-Path $path) { [long](Get-ChildItem $path -Recurse -File -Force | Measure-Object Length -Sum).Sum } else { 0L } }
function GB($bytes) { '{0:N2} GB' -f ($bytes / 1GB) }
function Robo($from, $to, $extra = '') {
    cmd /c "robocopy `"$from`" `"$to`" /E /MT:16 /R:2 /W:2 /NFL /NDL /NJH /NP $extra >> `"$logs\robocopy.log`" 2>&1"
    if ($LASTEXITCODE -ge 8) { throw "robocopy $from -> $to exit $LASTEXITCODE (see $logs\robocopy.log)" }
}

if ((Get-PSDrive C).Free -lt 190GB) { throw 'C: is below the 190 GB floor' }
if ((Get-PSDrive D).Free -lt 100GB) { throw 'D: has under 100 GB free' }
if (Test-Path $out) { throw "$out already exists; pick a new label" }
New-Item -ItemType Directory -Force $game, $logs | Out-Null
$sha = (cmd /c "git -C `"$repo`" rev-parse $Commit^^{commit} 2>nul")
if ($LASTEXITCODE -ne 0) { throw "unknown commit $Commit" }

# before: the last playable folder as it stands
$before = [ordered]@{ total = Size $AssetBase; fallback = Size "$AssetBase\fallback"; content = Size "$AssetBase\content" }
"Label $Label, commit $sha, assets from $AssetBase"

"--- export"
powershell -NoProfile -ExecutionPolicy Bypass -File "$repo\tools\export_playtest_clean.ps1" $sha "$out\export" "$logs\export.log" $ExportWorktree
if ($LASTEXITCODE -ne 0) { throw "export failed (see $logs\export.log)" }
Robo "$out\export" $game
foreach ($unwanted in 'steam_api64.dll', 'libgodotsteam*', 'libterrabrush*') {
    if (Get-ChildItem $game -Filter $unwanted) { throw "$unwanted was exported; the export must exclude it" }
}
Remove-Item -Recurse -Force "$out\export"

"--- content"
Robo "$ExportWorktree\content" "$game\content" '/XF *.before_* _forest_patch_probe.json'

"--- primary"
Robo "$AssetBase\primary" "$game\primary"
# The obsolete sapling's opaque branch cards are a playtest visual blocker.
# It has no canon/quest dependency: omit this scatter kind rather than repair it.
$scatterPath = "$game\primary\scatter_rules.json"
if (Test-Path $scatterPath) {
    $scatter = Get-Content $scatterPath -Raw | ConvertFrom-Json
    if ($scatter.kinds.saplings.mesh -eq 'model:flora_young_ash_stand') {
        $scatter.kinds.PSObject.Properties.Remove('saplings')
        [IO.File]::WriteAllText($scatterPath, ($scatter | ConvertTo-Json -Depth 30), (New-Object Text.UTF8Encoding $false))
        'Omitted obsolete opaque-card sapling scatter.'
    }
}

"--- fallback"
# Only ArtLibrary reads the fallback, through ready\, rigged\, animation\ready\ and materials\ (and their texture_cache). Audio, clips, manifests,
# rigs, sockets, sky, ui, vfx and the animation source/review folders are read from primary only. A file the primary also holds is never reached.
$fallbackKept = 'ready', 'rigged', 'materials', 'animation\ready', 'texture_cache'
foreach ($dir in $fallbackKept) { if (Test-Path "$AssetBase\fallback\$dir") { Robo "$AssetBase\fallback\$dir" "$game\fallback\$dir" } }
$shadowed = 0L; $fallbackRoot = "$game\fallback\"
foreach ($file in Get-ChildItem "$game\fallback" -Recurse -File -Force | Where-Object { $_.FullName -notlike "${fallbackRoot}texture_cache\*" }) {
    if (Test-Path "$game\primary\$($file.FullName.Substring($fallbackRoot.Length))") { $shadowed += $file.Length; Remove-Item $file.FullName }
}
# The fallback cache manifest: keep the entries whose source is still in the fallback, then the .dds files they use.
$manifestPath = "$game\fallback\texture_cache\manifest.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$kept = [ordered]@{}; $keepDds = @{}
foreach ($entry in $manifest.entries.PSObject.Properties) {
    if (Test-Path "$game\fallback\$(($entry.Name -split '#')[0] -replace '/', '\')") { $kept[$entry.Name] = $entry.Value; $keepDds[$entry.Value.dds] = $true }
}
[IO.File]::WriteAllText($manifestPath, ([pscustomobject]@{ comment = $manifest.comment; entries = $kept } | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
$orphans = Get-ChildItem "$game\fallback\texture_cache" -Filter *.dds | Where-Object { -not $keepDds.ContainsKey($_.Name) }
$orphanBytes = ($orphans | Measure-Object Length -Sum).Sum
$orphans | Remove-Item
Get-ChildItem "$game\fallback" -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending | Where-Object { -not (Get-ChildItem $_.FullName -Force) } | Remove-Item -Force

"--- launchers, notices, route"
Robo $payload $game '/XD .git'
Copy-Item "$here\README_TESTER.md" "$game\README.md"
# The CMU motion-capture clips (rancidmilk pack) ship in fallback\animation\ready; their terms travel with them.
$attribution = "$AssetBase\fallback\animation\clips\ATTRIBUTION.md"
$cmuClips = @(Get-ChildItem "$game\fallback\animation\ready", "$game\primary\animation" -Recurse -File -Filter '*cmu*' -ErrorAction SilentlyContinue).Count
if ($cmuClips -gt 0) {
    if (-not (Test-Path $attribution)) { throw "$cmuClips CMU clips ship but $attribution is missing" }
    Copy-Item $attribution "$game\THIRD_PARTY_NOTICES\CMU_mocap_ATTRIBUTION.md"
}
Set-Content "$game\VERSION.txt" "build $Label`ncommit $sha" -Encoding ascii
New-Item -ItemType Directory -Force "$game\saves" | Out-Null

"--- verify"
python "$here\approved_player_bodies.py" $game --verify
if ($LASTEXITCODE -ne 0) { throw 'Approved body verification failed; do not ship this package' }
# B-002: a >=2K source with no signed cache entry makes the game refuse to boot, and the export's own
# check runs before primary and fallback are staged beside it, so the assembled folder is gated here.
python "$here\check_texture_cache.py" $game "$logs\texture_cache_audit.json"
if ($LASTEXITCODE -ne 0) { throw 'large art texture cache is missing/stale/uncompressed/downscaled (see logs\texture_cache_audit.json)' }
foreach ($required in 'Otherreach.exe', 'PLAY.bat', 'PLAY Ultra 4K.bat', 'README.md', 'connected_route.json', 'THIRD_PARTY_NOTICES', 'content\_art_bindings.json', 'content\_scatter_rules.json', 'primary\bindings.json', 'fallback\ready') {
    if (-not (Test-Path "$game\$required")) { throw "assembled folder lacks $required" }
}
if (-not (Get-ChildItem $game -Directory -Filter 'data_*')) { throw 'assembled folder lacks the exe data folder' }
if (Get-ChildItem "$game\content" -Recurse -File | Where-Object { $_.Name -like '*.before_*' -or $_.Name -eq '_forest_patch_probe.json' }) { throw 'a backup or probe file is still in content' }

$after = [ordered]@{ total = Size $game; exe = (Get-Item "$game\Otherreach.exe").Length; fallback = Size "$game\fallback"; primary = Size "$game\primary"; content = Size "$game\content" }
$report = [ordered]@{
    label = $Label; commit = $sha; assetBase = $AssetBase; fileCount = (Get-ChildItem $game -Recurse -File).Count
    before = $before; after = $after
    savedBytes = [ordered]@{ total = $before.total - $after.total; fallbackDropped = $before.fallback - $after.fallback; fallbackShadowedByPrimary = $shadowed; fallbackOrphanTextureCache = [long]$orphanBytes }
}
$report | ConvertTo-Json -Depth 4 | Set-Content "$logs\build_report.json" -Encoding utf8
cmd /c "git -C `"$repo`" worktree remove --force `"$ExportWorktree`" >nul 2>&1"
if (Test-Path $ExportWorktree) { Remove-Item -Recurse -Force $ExportWorktree; cmd /c "git -C `"$repo`" worktree prune >nul 2>&1" }
"Before: $(GB $before.total) (fallback $(GB $before.fallback), content $(GB $before.content))"
"After:  $(GB $after.total) (exe $(GB $after.exe), primary $(GB $after.primary), fallback $(GB $after.fallback), content $(GB $after.content))"
"Saved:  $(GB ($before.total - $after.total)); report $logs\build_report.json; folder $game"
