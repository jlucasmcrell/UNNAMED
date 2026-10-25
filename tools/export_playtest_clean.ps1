# The playtest export (from the UI pass, 2026-10-15), from a clean worktree at a commit, so nothing uncommitted in the shared checkout (another
# agent's work in progress) is compiled into the build. The gitignored addons/ (less the editor-only and unused ones below) and the .godot
# import cache are copied in, so the import is quick.
# Every step's exit code is checked, and the log is scanned for GDExtension and export errors: the script exits 1 on the first failure.
# With -Primary <package asset root>, the score is installed into it and checked (owner ruling 2026-10-24: the music ships with the package).
# usage: powershell -File export_playtest_clean.ps1 <commit> <out dir> <log> [worktree dir] [-Primary <dir>]
param([string]$Commit, [string]$Out, [string]$Log, [string]$Wt = 'C:\Otherreach_Build\export_wt', [string]$Primary)
$ErrorActionPreference = 'Continue'; $ProgressPreference = 'SilentlyContinue'
$repo = 'C:\Otherreach_Recovery\UNNAMED'
# $Wt is outside C:\Otherreach_Recovery, which Google Drive syncs: it held the old worktree's files open, and 2 GB of copies would upload.
$wt = $Wt
$tools = 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31'
$engine = "$tools\godot\Godot_v4.7.2-stable_mono_win64_console.exe"
$env:DOTNET_ROOT = 'C:\Program Files\dotnet'; $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:PATH = "C:\Program Files\dotnet;$env:PATH"
$env:GODOT_AI_DISABLE_TELEMETRY = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
# Not shipped: godot_ai is editor-only; godotsteam, GD-Sync and terrabrush are referenced by no game code (checked by grep) and cost
# DLLs beside the exe, a missing-library error in the editor (terrabrush) and size in the pack.
$unshipped = 'godot_ai', 'godotsteam', 'GD-Sync', 'terrabrush'

