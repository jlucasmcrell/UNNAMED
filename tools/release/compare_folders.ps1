# Compares two folders file by file (relative path, size, SHA-256). Exit 0 when they are identical.
# usage: powershell -File tools\release\compare_folders.ps1 <expected folder> <actual folder> [-Ignore <relative path>...]
param(
    [Parameter(Mandatory)][string]$Expected,
    [Parameter(Mandatory)][string]$Actual,
    [string[]]$Ignore = @()
)
$ErrorActionPreference = 'Stop'

function Get-Tree([string]$folder) {
    $root = (Get-Item -LiteralPath $folder).FullName.TrimEnd('\') + '\'
    $tree = @{}
    foreach ($file in Get-ChildItem -LiteralPath $folder -Recurse -File -Force) {
        $relative = $file.FullName.Substring($root.Length)
        if ($Ignore -notcontains $relative) { $tree[$relative.ToLowerInvariant()] = $file }
    }
    return $tree
}
function Get-Sha256([string]$path) {
    $stream = [IO.File]::OpenRead($path); $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($stream)) } finally { $sha.Dispose(); $stream.Dispose() }
}

$a = Get-Tree $Expected; $b = Get-Tree $Actual
$problems = New-Object Collections.Generic.List[string]
foreach ($key in $a.Keys) { if (-not $b.ContainsKey($key)) { $problems.Add("missing: $key") } }
foreach ($key in $b.Keys) { if (-not $a.ContainsKey($key)) { $problems.Add("extra: $key") } }
$checked = 0; $bytes = 0L
foreach ($key in $a.Keys) {
    if (-not $b.ContainsKey($key)) { continue }
    if ($a[$key].Length -ne $b[$key].Length) { $problems.Add("size differs: $key"); continue }
    if ((Get-Sha256 $a[$key].FullName) -ne (Get-Sha256 $b[$key].FullName)) { $problems.Add("content differs: $key") }
    $checked++; $bytes += $a[$key].Length
}
"Compared $checked files, $('{0:N2}' -f ($bytes / 1GB)) GB, SHA-256 each side"
if ($problems.Count -gt 0) { $problems | Select-Object -First 30; "DIFFERENT: $($problems.Count) problems"; exit 1 }
'IDENTICAL'
