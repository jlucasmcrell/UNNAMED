param([Parameter(Mandatory)][string]$Commit,[Parameter(Mandatory)][string]$Out,[Parameter(Mandatory)][string]$Log,[Parameter(Mandatory)][string]$Worktree)
$ErrorActionPreference='Stop'
$env:MSBUILDDISABLENODEREUSE='1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER='0'
# Run this wrapper through run_guarded.ps1 -Gpu. The authoritative exporter owns the clean checkout.
& 'C:\Otherreach_Recovery\UNNAMED\tools\export_playtest_clean.ps1' $Commit $Out $Log $Worktree
if($LASTEXITCODE -ne 0){throw "Clean playtest export failed: $LASTEXITCODE"}
