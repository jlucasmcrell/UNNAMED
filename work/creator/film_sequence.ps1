# Film the D12 proof, joining the GPU queue until a run produces frames, then encode.
$ErrorActionPreference = 'Continue'
$root = 'D:\Otherreach_Scratch\codex_f1_siann_wt'
$log = Join-Path $root 'work\creator\film_sequence.log'
function Say($text) { $line = "$(Get-Date -Format HH:mm:ss) $text"; Write-Host $line; Add-Content -Path $log -Value $line }
for ($attempt = 1; $attempt -le 40; $attempt++) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'work\creator\run_d12_film.ps1') -Label d12proof05 *> (Join-Path $root 'work\creator\d12proof05.wrapper.log')
    $frames = (Get-ChildItem (Join-Path $root 'work\creator\out\d12proof05') -Filter *.png -ErrorAction SilentlyContinue | Measure-Object).Count
    Say "attempt $attempt wrote $frames frames"
    if ($frames -gt 100) { break }
    Start-Sleep -Seconds 20
}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'work\creator\encode_d12.ps1') *> (Join-Path $root 'work\creator\encode_d12.log')
Say 'film sequence finished'
