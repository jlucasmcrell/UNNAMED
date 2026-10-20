param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string[]]$JobArguments,[Parameter(Mandatory)][string]$Log,[switch]$Gpu,[switch]$VisibleWindow,[string]$Job='native-job',[int]$LimitSeconds=1080)
$ErrorActionPreference='Stop'
$flag='C:\Otherreach_Recovery\OWNER_PLAYING.flag'
$lease='codex-mh-slice '+$Job
$owned=$null
$held=$false
if(Test-Path -LiteralPath $flag){throw 'Owner playing; job deferred'}
try {
 if($Gpu){
  for($attempt=1;$attempt -le 3;$attempt++){
   & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\invoke_gpu_lock.ps1" acquire $lease
   if($LASTEXITCODE -eq 0){break}
   if($attempt -eq 3){throw 'Documented GPU acquire failed after3 attempts'}
   Start-Sleep -Seconds 5
  }
  $held=$true
 }
 if(Test-Path -LiteralPath $flag){throw 'Owner playing; job deferred after acquire'}
 $owned=Start-Process -FilePath $Executable -ArgumentList $JobArguments -WindowStyle $(if($VisibleWindow){'Normal'}else{'Hidden'}) -PassThru -RedirectStandardOutput $Log -RedirectStandardError ($Log+'.stderr')
 $null=$owned.Handle # Keep the Windows handle so ExitCode survives process termination.
 $started=Get-Date
 while(-not $owned.HasExited){
  if(Test-Path -LiteralPath $flag){throw 'Owner playing; stopping owned job'}
  $age=((Get-Date)-$started).TotalSeconds
  if($age -gt $LimitSeconds){throw 'Owned job reached time limit'}
  if($Gpu -and $age -gt 540 -and (Get-Content 'C:\Otherreach_Recovery\gpu.queue' -ErrorAction SilentlyContinue | Where-Object {$_.Trim()})){throw 'Yielding GPU to queued lane'}
  Start-Sleep -Seconds 2
  $owned.Refresh()
 }
 $owned.WaitForExit()
 "Owned process finished: $($owned.ExitCode)"
 if($owned.ExitCode -ne 0){throw "Owned process failed: $($owned.ExitCode)"}
}finally{
 if($owned -and -not $owned.HasExited){
  $snapshot=Get-CimInstance Win32_Process
  $ownedIds=[Collections.Generic.List[int]]::new()
  $ownedIds.Add($owned.Id)
  for($i=0;$i -lt $ownedIds.Count;$i++){
   foreach($child in $snapshot | Where-Object {$_.ParentProcessId -eq $ownedIds[$i]}){$ownedIds.Add([int]$child.ProcessId)}
  }
  for($i=$ownedIds.Count-1;$i -ge 0;$i--){Stop-Process -Id $ownedIds[$i] -Force -ErrorAction SilentlyContinue}
 }
 if($held){ & powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\Otherreach_Recovery\gpu_lock.ps1' release $lease }
}
