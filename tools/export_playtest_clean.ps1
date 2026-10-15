# The playtest export (from the UI pass, 2026-10-15), from a clean worktree at a commit, so nothing uncommitted in the shared checkout (another
# agent's work in progress) is compiled into the build. The gitignored addons/ (less godot_ai, which exports exclude) and the .godot import
# cache are copied in, so the import is quick.
# usage: powershell -File export_clean.ps1 <commit> <out dir> <log>
param([string]$Commit, [string]$Out, [string]$Log)
$ErrorActionPreference = 'Continue'; $ProgressPreference = 'SilentlyContinue'
$repo = 'C:\Otherreach_Recovery\UNNAMED'
# Outside C:\Otherreach_Recovery, which Google Drive syncs: it held the old worktree's files open, and 2 GB of copies would upload.
$wt = 'C:\Otherreach_Build\export_wt'
$tools = 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31'
$engine = "$tools\godot\Godot_v4.7.2-stable_mono_win64_console.exe"
$env:DOTNET_ROOT = 'C:\Program Files\dotnet'; $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:PATH = "C:\Program Files\dotnet;$env:PATH"
$env:GODOT_AI_DISABLE_TELEMETRY = '1'

"--- worktree at $Commit" | Tee-Object $Log
if (Test-Path $wt) { git -C $repo worktree remove --force $wt *>> $Log }
git -C $repo worktree add --detach $wt $Commit *>> $Log
$proj = "$wt\src\Presentation"
"--- copy addons and import cache" | Tee-Object $Log -Append
robocopy "$repo\src\Presentation\addons" "$proj\addons" /E /MT:16 /NFL /NDL /NJH /NP /XD godot_ai *>> $Log
robocopy "$repo\src\Presentation\.godot" "$proj\.godot" /E /MT:16 /NFL /NDL /NJH /NP /XD mono *>> $Log
# Godot AI is an editor-only tool: its plugin writes a game-helper autoload into project.godot, and exports exclude the add-on,
# so the shipped game would fail to create the autoload at start. Drop both editor-only lines from the worktree copy.
$pg = "$proj\project.godot"
(Get-Content $pg) | Where-Object { $_ -notmatch '^_mcp_game_helper=' -and $_ -notmatch 'res://addons/godot_ai/plugin\.cfg' } | Set-Content $pg -Encoding utf8
git -C $wt status --short | Tee-Object $Log -Append

New-Item -ItemType Directory -Force $Out | Out-Null
"--- import" | Tee-Object $Log -Append
& $engine --headless --path $proj --import *>> $Log
"import exit $LASTEXITCODE" | Tee-Object $Log -Append
"--- build" | Tee-Object $Log -Append
& $engine --headless --path $proj --build-solutions --quit *>> $Log
"build exit $LASTEXITCODE" | Tee-Object $Log -Append
"--- export" | Tee-Object $Log -Append
& $engine --headless --path $proj --export-release 'Windows Playtest' "$Out\Otherreach.exe" *>> $Log
"export exit $LASTEXITCODE" | Tee-Object $Log -Append
Get-ChildItem $Out | Select-Object Name, Length | Format-Table -AutoSize | Out-String | Tee-Object $Log -Append