New-Item -ItemType Directory -Force (Split-Path $Log) | Out-Null
Set-Content $Log "--- worktree at $Commit" -Encoding Default
function Say($text) { Write-Host $text; Add-Content $Log $text }
function Fail($text) { Say "FAILED: $text"; exit 1 }
# cmd does the redirect: PowerShell 5.1 turns a native stderr line into an error record
function Run($exe, $arguments) { cmd /c "`"$exe`" $arguments >> `"$Log`" 2>&1"; return $LASTEXITCODE }
function Robo($from, $to, $extra) {
    cmd /c "robocopy `"$from`" `"$to`" /E /MT:16 /NFL /NDL /NJH /NP $extra >> `"$Log`" 2>&1"
    if ($LASTEXITCODE -ge 8) { Fail "robocopy $from exit $LASTEXITCODE" }
}

if (Test-Path $wt) { Fail 'Export worktree already exists; choose a fresh path (storage policy)' }
git -C $repo worktree add --detach $wt $Commit 2>&1 | Add-Content $Log
if ($LASTEXITCODE -ne 0) { Fail "git worktree add $Commit" }
if ($Primary) {
    # Owner ruling 2026-10-25: rejected voice pilot; public opening is text-only.
    foreach ($voiceRoot in @((Join-Path $Primary 'vo'), (Join-Path $Out 'fallback\vo'))) {
        if (Test-Path $voiceRoot) {
            if (Get-ChildItem -LiteralPath $voiceRoot -File -Filter 'opening.*.ogg') {
                Fail 'Rejected opening voice files are present; stage a text-only public payload'
            }
        }
    }
    Say '--- full-resolution compressed texture preflight'
    $code = Run 'python' "-P `"$wt\tools\release\check_texture_cache.py`" `"$Out`" `"$Out\..\texture_cache_export_audit.json`""
    if ($code -ne 0) { Fail 'large art texture cache is missing/stale/uncompressed/downscaled (see audit)' }
}
$proj = "$wt\src\Presentation"
Say '--- copy addons and import cache'
Robo "$repo\src\Presentation\addons" "$proj\addons" ('/XD ' + ($unshipped -join ' '))
Robo "$repo\src\Presentation\.godot" "$proj\.godot" '/XD mono'
# The cache lists the extensions the shared checkout loaded; Godot rebuilds the list from the addons that are here.
Remove-Item "$proj\.godot\extension_list.cfg" -ErrorAction SilentlyContinue
# Godot AI is an editor-only tool: its plugin writes a game-helper autoload into project.godot, and exports exclude the add-on,
# so the shipped game would fail to create the autoload at start. Drop both editor-only lines from the worktree copy.
$pg = "$proj\project.godot"
(Get-Content $pg) | Where-Object { $_ -notmatch '^_mcp_game_helper=' -and $_ -notmatch 'res://addons/godot_ai/plugin\.cfg' } | Set-Content $pg -Encoding utf8
git -C $wt status --short | Add-Content $Log

New-Item -ItemType Directory -Force $Out | Out-Null
Say '--- import'
$code = Run $engine "--headless --path `"$proj`" --import"
Say "import exit $code"
if ($code -ne 0) {
    # The first import of a fresh worktree has crashed in the font importer; the second run resumes from what was imported.
    Say '--- import again'
    $code = Run $engine "--headless --path `"$proj`" --import"
    Say "import exit $code"
}
if ($code -ne 0) { Fail "import exit $code" }
Say '--- build'
$code = Run $engine "--headless --path `"$proj`" --build-solutions --quit"
Say "build exit $code"
if ($code -ne 0) { Fail "build exit $code" }
Say '--- export'
$code = Run $engine "--headless --path `"$proj`" --export-release `"Windows Playtest`" `"$Out\Otherreach.exe`""
Say "export exit $code"
if ($code -ne 0) { Fail "export exit $code" }

# Godot exits 0 after a failed extension load or a failed pack, so read the log as well.
$plain = (Get-Content $Log) -replace '\x1b\[[0-9;]*m', ''
$bad = $plain | Select-String -Pattern 'ERROR: .*(GDExtension|extension|export|pack)|Error loading extension|error CS\d+|Failed to (export|save|open)|Cannot export|Export failed' | Select-Object -First 10
if ($bad) { Fail ("errors in the Godot output:`n" + ($bad.Line -join "`n")) }
$exe = Get-Item "$Out\Otherreach.exe" -ErrorAction SilentlyContinue
if (-not $exe -or $exe.Length -lt 100MB) { Fail 'Otherreach.exe is missing or too small' }
if (-not (Get-ChildItem $Out -Directory -Filter 'data_*')) { Fail 'the exe data folder is missing' }
if ($Primary) {
    Say '--- music into the package'
    $code = Run 'python' "-P `"$wt\tools\audio_pipeline\recovery_20261016\install.py`" C:\Otherreach_Recovery\asset_work\audio_recovery `"$Primary`""
    if ($code -ne 0) { Fail "music install exit $code" }
    $code = Run 'python' "-P `"$wt\tools\audio_pipeline\recovery_20261016\check_music.py`" `"$Primary`""
    if ($code -ne 0) { Fail 'the package is missing music (see the log)' }
    Say '--- recorded Waystone touch, bind and arrival into the package'
    $waystone = Join-Path (Split-Path $Out) 'waystone_audio_delivery'
    $code = Run 'python' "-P `"$wt\tools\audio_pipeline\build_waystone_delivery.py`" `"$Primary`" `"$waystone`" --ffmpeg C:\Otherreach_Worker\tools\ffmpeg\bin\ffmpeg.exe"
    if ($code -ne 0) { Fail "Waystone delivery build exit $code" }
    $code = Run 'python' "-P `"$wt\tools\audio_pipeline\recovery_20261016\install.py`" `"$waystone`" `"$Primary`""
    if ($code -ne 0) { Fail "Waystone delivery install exit $code" }
}
Say (Get-ChildItem $Out | Select-Object Name, Length | Format-Table -AutoSize | Out-String)
exit 0
