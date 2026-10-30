# Encode the captured frames into an mp4 and report what was captured (goal round 5).
param([string]$Folder = 'F:\otherreach_captures\fishing_ingame', [string]$Name = 'fishing_ingame')
$ErrorActionPreference = 'Continue'
$ffmpeg = 'C:\Otherreach_Worker\tools\ffmpeg\bin\ffmpeg.exe'
$frames = Get-ChildItem $Folder -Filter '*.png' -ErrorAction SilentlyContinue
"frames: $($frames.Count)"
if ($frames.Count -eq 0) { exit 0 }
# The capture writes numbered stills; ffmpeg wants a sequence, so order by name and rename a copy if needed.
$sorted = $frames | Sort-Object Name
$first = $sorted[0].BaseName
if ($first -notmatch '\d') {
    "first frame has no number: $first"
}
$mp4 = Join-Path $Folder ($Name + '.mp4')
& $ffmpeg -y -pattern_type glob -i (Join-Path $Folder '*.png') -framerate 12 -c:v libx264 -pix_fmt yuv420p -crf 20 $mp4 *> (Join-Path $Folder 'ffmpeg.log')
if (Test-Path $mp4) { "wrote $mp4 $([math]::Round((Get-Item $mp4).Length/1KB)) KB" } else { 'ffmpeg failed'; Get-Content (Join-Path $Folder 'ffmpeg.log') -Tail 4 }
