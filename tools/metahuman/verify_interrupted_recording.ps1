param([Parameter(Mandatory)][string]$Ffmpeg,[Parameter(Mandatory)][string]$OutputDirectory)
# CPU-only regression: interrupted MP4 must retain decodable frames without a trailer.
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
$video=Join-Path $out 'interrupted.mp4'
if(Test-Path -LiteralPath $video){throw 'Choose a fresh regression output directory'}
New-Item -ItemType Directory -Path $out -Force | Out-Null
$owned=$null
try {
 $owned=Start-Process -FilePath $Ffmpeg -WindowStyle Hidden -PassThru -ArgumentList @('-hide_banner','-y','-re','-f','lavfi','-i','testsrc2=size=64x64:rate=30','-c:v','libx264','-threads','2','-preset','ultrafast','-pix_fmt','yuv420p','-force_key_frames','expr:gte(t,n_forced*1)','-flush_packets','1','-movflags','+frag_keyframe+empty_moov+default_base_moof',('"'+$video+'"')) -RedirectStandardOutput (Join-Path $out 'stdout.log') -RedirectStandardError (Join-Path $out 'stderr.log')
 Start-Sleep -Seconds 5
 if($owned.HasExited){throw 'Encoder exited before the intentional interruption'}
 $owned.Kill();$owned.WaitForExit()
 $probe=& (Join-Path (Split-Path -Parent $Ffmpeg) 'ffprobe.exe') -v error -count_frames -select_streams v:0 -show_entries stream=nb_read_frames:format=duration -of json $video
 if($LASTEXITCODE -ne 0){throw 'Interrupted recording is unreadable'}
 $data=$probe | ConvertFrom-Json
 if([int]$data.streams[0].nb_read_frames -lt 60){throw 'Fewer than two seconds survived interruption'}
 $probe | Set-Content -LiteralPath (Join-Path $out 'probe.json')
 'INTERRUPTED_FRAGMENTED_MP4_PASS'
}finally{if($owned -and -not $owned.HasExited){$owned.Kill();$owned.WaitForExit()}}
