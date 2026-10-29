$ErrorActionPreference = 'Stop'
# Encode the D12 film frames into one clip per character, then both into one proof clip.
$ffmpeg = 'C:\Otherreach_Worker\tools\ffmpeg\bin\ffmpeg.exe'
$dir = 'D:\Otherreach_Scratch\codex_f1_siann_wt\work\creator\out\d12proof05'
$outDir = 'D:\Otherreach_Scratch\codex_f1_siann_wt\work\creator\out\d12proof05\clips'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$actions = 'idle', 'walk', 'run', 'turn', 'jump', 'stop'
$parts = @()
foreach ($who in 'siann', 'veth') {
    $list = Join-Path $outDir "$who.txt"
    Set-Content -Path $list -Value '' -Encoding ASCII
    foreach ($action in $actions) {
        $first = Get-ChildItem $dir -Filter "${who}_${action}_*.png" | Sort-Object Name | Select-Object -First 1
        if (-not $first) { continue }
        $mp4 = Join-Path $outDir "$who`_$action.mp4"
        & $ffmpeg -hide_banner -y -loglevel error -framerate 24 -start_number 0 -i (Join-Path $dir "${who}_${action}_%03d.png") -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p $mp4
        Add-Content -Path $list -Value ("file '" + $mp4.Replace("'", "'\''") + "'")
        $parts += $mp4
    }
    $joined = Join-Path $outDir "$who`_d12_proof.mp4"
    & $ffmpeg -hide_banner -y -loglevel error -f concat -safe 0 -i $list -c copy $joined
    Write-Host "wrote $joined"
}
Get-ChildItem $outDir -Filter '*_d12_proof.mp4' | Select-Object Name, Length | Format-Table -AutoSize
