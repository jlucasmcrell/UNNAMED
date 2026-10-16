# Splits the assembled playtest folder into zip parts (each at most 1.9 GiB, under the 2 GiB GitHub release asset limit) and writes
# install-manifest.json: every part's name, size and SHA-256, every file's path, size, SHA-256 and part, and the download URL template.
# It also builds the small Setup zip (Setup.cmd, Setup.ps1, the manifest). Nothing is uploaded.
# usage: powershell -File tools\release\package_parts.ps1 -Label 20261017a [-Tag <release tag>]
# Output: D:\Otherreach_Scratch\release\<Label>\parts\
param(
    [Parameter(Mandatory)][ValidatePattern('^\d{8}[a-z]$')][string]$Label,
    [string]$Tag,
    [string]$OutRoot = 'D:\Otherreach_Scratch\release',
    [long]$PartLimit = 1.9GB,
    [string]$BaseUrl = 'https://github.com/jlucasmcrell/otherreach-playtest/releases/download/'
)
$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'
if (-not $Tag) { $Tag = "playtest-$Label" }
if ($Tag -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') { throw "Bad release tag: $Tag" }
$here = Split-Path $MyInvocation.MyCommand.Path
$game = "$OutRoot\$Label\game"; $parts = "$OutRoot\$Label\parts"
if (-not (Test-Path "$game\Otherreach.exe")) { throw "$game is not an assembled folder; run build_playtest.ps1 first" }
if (Test-Path $parts) { throw "$parts already exists" }
if ((Get-PSDrive D).Free -lt 25GB) { throw 'D: has under 25 GB free' }
New-Item -ItemType Directory $parts | Out-Null
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$installFolder = "Otherreach-Playtest-$Label"
$prefix = "$installFolder-part-"
$limit = $PartLimit - 4MB   # headroom for the central directory
$root = (Get-Item $game).FullName.TrimEnd('\') + '\'
$files = Get-ChildItem $game -Recurse -File -Force | Sort-Object { $_.FullName } -Culture ([cultureinfo]::InvariantCulture)
$buffer = New-Object byte[] (4MB)
$partRecords = New-Object Collections.Generic.List[object]
$fileRecords = New-Object Collections.Generic.List[object]
$stream = $null; $zip = $null; $current = $null

function Get-FileSha256([string]$path) {
    $s = [IO.File]::OpenRead($path); $h = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($h.ComputeHash($s)).Replace('-', '').ToLowerInvariant() } finally { $h.Dispose(); $s.Dispose() }
}
function Close-Part {
    if ($null -eq $script:zip) { return }
    $script:zip.Dispose(); $script:stream.Dispose(); $script:zip = $null; $script:stream = $null
    $info = Get-Item $script:current.path
    if ($info.Length -gt $PartLimit) { throw "$($info.Name) is $($info.Length) bytes, over the limit" }
    $script:partRecords.Add([ordered]@{ name = $info.Name; bytes = $info.Length; sha256 = (Get-FileSha256 $info.FullName); uncompressed_bytes = $script:current.raw; file_count = $script:current.count })
    "  $($info.Name): $('{0:N0}' -f $info.Length) bytes, $($script:current.count) files"
}
function Open-Part {
    $n = $script:partRecords.Count + 1
    $path = '{0}\{1}{2:D3}.zip' -f $parts, $prefix, $n
    $script:stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    $script:zip = New-Object IO.Compression.ZipArchive($script:stream, [IO.Compression.ZipArchiveMode]::Create, $false)
    $script:current = @{ path = $path; raw = 0L; count = 0; number = $n }
}

"Packing $($files.Count) files from $game"
try {
    foreach ($file in $files) {
        if ($file.Length -gt $limit) { throw "$($file.FullName) is larger than a part" }
        if ($null -ne $zip -and $current.count -gt 0 -and ($stream.Position + $file.Length + 1MB) -gt $limit) { Close-Part }
        if ($null -eq $zip) { Open-Part }
        $relative = $file.FullName.Substring($root.Length).Replace('\', '/')
        $entry = $zip.CreateEntry($relative, [IO.Compression.CompressionLevel]::Fastest)
        $entry.LastWriteTime = [DateTimeOffset]$file.LastWriteTime
        $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
        $in = [IO.File]::OpenRead($file.FullName); $out = $entry.Open()
        try {
            while (($read = $in.Read($buffer, 0, $buffer.Length)) -gt 0) { $hash.AppendData($buffer, 0, $read); $out.Write($buffer, 0, $read) }
        } finally { $out.Dispose(); $in.Dispose() }
        $fileRecords.Add([ordered]@{ path = $relative; bytes = $file.Length; sha256 = ([BitConverter]::ToString($hash.GetHashAndReset()).Replace('-', '').ToLowerInvariant()); part = $current.number })
        $current.raw += $file.Length; $current.count++
    }
    Close-Part
} catch {
    if ($zip) { $zip.Dispose() }; if ($stream) { $stream.Dispose() }
    throw
}

$manifest = [ordered]@{
    schema = 1
    label = $Label
    release_tag = $Tag
    install_folder = $installFolder
    url_template = "$BaseUrl$Tag/{name}"
    total_uncompressed_bytes = [long]($fileRecords | Measure-Object { $_.bytes } -Sum).Sum
    parts = $partRecords
    files = $fileRecords
}
$manifestPath = "$parts\install-manifest.json"
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding $false))

# the small starter zip: Setup.cmd + Setup.ps1 + the manifest it trusts
$setupZip = "$parts\$installFolder-Setup.zip"
$starter = "$OutRoot\$Label\setup_staging"
New-Item -ItemType Directory $starter | Out-Null
Copy-Item "$here\setup\*" $starter
Copy-Item $manifestPath $starter
Compress-Archive -Path "$starter\*" -DestinationPath $setupZip
Remove-Item -Recurse -Force $starter

$partBytes = [long]($partRecords | Measure-Object { $_.bytes } -Sum).Sum
"Parts: $($partRecords.Count), download $('{0:N2}' -f ($partBytes / 1GB)) GB (+ Setup zip $('{0:N1}' -f ((Get-Item $setupZip).Length / 1KB)) KB), unpacked $('{0:N2}' -f ($manifest.total_uncompressed_bytes / 1GB)) GB"
"Manifest $manifestPath"
