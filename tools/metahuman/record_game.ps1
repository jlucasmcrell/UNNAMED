param([string]$WindowTitle='Otherreach',[string]$Label='rcd5_normal_baseline',[int]$Seconds=360,[string]$ReadySignal='',[Parameter(Mandatory)][string]$Ffmpeg,[Parameter(Mandatory)][string]$OutputDirectory,[switch]$DesktopCapture,[switch]$GraphicsCapture,[string]$StopSignal='',[string]$StoppedSignal='',[string]$StopIfFlagExists='')
# Run with PowerShell 7 (ProcessStartInfo.ArgumentList), not Windows PowerShell 5.1.
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $out -Force|Out-Null
$exe=[IO.Path]::GetFullPath($Ffmpeg)
$psi=[Diagnostics.ProcessStartInfo]::new($exe);$psi.UseShellExecute=$false;$psi.CreateNoWindow=$true;$psi.RedirectStandardInput=$true;$psi.RedirectStandardError=$true;$psi.RedirectStandardOutput=$true
$captureArgs=if($GraphicsCapture){@('-f','lavfi','-i',"gfxcapture=window_title='$WindowTitle':max_framerate=60:capture_cursor=false",'-vf','hwdownload,format=bgra,format=yuv420p','-c:v','libx264','-preset','ultrafast','-crf','18')}else{@('-f','gdigrab','-framerate','60','-draw_mouse','0','-i',$(if($DesktopCapture){'desktop'}else{'title='+$WindowTitle}),'-c:v','h264_nvenc','-preset','p4','-rc','vbr','-cq','18','-b:v','0')}
$progressPath=Join-Path $out ($Label+'.progress')
if($GraphicsCapture){$captureArgs+=@('-progress',$progressPath)}
foreach($arg in (@('-hide_banner','-y')+$captureArgs+@('-an','-pix_fmt','yuv420p','-movflags','+faststart',(Join-Path $out ($Label+'.mp4'))))){$psi.ArgumentList.Add($arg)}
if($PSVersionTable.PSVersion.Major -lt 7){throw 'Run this recorder with pwsh (PowerShell 7)'}
$p=[Diagnostics.Process]::Start($psi);$null=$p.Handle;$err=$p.StandardError.ReadToEndAsync();$stdout=$p.StandardOutput.ReadToEndAsync();$started=Get-Date
@{pid=$p.Id;started=$p.StartTime.ToString('o');executable=$exe;window=$WindowTitle;desktop_capture=[bool]$DesktopCapture;graphics_capture=[bool]$GraphicsCapture;kind=$(if($GraphicsCapture){'REAL-TIME Windows Graphics Capture; software H264; no pacing override'}else{'REAL-TIME gdigrab60; no Godot write-movie/pacing override'})}|ConvertTo-Json|Set-Content (Join-Path $out ($Label+'.process.json'))
try{
 Start-Sleep -Milliseconds 700
 if($p.HasExited){throw ('Recorder startup failed: '+$err.Result)}
 if($GraphicsCapture -and $ReadySignal){
  $readyDeadline=(Get-Date).AddSeconds(30)
  while(-not(Test-Path -LiteralPath $progressPath) -or -not(Select-String -LiteralPath $progressPath -Pattern '^frame=[1-9][0-9]*$' -Quiet -ErrorAction SilentlyContinue)){
   $p.Refresh()
   if($p.HasExited){throw ('Recorder exited before its first frame: '+$err.Result)}
   if((Get-Date)-gt$readyDeadline){throw 'WGC produced no frame within30 seconds'}
   if($StopIfFlagExists -and (Test-Path -LiteralPath $StopIfFlagExists)){throw 'Owner flag appeared while waiting for first frame'}
   Start-Sleep -Milliseconds 100
  }
 }
 if($ReadySignal){[IO.File]::WriteAllText($ReadySignal,(Get-Date -Format o))}
 while(-not $p.HasExited -and ((Get-Date)-$started).TotalSeconds -lt $Seconds -and -not (Test-Path (Join-Path $out ($Label+'.stop'))) -and (-not $StopSignal -or -not(Test-Path -LiteralPath $StopSignal)) -and (-not $StopIfFlagExists -or -not(Test-Path -LiteralPath $StopIfFlagExists))){Start-Sleep -Seconds 1;$p.Refresh()}
 if(-not $p.HasExited){$p.StandardInput.WriteLine('q');if(-not $p.WaitForExit(15000)){$p.Kill()}}
 $p.WaitForExit();$err.Result|Set-Content (Join-Path $out ($Label+'.ffmpeg.log'));$stdout.Result|Set-Content (Join-Path $out ($Label+'.stdout.log'));Write-Output ('Recorder exit '+$p.ExitCode)
}finally{if(-not $p.HasExited){$p.StandardInput.WriteLine('q');$null=$p.WaitForExit(10000)};if($p.HasExited -and $StoppedSignal){[IO.File]::WriteAllText($StoppedSignal,(Get-Date -Format o))}}
