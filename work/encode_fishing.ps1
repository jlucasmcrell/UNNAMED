# Encode each fishing capture into a numbered-sequence mp4 (this ffmpeg build has no glob support).
$ff = 'C:\Otherreach_Worker\tools\ffmpeg\bin\ffmpeg.exe'
foreach ($d in @('F:\otherreach_captures\fishing_ingame','F:\otherreach_captures\fishing_creek_noon','F:\otherreach_captures\fishing_pond_noon','F:\otherreach_captures\fishing_visible')) {
    if (-not (Test-Path $d)) { continue }
    $frames = Get-ChildItem $d -Filter '*.png' | Sort-Object Name
    if ($frames.Count -eq 0) { "no frames: $d"; continue }
    $seq = Join-Path $d 'seq'
    New-Item -ItemType Directory -Force -Path $seq | Out-Null
    $i = 0
    foreach ($f in $frames) { Copy-Item $f.FullName (Join-Path $seq ('f{0:d4}.png' -f $i)) -Force; $i++ }
    $leaf = Split-Path $d -Leaf
    $mp4 = Join-Path $d ($leaf + '.mp4')
    & $ff '-y' '-framerate' '2' '-i' (Join-Path $seq 'f%04d.png') '-c:v' 'libx264' '-pix_fmt' 'yuv420p' '-crf' '20' $mp4 *> (Join-Path $d 'ffmpeg.log')
    if (Test-Path $mp4) { "$mp4 $([math]::Round((Get-Item $mp4).Length/1KB))KB" } else { "failed: $d" ; Get-Content (Join-Path $d 'ffmpeg.log') -Tail 2 }
}
