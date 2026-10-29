# One blocking sequence for the owner's last two proofs: the lining frame, then the camera-following film, then the encode.
$ErrorActionPreference = 'Stop'
$root = 'D:\Otherreach_Scratch\codex_f1_siann_wt'
$log = Join-Path $root 'work\creator\finish_sequence.log'
function Say($text) { $line = "$(Get-Date -Format HH:mm:ss) $text"; Write-Host $line; Add-Content -Path $log -Value $line }

# 1) The lining frame, joining the GPU queue until it is served.
for ($attempt = 1; $attempt -le 60; $attempt++) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\creator\run_creator_gate_b.ps1') -Label mask9 *> (Join-Path $root 'work\creator\mask9.log')
    if ($LASTEXITCODE -eq 0) { Say "lining frame captured on attempt $attempt"; break }
    $text = Get-Content (Join-Path $root 'work\creator\mask9.log') -Raw -ErrorAction SilentlyContinue
    if ($text -notmatch 'GPU acquire failed' -and $text -notmatch 'Time limit') { Say "lining frame run ended without the GPU: $LASTEXITCODE"; break }
    Start-Sleep -Seconds 20
}

# 2) The film, with the camera following the figure.
for ($attempt = 1; $attempt -le 60; $attempt++) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'work\creator\run_d12_film.ps1') -Label d12proof05 *> (Join-Path $root 'work\creator\d12proof05.wrapper.log')
    $pngs = (Get-ChildItem (Join-Path $root 'work\creator\out\d12proof05') -Filter '*.png' -ErrorAction SilentlyContinue | Measure-Object).Count
    if ($pngs -gt 100) { Say "film captured $pngs frames on attempt $attempt"; break }
    $wrapper = Get-Content (Join-Path $root 'work\creator\d12proof05.wrapper.log') -Raw -ErrorAction SilentlyContinue
    if ($wrapper -notmatch 'GPU acquire failed') { Say "film attempt $attempt ended with $pngs frames"; }
    Start-Sleep -Seconds 20
}

# 3) Encode whatever was filmed.
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'work\creator\encode_d12.ps1') > (Join-Path $root 'work\creator\encode_d12.log') 2>&1
Say 'sequence finished'
