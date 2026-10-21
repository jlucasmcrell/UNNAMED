param([string]$WindowTitle='Otherreach',[string]$Label='rcd5_normal_baseline',[int]$Seconds=360,[string]$ReadySignal='',[Parameter(Mandatory)][string]$Ffmpeg,[Parameter(Mandatory)][string]$OutputDirectory,[switch]$DesktopCapture,[string]$StopSignal='',[string]$StoppedSignal='')
# Run with PowerShell 7 (ProcessStartInfo.ArgumentList), not Windows PowerShell 5.1.
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $out -Force|Out-Null
$exe=[IO.Path]::GetFullPath($Ffmpeg)
$psi=[Diagnostics.ProcessStartInfo]::new($exe);$psi.UseShellExecute=$false;$psi.CreateNoWindow=$true;$psi.RedirectStandardInput=$true;$psi.RedirectStandardError=$true;$psi.RedirectStandardOutput=$true
foreach($arg in @('-hide_banner','-y','-f','gdigrab','-framerate','60','-draw_mouse','0','-i',$(if($DesktopCapture){'desktop'}else{'title='+$WindowTitle}),'-an','-c:v','h264_nvenc','-preset','p4','-rc','vbr','-cq','18','-b:v','0','-pix_fmt','yuv420p','-movflags','+faststart',(Join-Path $out ($Label+'.mp4')))){$psi.ArgumentList.Add($arg)}
if($PSVersionTable.PSVersion.Major -lt 7){throw 'Run this recorder with pwsh (PowerShell 7)'}
$p=[Diagnostics.Process]::Start($psi);$null=$p.Handle;$err=$p.StandardError.ReadToEndAsync();$stdout=$p.StandardOutput.ReadToEndAsync();$started=Get-Date
@{pid=$p.Id;started=$p.StartTime.ToString('o');executable=$exe;window=$WindowTitle;desktop_capture=[bool]$DesktopCapture;kind='REAL-TIME gdigrab60; no Godot write-movie/pacing override'}|ConvertTo-Json|Set-Content (Join-Path $out ($Label+'.process.json'))
try{
 Start-Sleep -Milliseconds 700
 if($p.HasExited){throw ('Recorder startup failed: '+$err.Result)}
 if($ReadySignal){[IO.File]::WriteAllText($ReadySignal,(Get-Date -Format o))}
 while(-not $p.HasExited -and ((Get-Date)-$started).TotalSeconds -lt $Seconds -and -not (Test-Path (Join-Path $out ($Label+'.stop'))) -and (-not $StopSignal -or -not(Test-Path -LiteralPath $StopSignal))){Start-Sleep -Seconds 1;$p.Refresh()}
 if(-not $p.HasExited){$p.StandardInput.WriteLine('q');if(-not $p.WaitForExit(15000)){$p.Kill()}}
 $p.WaitForExit();$err.Result|Set-Content (Join-Path $out ($Label+'.ffmpeg.log'));$stdout.Result|Set-Content (Join-Path $out ($Label+'.stdout.log'));Write-Output ('Recorder exit '+$p.ExitCode)
}finally{if(-not $p.HasExited){$p.StandardInput.WriteLine('q');$null=$p.WaitForExit(10000)};if($p.HasExited -and $StoppedSignal){[IO.File]::WriteAllText($StoppedSignal,(Get-Date -Format o))}}
