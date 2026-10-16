# Export one Unreal content pack to FBX + PNG + per-mesh JSON manifests (see README.txt).
#   powershell -ExecutionPolicy Bypass -File export_pack.ps1 <uproject> <PackName> <outdir> [-Source fab|cosmos] [-Match regex] [-TexturesOnly] [-Animations]
param(
    [Parameter(Mandatory = $true)][string]$UProject,
    [Parameter(Mandatory = $true)][string]$Pack,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [ValidateSet("fab", "cosmos")][string]$Source = "fab",
    [string]$Match = "",
    [switch]$SkipExisting,
    [switch]$TexturesOnly,
    [switch]$Animations,
    [string]$Engine = "C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor-Cmd.exe"
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force $OutDir | Out-Null
# The pythonscript commandlet unescapes backslashes inside -script (\t, \u ...): pass every path with forward slashes.
$script = (Join-Path $PSScriptRoot "export_pack.py") -replace '\\', '/'
$out = (Resolve-Path $OutDir).Path -replace '\\', '/'
$args_ = "$script --pack $Pack --out $out --source $Source"
if ($Match) { $args_ += " --match $Match" }
if ($SkipExisting) { $args_ += " --skip-existing" }
if ($TexturesOnly) { $args_ += " --textures-only" }
if ($Animations) { $args_ += " --animations" }
$log = Join-Path $OutDir "_export_log.txt"
$t0 = Get-Date
& $Engine ($UProject -replace '\\', '/') -run=pythonscript "-script=$args_" -unattended -nop4 -nosplash -NullRHI -stdout -FullStdOutLogOutput *> $log
$code = $LASTEXITCODE
$secs = [int]((Get-Date) - $t0).TotalSeconds
Select-String -Path $log -Pattern "export_pack: done|Assertion failed|Error: [A-Za-z]*Error" | Select-Object -First 4 | ForEach-Object { $_.Line }
"exit $code after $secs s; log $log"
if (-not (Test-Path (Join-Path $OutDir "_pack.json"))) { exit 1 }
exit $code
