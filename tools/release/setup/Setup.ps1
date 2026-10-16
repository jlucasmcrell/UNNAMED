# Otherreach playtest installer. Windows PowerShell 5.1, no administrator rights.
#   Setup.cmd                              download the parts named in install-manifest.json and install beside this script
#   Setup.cmd -InstallDir "D:\Games\Otherreach"
#   Setup.cmd -LocalSource "D:\parts"      take the parts from a folder instead of the web (nothing is downloaded or deleted)
# Rerunning resumes: a part that was downloaded in part continues where it stopped, and a part whose files are all installed is skipped.
param(
    [string]$InstallDir,
    [string]$LocalSource,
    [string]$ManifestPath = (Join-Path $PSScriptRoot 'install-manifest.json'),
    [string]$UrlTemplate   # replaces the manifest's download address (a mirror, or a test server); {name} stands for the part's file name
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$officialPrefix = 'https://github.com/jlucasmcrell/otherreach-playtest/releases/download/'
$spareBytes = 512MB
$lock = $null

function Format-Size([long]$bytes) { if ($bytes -ge 1GB) { '{0:N2} GB' -f ($bytes / 1GB) } else { '{0:N0} MB' -f ($bytes / 1MB) } }

# A package path becomes a full path under the install folder, or an error: zip-slip, drive letters, reserved names.
function Resolve-PackagePath([string]$relative) {
    if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or $relative -match '[\x00-\x1f<>:"|?*\\]') { throw "Unsafe path in the file list: $relative" }
    foreach ($segment in $relative.Split('/')) {
        if ($segment -eq '' -or $segment -eq '.' -or $segment -eq '..' -or $segment.EndsWith('.') -or $segment.EndsWith(' ') -or $segment -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)') { throw "Unsafe path in the file list: $relative" }
    }
    $full = [IO.Path]::GetFullPath((Join-Path $script:root ($relative.Replace('/', '\'))))
    if (-not $full.StartsWith($script:root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Path leaves the install folder: $relative" }
    return $full
}

function Get-Sha256([string]$path) {
    $stream = [IO.File]::OpenRead($path); $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}

function Test-Part([string]$path, $part) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
    if ((Get-Item -LiteralPath $path).Length -ne [long]$part.bytes) { return $false }
    return (Get-Sha256 $path) -eq $part.sha256
}

# Downloads $url to $destination, continuing a partial file (HTTP Range) and retrying a dropped connection.
function Get-Download([string]$url, [string]$destination, [long]$expected) {
    $partial = $destination + '.download'
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        $have = if (Test-Path -LiteralPath $partial) { (Get-Item -LiteralPath $partial).Length } else { 0L }
        if ($have -gt $expected) { Remove-Item -LiteralPath $partial; $have = 0L }
        if ($have -eq $expected) { break }
        $response = $null; $out = $null; $in = $null
        try {
            $request = [Net.HttpWebRequest]::Create($url)
            $request.UserAgent = 'OtherreachSetup/1'; $request.Timeout = 60000; $request.ReadWriteTimeout = 60000
            if ($have -gt 0) { $request.AddRange([long]$have) }
            $response = $request.GetResponse()
            if ($have -gt 0 -and [int]$response.StatusCode -ne 206) { $have = 0L }   # the server ignored the range: start the part again
            $out = [IO.File]::Open($partial, $(if ($have -gt 0) { [IO.FileMode]::Append } else { [IO.FileMode]::Create }), [IO.FileAccess]::Write, [IO.FileShare]::None)
            $in = $response.GetResponseStream()
            $buffer = New-Object byte[] (1MB)
            $position = $have; $clock = [Diagnostics.Stopwatch]::StartNew(); $lastShown = 0L; $startedAt = $have
            while (($read = $in.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $out.Write($buffer, 0, $read); $position += $read
                if ($clock.ElapsedMilliseconds - $lastShown -ge 1000) {
                    $lastShown = $clock.ElapsedMilliseconds
                    $speed = ($position - $startedAt) / [Math]::Max(1, $clock.Elapsed.TotalSeconds) / 1MB
                    Write-Host ("`r    {0:N0} of {1:N0} MB ({2:N0}%), {3:N1} MB/s   " -f ($position / 1MB), ($expected / 1MB), (100 * $position / $expected), $speed) -NoNewline
                }
            }
            Write-Host ("`r    {0:N0} of {1:N0} MB                              " -f ($position / 1MB), ($expected / 1MB))
        } catch {
            Write-Host ''
            $status = if ($_.Exception.InnerException -is [Net.WebException] -and $_.Exception.InnerException.Response) { [int]$_.Exception.InnerException.Response.StatusCode } else { 0 }
            if ($status -eq 416 -or $status -eq 404 -or $status -eq 403) {
                if ($status -eq 416) { Remove-Item -LiteralPath $partial -ErrorAction SilentlyContinue; continue }
                throw "The server answered $status for $url - the part is not published at that address."
            }
            Write-Host "    Connection problem ($($_.Exception.Message)); trying again in $($attempt * 4) s (attempt $attempt of 6)." -ForegroundColor Yellow
            Start-Sleep -Seconds ($attempt * 4)
        } finally {
            if ($in) { $in.Dispose() }; if ($out) { $out.Dispose() }; if ($response) { $response.Close() }
        }
    }
    if (-not (Test-Path -LiteralPath $partial) -or (Get-Item -LiteralPath $partial).Length -ne $expected) {
        throw "The download of $(Split-Path $destination -Leaf) did not finish. Check your connection and run Setup.cmd again; it continues from where it stopped."
    }
    Move-Item -LiteralPath $partial -Destination $destination -Force
}

function Expand-Part($part, [string]$archivePath, $filesByPath) {
    $zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
    $buffer = New-Object byte[] (4MB)
    try {
        $names = @{}
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }
            $record = $filesByPath[$entry.FullName]
            if ($null -eq $record -or [int]$record.part -ne [int]$part.number) { throw "$($part.name) holds a file that is not in the file list: $($entry.FullName)" }
            if ($entry.Length -ne [long]$record.bytes) { throw "$($part.name): $($entry.FullName) has the wrong size" }
            $names[$entry.FullName] = $true
            $destination = Resolve-PackagePath $entry.FullName
            $existing = New-Object IO.FileInfo($destination)
            if ($existing.Exists -and $existing.Length -eq $entry.Length) { continue }   # finished by an earlier run
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
            $staging = $destination + '.partial'
            $sha = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
            $in = $entry.Open(); $out = [IO.File]::Open($staging, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                while (($read = $in.Read($buffer, 0, $buffer.Length)) -gt 0) { $sha.AppendData($buffer, 0, $read); $out.Write($buffer, 0, $read) }
            } finally { $out.Dispose(); $in.Dispose() }
            if ([BitConverter]::ToString($sha.GetHashAndReset()).Replace('-', '').ToLowerInvariant() -ne $record.sha256) {
                Remove-Item -LiteralPath $staging
                throw "$($entry.FullName) failed its checksum after extraction (the part may be damaged)"
            }
            if ($existing.Exists) { Remove-Item -LiteralPath $destination -Force }
            [IO.File]::SetLastWriteTime($staging, $entry.LastWriteTime.LocalDateTime)
            [IO.File]::Move($staging, $destination)
        }
        $missing = @($filesByPath.Values | Where-Object { [int]$_.part -eq [int]$part.number -and -not $names.ContainsKey($_.path) })
        if ($missing.Count -gt 0) { throw "$($part.name) lacks $($missing.Count) files from the file list, for example $($missing[0].path)" }
    } finally { $zip.Dispose() }
}

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]3072
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Add-Type -AssemblyName System.Web.Extensions

    if ($LocalSource -and -not (Test-Path -LiteralPath $LocalSource -PathType Container)) { throw "The -LocalSource folder does not exist: $LocalSource" }
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf) -and $LocalSource) { $ManifestPath = Join-Path $LocalSource 'install-manifest.json' }
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) { throw "install-manifest.json is missing. Extract the whole Setup zip and run Setup.cmd from that folder." }
    $json = New-Object Web.Script.Serialization.JavaScriptSerializer
    $json.MaxJsonLength = [int]::MaxValue
    $manifest = $json.DeserializeObject([IO.File]::ReadAllText($ManifestPath))

    if ($manifest.schema -ne 1) { throw 'This Setup does not understand the manifest version.' }
    if ($manifest.install_folder -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') { throw 'The manifest names an invalid install folder.' }
    $template = if ($UrlTemplate) { $UrlTemplate } else { [string]$manifest.url_template }
    if (-not $LocalSource) {
        if ($template -notmatch '\{name\}') { throw 'The download address has no {name} in it.' }
        if (-not $UrlTemplate -and -not $template.StartsWith($officialPrefix)) { throw "The manifest points outside $officialPrefix - refusing to download." }
    }
    $parts = @(); $number = 0
    foreach ($entry in $manifest.parts) {
        $number++
        if ([string]$entry.name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\.zip$' -or [string]$entry.sha256 -notmatch '^[a-f0-9]{64}$' -or [long]$entry.bytes -le 0) { throw "Invalid part in the manifest: $($entry.name)" }
        $parts += [pscustomobject]@{ number = $number; name = [string]$entry.name; bytes = [long]$entry.bytes; sha256 = [string]$entry.sha256; raw = [long]$entry.uncompressed_bytes }
    }
    if ($parts.Count -eq 0) { throw 'The manifest lists no parts.' }

    if (-not $InstallDir) { $InstallDir = Join-Path $PSScriptRoot ([string]$manifest.install_folder) }
    $script:root = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
    $filesByPath = @{}; $partFiles = @{}
    foreach ($entry in $manifest.files) {
        if ([string]$entry.sha256 -notmatch '^[a-f0-9]{64}$') { throw "Invalid checksum for $($entry.path)" }
        $null = Resolve-PackagePath ([string]$entry.path)
        $record = [pscustomobject]@{ path = [string]$entry.path; bytes = [long]$entry.bytes; sha256 = [string]$entry.sha256; part = [int]$entry.part }
        $filesByPath[$record.path] = $record
        if (-not $partFiles.ContainsKey($record.part)) { $partFiles[$record.part] = New-Object Collections.Generic.List[object] }
        $partFiles[$record.part].Add($record)
    }

    Write-Host "Otherreach playtest $($manifest.label)"
    Write-Host "Installing to: $script:root"
    if ($LocalSource) { Write-Host "Parts from:    $LocalSource" }

    # which parts still have files to install
    $pending = @()
    foreach ($part in $parts) {
        $complete = $true
        foreach ($record in $partFiles[$part.number]) {
            $info = New-Object IO.FileInfo((Resolve-PackagePath $record.path))
            if (-not $info.Exists -or $info.Length -ne $record.bytes) { $complete = $false; break }
        }
        if (-not $complete) { $pending += $part }
    }

    if ($pending.Count -gt 0) {
        $need = ($pending | Measure-Object raw -Sum).Sum + $spareBytes
        if (-not $LocalSource) { $need += ($pending | Measure-Object bytes -Maximum).Maximum }
        $driveRoot = [IO.Path]::GetPathRoot($script:root)
        if ($driveRoot.StartsWith('\\')) { Write-Host 'Network folder: free space was not checked.' -ForegroundColor Yellow }
        else {
            $free = (New-Object IO.DriveInfo($driveRoot)).AvailableFreeSpace
            if ($free -lt $need) { throw "Not enough free space on $driveRoot - the install needs $(Format-Size $need) and $(Format-Size $free) is free. Free some space or choose another folder: Setup.cmd -InstallDir `"D:\Otherreach`"" }
            Write-Host "Disk space:    $(Format-Size $free) free, $(Format-Size $need) needed"
        }
    }

    [IO.Directory]::CreateDirectory($script:root) | Out-Null
    $workDir = Join-Path $script:root '.setup_download'
    [IO.Directory]::CreateDirectory($workDir) | Out-Null
    $lock = [IO.File]::Open((Join-Path $workDir 'setup.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)

    foreach ($part in $parts) {
        $label = "[$($part.number)/$($parts.Count)]"
        if ($pending.number -notcontains $part.number) { Write-Host "$label $($part.name): already installed."; continue }
        if ($LocalSource) {
            $archive = Join-Path $LocalSource $part.name
            Write-Host "$label Checking $($part.name)..."
            if (-not (Test-Part $archive $part)) { throw "$($part.name) in $LocalSource is missing or damaged (expected $($part.bytes) bytes, SHA-256 $($part.sha256))." }
        } else {
            $archive = Join-Path $workDir $part.name
            for ($try = 1; $try -le 2; $try++) {
                if (-not (Test-Path -LiteralPath $archive)) {
                    Write-Host "$label Downloading $($part.name) ($(Format-Size $part.bytes))..."
                    Get-Download ($template.Replace('{name}', $part.name)) $archive $part.bytes
                } else { Write-Host "$label Using the downloaded $($part.name)..." }
                Write-Host '    Verifying SHA-256...'
                if (Test-Part $archive $part) { break }
                Remove-Item -LiteralPath $archive -Force
                if ($try -eq 2) { throw "$($part.name) failed its SHA-256 check twice. The file on the server may be damaged; try again later." }
                Write-Host '    The part failed its check; downloading it once more.' -ForegroundColor Yellow
            }
        }
        Write-Host "    Extracting $($partFiles[$part.number].Count) files..."
        Expand-Part $part $archive $filesByPath
        if (-not $LocalSource) { Remove-Item -LiteralPath $archive -Force }
    }

    [IO.Directory]::CreateDirectory((Join-Path $script:root 'saves')) | Out-Null
    $lock.Dispose(); $lock = $null
    Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host 'Clearing the downloaded-file flag (Unblock-File)...'
    try { Get-ChildItem -LiteralPath $script:root -Recurse -File -Force | Unblock-File -ErrorAction Stop }
    catch { Write-Host "    Unblock-File was not fully applied: $($_.Exception.Message)" -ForegroundColor Yellow }

    $bad = @($filesByPath.Values | Where-Object { $i = New-Object IO.FileInfo((Resolve-PackagePath $_.path)); -not $i.Exists -or $i.Length -ne $_.bytes })
    if ($bad.Count -gt 0) { throw "$($bad.Count) files are missing or the wrong size after install, for example $($bad[0].path)" }
    Write-Host ''
    Write-Host "Installed $($filesByPath.Count) files ($(Format-Size ($filesByPath.Values | Measure-Object bytes -Sum).Sum)), every one checked against its SHA-256." -ForegroundColor Green
    Write-Host "Open $script:root and double-click PLAY.bat. Your saves go in its saves folder."
    exit 0
} catch {
    Write-Host ''
    Write-Host "Setup stopped: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Nothing was uninstalled. Fix the problem and run Setup.cmd again; it continues where it stopped.'
    exit 1
} finally {
    if ($null -ne $lock) { $lock.Dispose() }
}
